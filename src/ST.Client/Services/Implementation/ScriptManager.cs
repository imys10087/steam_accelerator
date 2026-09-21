using Microsoft.Extensions.Logging;
using System.Application.Models;
using System.Application.Properties;
using System.Application.Repositories;
using System.Application.UI.Resx;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace System.Application.Services.Implementation
{
    /// <inheritdoc cref="IScriptManager"/>
    public sealed class ScriptManager : IScriptManager
    {
        /// <summary>基础脚本（内置脚本）在服务端的固定 Id。</summary>
        static readonly Guid BasicsScriptId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        const int DefaultOrder = 10;

        readonly ILogger logger;
        readonly IToast toast;
        readonly IHttpService httpService;
        readonly IScriptRepository scriptRepository;
        readonly ICloudServiceClient csc;

        public ScriptManager(
            IScriptRepository scriptRepository,
            ILoggerFactory loggerFactory,
            IToast toast,
            IHttpService httpService,
            ICloudServiceClient csc)
        {
            this.scriptRepository = scriptRepository;
            this.toast = toast;
            this.httpService = httpService;
            this.csc = csc;
            logger = loggerFactory.CreateLogger<ScriptManager>();
        }

        public async Task<IApiResponse<ScriptDTO?>> AddScriptAsync(
            string filePath,
            ScriptDTO? oldInfo = null,
            bool build = true,
            int? order = null,
            bool deleteFile = false,
            Guid? pid = null,
            bool ignoreCache = false)
        {
            var fileInfo = new FileInfo(filePath);

            if (!fileInfo.Exists)
            {
                var msg = AppResources.Script_NoFile.Format(filePath);
                logger.LogError(msg);
                return ApiResponse.Fail<ScriptDTO?>(msg);
            }

            ScriptDTO.TryParse(filePath, out var info);
            return await AddScriptAsync(fileInfo, info, oldInfo, build, order, deleteFile, pid, ignoreCache)
                .ConfigureAwait(false);
        }

        public async Task<IApiResponse<ScriptDTO?>> AddScriptAsync(
            FileInfo fileInfo,
            ScriptDTO? info,
            ScriptDTO? oldInfo = null,
            bool build = true,
            int? order = null,
            bool deleteFile = false,
            Guid? pid = null,
            bool ignoreCache = false)
        {
            if (info == null)
            {
                var msg = AppResources.Script_ReadFileError.Format(fileInfo.FullName);
                logger.LogError(msg);
                return ApiResponse.Fail<ScriptDTO?>(msg);
            }

            if (info.Content == null)
            {
                var msg = AppResources.Script_ReadFileError.Format(fileInfo.FullName);
                logger.LogError(msg);
                toast.Show(msg);
                return ApiResponse.Fail<ScriptDTO?>(msg);
            }

            try
            {
                var md5 = Hashs.String.MD5(info.Content);
                var sha512 = Hashs.String.SHA512(info.Content);

                if (!ignoreCache && await scriptRepository.ExistsScript(md5, sha512).ConfigureAwait(false))
                {
                    return ApiResponse.Fail<ScriptDTO?>(AppResources.Script_FileRepeat);
                }

                var fileName = md5 + FileEx.JS;
                var appDataPath = Path.Combine(IOPath.AppDataDirectory, IScriptManager.DirName, fileName);
                var saveInfo = new FileInfo(appDataPath);

                // 修复点：Windows 路径大小写不敏感，原来用区分大小写的字符串比较，
                // 同一文件不同大小写写法会被误判为「不同文件」而重复删除/复制。
                var isNoRepeat = !string.Equals(
                    saveInfo.FullName, fileInfo.FullName, StringComparison.OrdinalIgnoreCase);

                if (!saveInfo.Directory!.Exists)
                {
                    saveInfo.Directory.Create();
                }

                if (saveInfo.Exists)
                {
                    if (isNoRepeat) saveInfo.Delete();
                }
                else
                {
                    fileInfo.CopyTo(appDataPath);
                }

                if (oldInfo != null && oldInfo.LocalId > 0)
                {
                    info.LocalId = oldInfo.LocalId;
                    info.Id = oldInfo.Id;
                    info.Order = oldInfo.Order;

                    if (isNoRepeat)
                    {
                        var deleteState = await DeleteScriptAsync(oldInfo, false).ConfigureAwait(false);
                        if (!deleteState.IsSuccess)
                        {
                            return ApiResponse.Fail<ScriptDTO?>(
                                AppResources.Script_FileDeleteError.Format(oldInfo.FilePath));
                        }
                    }
                }

                if (pid.HasValue)
                {
                    info.Id = pid.Value;
                }

                var cachePath = Path.Combine(IOPath.CacheDirectory, IScriptManager.DirName, fileName);

                info.FilePath = appDataPath;
                info.IsBuild = build;
                info.CachePath = appDataPath;

                var cacheInfo = new FileInfo(cachePath);
                cacheInfo.Refresh();

                if (!await BuildScriptAsync(info, cacheInfo, build).ConfigureAwait(false))
                {
                    var buildErrorMsg = AppResources.Script_BuildError.Format(fileInfo.FullName);
                    logger.LogError(buildErrorMsg);
                    toast.Show(buildErrorMsg);
                    return ApiResponse.Fail<ScriptDTO?>(buildErrorMsg);
                }

                var db = info.ToEntity();
                db.MD5 = md5;
                db.SHA512 = sha512;

                if (db.Pid == BasicsScriptId)
                {
                    info.IsBasics = true;
                    order = 1;
                }

                if (order.HasValue)
                {
                    db.Order = order.Value;
                }
                else if (db.Order == 0)
                {
                    db.Order = DefaultOrder;
                }

                if (deleteFile)
                {
                    TryDelete(fileInfo, "删除源脚本文件失败");
                }

                var (rowCount, _) = await scriptRepository.InsertOrUpdateAsync(db).ConfigureAwait(false);
                info.LocalId = db.Id;

                if (rowCount > 0)
                {
                    return ApiResponse.Code<ScriptDTO?>(ApiResponseCode.OK, AppResources.Script_SaveDbSuccess, info);
                }

                return ApiResponse.Fail<ScriptDTO?>(AppResources.Script_SaveDBError);
            }
            catch (Exception e)
            {
                var msg = AppResources.Script_ReadFileError.Format(e.GetAllMessage());
                logger.LogError(e, msg);
                return ApiResponse.Code<ScriptDTO?>(ApiResponseCode.Fail, msg, default, e);
            }
        }

        /// <summary>
        /// 生成最终注入到页面的脚本内容（拼接依赖 JS + 包裹 jQuery 适配层）。
        /// </summary>
        /// <returns>
        /// 是否写入成功。
        /// <para>
        /// 修复点：原实现只在 <c>model.RequiredJsArray != null</c> 时返回 <see langword="true"/>，
        /// 并在方法末尾无条件 <c>return false</c>。当脚本没有任何 <c>@require</c> 依赖
        /// （<c>RequiredJs</c> 为 <see langword="null"/>）时会直接判定「构建失败」，
        /// 导致这类脚本永远无法被保存。现在无论有无依赖都会正确写出文件。
        /// </para>
        /// </returns>
        public async Task<bool> BuildScriptAsync(ScriptDTO model, FileInfo fileInfo, bool build = true)
        {
            try
            {
                var scriptContent = new StringBuilder();

                if (build)
                {
                    if (model.RequiredJsArray != null)
                    {
                        foreach (var item in model.RequiredJsArray)
                        {
                            try
                            {
                                var scriptInfo = await httpService.GetAsync<string>(item).ConfigureAwait(false);
                                scriptContent.AppendLine(scriptInfo);
                            }
                            catch (Exception e)
                            {
                                var errorMsg = AppResources.Script_BuildDownloadError.Format(model.Name, item);
                                logger.LogError(e, errorMsg);
                                toast.Show(errorMsg);
                            }
                        }
                    }

                    scriptContent.AppendLine("(function () {");
                    scriptContent.AppendLine("var jq = jQuery.noConflict();(($, jQuery) => {");
                    scriptContent.AppendLine(model.Content);
                    scriptContent.AppendLine("})(jq, jq)})()");
                }
                else
                {
                    scriptContent.Append(model.Content);
                }

                fileInfo.Refresh();

                if (fileInfo.Directory != null && !fileInfo.Directory.Exists)
                {
                    fileInfo.Directory.Create();
                }

                if (fileInfo.Exists)
                {
                    fileInfo.Delete();
                }

                model.Content = scriptContent.ToString();

                using (var writer = fileInfo.CreateText())
                {
                    await writer.WriteAsync(scriptContent.ToString()).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                }

                return true;
            }
            catch (Exception e)
            {
                var msg = AppResources.Script_BuildError.Format(e.GetAllMessage());
                logger.LogError(e, msg);
                toast.Show(msg);
                return false;
            }
        }

        public async Task<IApiResponse> DeleteScriptAsync(ScriptDTO item, bool removeByDataBase = true)
        {
            // 删除操作应当幂等：文件不存在时返回成功，只有真正的删除失败才报错
            if (item.LocalId <= 0)
            {
                return OkScriptDeleteSuccess();
            }

            var info = await scriptRepository.FirstOrDefaultAsync(x => x.Id == item.LocalId).ConfigureAwait(false);
            if (info == null)
            {
                return OkScriptDeleteSuccess();
            }

            var fileName = info.MD5 + FileEx.JS;

            var cachePath = Path.Combine(IOPath.CacheDirectory, IScriptManager.DirName, fileName);
            var cacheDeleteResult = TryDelete(new FileInfo(cachePath), null);
            if (!cacheDeleteResult)
            {
                var msg = AppResources.Script_CacheDeleteError.Format(cachePath);
                logger.LogError("删除脚本缓存失败, path:{0}", cachePath);
                return ApiResponse.Fail(msg);
            }

            var appDataPath = Path.Combine(IOPath.AppDataDirectory, IScriptManager.DirName, fileName);
            if (!TryDelete(new FileInfo(appDataPath), null))
            {
                var msg = AppResources.Script_FileDeleteError.Format(appDataPath);
                logger.LogError("删除脚本文件失败, path:{0}", appDataPath);
                return ApiResponse.Fail(msg);
            }

            if (removeByDataBase)
            {
                await scriptRepository.DeleteAsync(item.LocalId).ConfigureAwait(false);
            }

            return OkScriptDeleteSuccess();

            static IApiResponse OkScriptDeleteSuccess() => ApiResponse.Ok(AppResources.Script_DeleteSuccess);
        }

        public async Task<ScriptDTO> TryReadFile(ScriptDTO item)
        {
            var cachePath = Path.Combine(IOPath.CacheDirectory, item.CachePath);

            if (File.Exists(cachePath))
            {
                item.Content = await File.ReadAllTextAsync(cachePath).ConfigureAwait(false);
                return item;
            }

            var infoPath = Path.Combine(IOPath.AppDataDirectory, item.FilePath);
            if (File.Exists(infoPath))
            {
                item.Content = await File.ReadAllTextAsync(infoPath).ConfigureAwait(false);

                var fileInfo = new FileInfo(cachePath);
                if (!await BuildScriptAsync(item, fileInfo, item.IsBuild).ConfigureAwait(false))
                {
                    toast.Show(AppResources.Script_ReadFileError.Format(item.Name));
                }

                return item;
            }

            // 文件已丢失：清理数据库残留
            var deleteResult = await DeleteScriptAsync(item).ConfigureAwait(false);
            toast.Show(deleteResult.IsSuccess
                ? AppResources.Script_NoFile.Format(item.Name)
                : AppResources.Script_NoFileDeleteError.Format(item.Name));

            return item;
        }

        public async Task<IEnumerable<ScriptDTO>> GetAllScriptAsync()
        {
            var scriptList = (await scriptRepository.GetAllAsync().ConfigureAwait(false)).ToDTOList();

            // 历史上出现过基础脚本被重复插入，这里做一次去重清理
            if (scriptList.Count(x => x.Id == BasicsScriptId) > 1)
            {
                await RemoveDuplicateBasicsAsync(scriptList).ConfigureAwait(false);
                scriptList = (await scriptRepository.GetAllAsync().ConfigureAwait(false)).ToDTOList();
            }

            try
            {
                foreach (var item in scriptList)
                {
                    await TryReadFile(item).ConfigureAwait(false);

                    if (item.Id != BasicsScriptId) continue;

                    item.IsBasics = true;
                    item.Order = 1;

                    if (!item.IsBuild) continue;

                    // 基础脚本在本地以「未构建」形式保存，首次加载时重建一次
                    item.IsBuild = false;
                    var fileInfo = new FileInfo(item.FilePath);

                    if (fileInfo.Exists)
                    {
                        var state = await AddScriptAsync(fileInfo, item, item, false, 1, ignoreCache: true)
                            .ConfigureAwait(false);

                        if (state.IsSuccess && state.Content?.Content != null)
                        {
                            item.Content = state.Content.Content;
                        }
                    }
                    else
                    {
                        await RedownloadBasicsAsync(item).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception e)
            {
                var errorMsg = AppResources.Script_ReadFileError.Format(e.GetAllMessage());
                logger.LogError(e, errorMsg);
                toast.Show(errorMsg);
            }

            return scriptList.Where(x => !string.IsNullOrWhiteSpace(x.Content));
        }

        async Task RemoveDuplicateBasicsAsync(List<ScriptDTO> scriptList)
        {
            string? keptPath = null;

            foreach (var item in scriptList.Where(x => x.Id == BasicsScriptId))
            {
                var path = new FileInfo(Path.Combine(IOPath.AppDataDirectory, item.FilePath));

                if (!path.Exists)
                {
                    await DeleteScriptAsync(item).ConfigureAwait(false);
                    continue;
                }

                if (keptPath == null)
                {
                    keptPath = item.FilePath;
                    continue;
                }

                await scriptRepository.DeleteAsync(item.LocalId).ConfigureAwait(false);
            }
        }

        async Task RedownloadBasicsAsync(ScriptDTO item)
        {
            var basicsInfo = await csc.Script
                .Basics(AppResources.Script_NoFile.Format(item.FilePath))
                .ConfigureAwait(false);

            if (basicsInfo.Code != ApiResponseCode.OK || basicsInfo.Content == null) return;

            var jsPath = await DownloadScriptAsync(basicsInfo.Content.UpdateLink).ConfigureAwait(false);
            if (!jsPath.IsSuccess) return;

            var build = await AddScriptAsync(
                jsPath.Content!, item,
                build: false,
                order: 1,
                deleteFile: true,
                pid: basicsInfo.Content.Id,
                ignoreCache: true).ConfigureAwait(false);

            if (build.IsSuccess && build.Content?.Content != null)
            {
                item.Content = build.Content.Content;
            }
        }

        public async Task<IApiResponse<string>> DownloadScriptAsync(string url)
        {
            var scriptStr = await httpService.GetAsync<string>(url, MediaTypeNames.JS).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(scriptStr))
            {
                logger.LogError("DownloadScript IsNullOrWhiteSpace, url:{0}", url);
                return ApiResponse.Code(ApiResponseCode.NoResponseContentValue, null, string.Empty);
            }

            string? cachePath = null;

            try
            {
                var md5 = Hashs.String.MD5(scriptStr);
                cachePath = Path.Combine(IOPath.CacheDirectory, IScriptManager.DirName, md5 + FileEx.DownloadCache);

                var fileInfo = new FileInfo(cachePath);
                if (!fileInfo.Directory!.Exists)
                {
                    fileInfo.Directory.Create();
                }
                else if (fileInfo.Exists)
                {
                    fileInfo.Delete();
                }

                using (var writer = fileInfo.CreateText())
                {
                    await writer.WriteAsync(scriptStr).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                }

                return ApiResponse.Ok(cachePath);
            }
            catch (Exception e)
            {
                logger.LogError(e, "DownloadScript FileWrite catch, url:{0}, cachePath:{1}", url, cachePath);
                return ApiResponse.Exception<string>(e);
            }
        }

        /// <summary>尽力删除文件；返回是否已不存在（幂等语义）。</summary>
        bool TryDelete(FileInfo fileInfo, string? logMessage)
        {
            try
            {
                if (fileInfo.Exists)
                {
                    fileInfo.Delete();
                }

                return true;
            }
            catch (Exception e)
            {
                if (logMessage != null)
                {
                    logger.LogError(e, "{0}, path:{1}", logMessage, fileInfo.FullName);
                }

                return false;
            }
        }
    }
}
