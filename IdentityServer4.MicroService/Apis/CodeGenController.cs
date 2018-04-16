﻿using System;
using System.IO;
using System.Text;
using System.Net.Http;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.NodeServices;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Swashbuckle.AspNetCore.SwaggerGen;
using IdentityServer4.MicroService.Enums;
using IdentityServer4.MicroService.Services;
using IdentityServer4.MicroService.Models.Apis.Common;
using IdentityServer4.MicroService.Models.Apis.CodeGenController;
using IdentityServer4.MicroService.CacheKeys;
using static IdentityServer4.MicroService.AppConstant;
using static IdentityServer4.MicroService.MicroserviceConfig;

namespace IdentityServer4.MicroService.Apis
{
    /// <summary>
    /// 代码生成
    /// </summary>
    [Route("CodeGen")]
    [Produces("application/json")]
    [Authorize(AuthenticationSchemes = AppAuthenScheme, Roles = Roles.Users)]
    public class CodeGenController : BasicController
    {
        #region Services
        readonly SwaggerCodeGenService swagerCodeGen;
        readonly INodeServices nodeServices;
        // azure Storage
        readonly AzureStorageService storageService;
        #endregion

        #region 构造函数
        public CodeGenController(
            AzureStorageService _storageService,
            IStringLocalizer<CodeGenController> localizer,
            SwaggerCodeGenService _swagerCodeGen,
            INodeServices _nodeServices,
            RedisService _redis)
        {
            l = localizer;
            swagerCodeGen = _swagerCodeGen;
            nodeServices = _nodeServices;
            storageService = _storageService;
            redis = _redis;
        }
        #endregion

        #region 代码生成 - 客户端
        /// <summary>
        /// 代码生成 - 客户端
        /// </summary>
        /// <remarks>支持生成的客户端集合</remarks>
        [HttpGet("Clients")]
        [AllowAnonymous]
        [SwaggerOperation("CodeGen/Clients")]
        public ApiResult<List<SwaggerCodeGenItem>> Clients(bool fromCache = true)
        {
            var result = fromCache ? swagerCodeGen.ClientItemsCache : swagerCodeGen.ClientItems;

            return new ApiResult<List<SwaggerCodeGenItem>>(result);
        }
        #endregion

        #region 代码生成 - 服务端
        /// <summary>
        /// 代码生成 - 服务端
        /// </summary>
        /// <remarks>支持生成的服务端集合</remarks>
        [HttpGet("Servers")]
        [AllowAnonymous]
        [SwaggerOperation("CodeGen/Servers")]
        public ApiResult<List<SwaggerCodeGenItem>> Servers(bool fromCache = true)
        {
            var result = fromCache ? swagerCodeGen.ServerItemsCache : swagerCodeGen.ServerItems;

            return new ApiResult<List<SwaggerCodeGenItem>>(result);
        }
        #endregion

        #region 代码生成 - 发布SDK
        /// <summary>
        /// 代码生成 - 发布SDK
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <remarks>立即发布SDK到指定包管理平台</remarks>
        [HttpPost("ReleaseSDK")]
        [AllowAnonymous]
        [SwaggerOperation("CodeGen/ReleaseSDK")]
        public async Task<ApiResult<bool>> ReleaseSDK([FromBody]GenerateClientRequest value)
        {
            if (!ModelState.IsValid)
            {
                return new ApiResult<bool>()
                {
                    code = (int)BasicControllerEnums.UnprocessableEntity,
                    message = ModelErrors()
                };
            }

            var platformPath = $"./Node/sdkgen/{Enum.GetName(typeof(PackagePlatform), value.platform)}/";

            try
            {
                var swaggerDoc = string.Empty;

                using (var hc = new HttpClient())
                {
                    swaggerDoc = await hc.GetStringAsync(value.swaggerUrl);
                }

                if (string.IsNullOrWhiteSpace(swaggerDoc))
                {
                    return new ApiResult<bool>(l, CodeGenControllerEnums.GenerateClient_GetSwaggerFialed);
                }

                var templateDirectory = platformPath + Enum.GetName(typeof(Language), value.language) + "_" + DateTime.Now.Ticks.ToString();

                if (!Directory.Exists(templateDirectory))
                {
                    Directory.CreateDirectory(templateDirectory);
                }

                var SdkCode = await nodeServices.InvokeAsync<string>(platformPath + value.language,
                    swaggerDoc, value.apiId);

                switch (value.platform)
                {
                    case PackagePlatform.npm:

                        var PublishResult = ReleasePackage_NPM(templateDirectory, value.language, SdkCode, value.apiId);

                        break;

                    case PackagePlatform.nuget:

                        break;

                    case PackagePlatform.spm:

                        break;

                    case PackagePlatform.gradle:

                        break;

                    default:
                        break;
                }


                return new ApiResult<bool>(true);
            }

            catch (Exception ex)
            {
                return new ApiResult<bool>(l,
                    BasicControllerEnums.ExpectationFailed,
                    ex.Message);
            }
        }
        async Task<bool> ReleasePackage_NPM(string templateDirectory, Language lan, string SdkCode, string apiId)
        {
            #region 写SDK文件
            var fileName = string.Empty;

            switch (lan)
            {
                case Language.angular2:
                    fileName = templateDirectory + "/index.ts";
                    break;

                case Language.jQuery:
                    fileName = templateDirectory + "/index.js";
                    break;

                default:
                    break;
            }

            using (var sw = new StreamWriter(fileName, false, Encoding.UTF8))
            {
                sw.WriteLine(SdkCode);
            }
            #endregion

            #region 更新包信息
            var configFilePath = templateDirectory + "/package.json";

            var options = await GetNpmOptions(lan, apiId);

            var JsonDoc = new JObject();

            if (!string.IsNullOrWhiteSpace(options.name))
            {
                JsonDoc["name"] = options.name;
            }

            if (!string.IsNullOrWhiteSpace(options.homepage))
            {
                JsonDoc["homepage"] = options.homepage;
            }

            if (!string.IsNullOrWhiteSpace(options.author))
            {
                JsonDoc["author"] = options.author;
            }

            if (options.keywords != null && options.keywords.Length > 0)
            {
                JsonDoc["keywords"] = JToken.FromObject(options.keywords);
            }

            if (!string.IsNullOrWhiteSpace(options.description))
            {
                JsonDoc["description"] = options.description;
            }

            if (string.IsNullOrWhiteSpace(options.version)) { options.version = "0.0.0"; }

            if (!string.IsNullOrWhiteSpace(options.license))
            {
                JsonDoc["license"] = options.license;
            }

            #region version

            var CurrentVersion = Version.Parse(options.version);

            var newVersion = string.Empty;

            if (CurrentVersion.Build + 1 < int.MaxValue)
            {
                newVersion = $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build + 1}";
            }

            else if (CurrentVersion.Minor + 1 < int.MaxValue)
            {
                newVersion = $"{CurrentVersion.Major}.{CurrentVersion.Minor + 1}.{CurrentVersion.Build}";
            }

            else if (CurrentVersion.Major + 1 < int.MaxValue)
            {
                newVersion = $"{CurrentVersion.Major + 1}.{CurrentVersion.Minor}.{CurrentVersion.Build}";
            }

            JsonDoc["version"] = newVersion;
            #endregion

            using (var sw = new StreamWriter(configFilePath, false, Encoding.UTF8))
            {
                await sw.WriteLineAsync(JsonDoc.ToString());
            }
            #endregion

            var npmrcFilePath = templateDirectory + "/.npmrc";
            if (!string.IsNullOrWhiteSpace(options.token))
            {
                using (var sw = new StreamWriter(npmrcFilePath, false, Encoding.UTF8))
                {
                    await sw.WriteLineAsync("//registry.npmjs.org/:_authToken=" + options.token);
                }
            }

            var readmeFilePath = templateDirectory + "/README.md";
            if (!string.IsNullOrWhiteSpace(options.README))
            {
                using (var sw = new StreamWriter(readmeFilePath, false, Encoding.UTF8))
                {
                    await sw.WriteLineAsync(options.README);
                }
            }

            #region 打包SDK文件为.zip
            var releaseDirectory = templateDirectory + "_release/";

            if (!Directory.Exists(releaseDirectory))
            {
                Directory.CreateDirectory(releaseDirectory);
            }

            var templateName = Directory.GetParent(templateDirectory).Name;
            var languageName = Path.GetFileName(templateDirectory);
            var packageFileName = $"{templateName}.{languageName}_{newVersion}.zip";

            ZipFile.CreateFromDirectory(templateDirectory, releaseDirectory + packageFileName);
            #endregion

            #region 上传.zip，发消息到发包队列
            var blobUrl = string.Empty;

            using (var zipFileStream = new FileStream(releaseDirectory + packageFileName, FileMode.Open, FileAccess.Read))
            {
                blobUrl = await storageService.UploadBlobAsync(zipFileStream, "codegen-npm", packageFileName);
            }

            await storageService.AddMessageAsync("publish-package-npm", blobUrl);
            #endregion

            #region 清理本地文件
            try
            {
                Directory.Delete(releaseDirectory, true);
                Directory.Delete(templateDirectory, true);
            }
            catch { }
            #endregion

            options.version = newVersion;

            await SetNpmOptions(apiId, lan, options);

            return true;
        }
        #endregion

        #region 代码生成 - NPM设置
        /// <summary>
        ///  代码生成 - NPM设置
        /// </summary>
        /// <param name="id">微服务ID</param>
        /// <param name="language">语言</param>
        /// <returns></returns>
        [HttpGet("{id}/NpmOptions/{language}")]
        [AllowAnonymous]
        [SwaggerOperation("CodeGen/NpmOptions")]
        public async Task<ApiResult<CodeGenNpmOptionsModel>> NpmOptions(string id, Language language)
        {
            var result = await GetNpmOptions(language, id);

            var key = CodeGenControllerKeys.NpmOptions + id;

            var cacheResult = await redis.GetAsync(key);

            if (result != null)
            {
                return new ApiResult<CodeGenNpmOptionsModel>(result);
            }
            else
            {
                return new ApiResult<CodeGenNpmOptionsModel>(l, CodeGenControllerEnums.NpmOptions_GetOptionsFialed);
            }
        }
        async Task<CodeGenNpmOptionsModel> GetNpmOptions(Language lan, string id)
        {
            var key = CodeGenControllerKeys.NpmOptions + Enum.GetName(typeof(Language), lan) + ":" + id;

            var cacheResult = await redis.GetAsync(key);

            if (!string.IsNullOrWhiteSpace(cacheResult))
            {
                try
                {
                    var result = JsonConvert.DeserializeObject<CodeGenNpmOptionsModel>(cacheResult);

                    return result;
                }

                catch
                {
                    return null;
                }
            }

            return null;
        }
        #endregion

        #region 代码生成 - 更新NPM设置
        /// <summary>
        ///  代码生成 - 更新NPM设置
        /// </summary>
        /// <param name="id">微服务ID</param>
        /// <param name="language">语言</param>
        /// <param name="value">package.json的内容字符串</param>
        /// <returns></returns>
        [HttpPut("{id}/NpmOptions/{language}")]
        [AllowAnonymous]
        [SwaggerOperation("CodeGen/PutNpmOptions")]
        public async Task<ApiResult<bool>> NpmOptions(string id, Language language, [FromBody]CodeGenNpmOptionsModel value)
        {
            if (!ModelState.IsValid)
            {
                return new ApiResult<bool>(l, BasicControllerEnums.UnprocessableEntity,
                    ModelErrors());
            }

            var result = await SetNpmOptions(id, language, value);

            return new ApiResult<bool>(result);
        }
        async Task<bool> SetNpmOptions(string id, Language lan, CodeGenNpmOptionsModel value)
        {
            var key = CodeGenControllerKeys.NpmOptions + Enum.GetName(typeof(Language), lan) + ":" + id;

            var cacheResult = JsonConvert.SerializeObject(value);

            var result = await redis.SetAsync(key, cacheResult, null);

            return result;
        }
        #endregion
    }
}
