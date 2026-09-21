using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Application.Models;
using System.Application.Services.CloudService.Clients;
using System.Application.Services.CloudService.Clients.Abstractions;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace System.Application.Services.CloudService
{
    /// <summary>
    /// 服务端接口客户端基类。
    ///
    /// <para><b>裁剪说明</b></para>
    /// <para>
    /// 原基类暴露了 8 个子客户端：<c>Account</c>、<c>Manage</c>、<c>AuthMessage</c>、
    /// <c>Version</c>、<c>ActiveUser</c>、<c>Accelerate</c>、<c>Script</c>、<c>DonateRanking</c>。
    /// 其中只有 <c>Accelerate</c>（加速项目列表）与 <c>Script</c>（脚本商店/内置脚本）
    /// 属于本工具的功能范围，<c>Version</c> 保留用于「服务端下发版本策略」。
    /// 其余（账号、管理、短信、活跃上报、捐赠排行）随对应功能模块移除。
    /// </para>
    /// <para>
    /// 同时移除了 <c>SaveAuthTokenAsync</c>、<c>OnLoginedAsync</c>、
    /// <c>RefreshToken</c>、<c>RSA</c> 等账号鉴权入口。
    /// </para>
    /// </summary>
    public abstract class CloudServiceClientBase : GeneralHttpClientFactory, ICloudServiceClient, IApiConnectionPlatformHelper
    {
        protected internal const string ClientName_ = "CloudServiceClient";

        #region 子客户端

        /// <summary>加速项目与脚本列表。</summary>
        public IAccelerateClient Accelerate { get; }

        /// <summary>脚本商店与内置脚本。</summary>
        public IScriptClient Script { get; }

        /// <summary>服务端版本策略（用于提示客户端已过期）。</summary>
        public IVersionClient Version { get; }

        #endregion

        readonly IApiConnection connection;
        protected readonly ICloudServiceSettings settings;
        protected readonly IToast toast;

        public string ApiBaseUrl { get; }

        public ICloudServiceSettings Settings => settings;

        protected sealed override string? DefaultClientName => ClientName_;

        protected CloudServiceClientBase(
            ILogger logger,
            IHttpClientFactory clientFactory,
            IHttpPlatformHelperService http_helper,
            IToast toast,
            ICloudServiceSettings settings,
            IModelValidator validator)
            : base(logger, http_helper, clientFactory)
        {
            this.toast = toast;
            this.settings = settings;

            ApiBaseUrl = string.IsNullOrWhiteSpace(settings.ApiBaseUrl)
                ? throw new ArgumentNullException(nameof(settings))
                : settings.ApiBaseUrl;

            connection = new ApiConnection(logger, this, http_helper, validator);

            Accelerate = new AccelerateClient(connection);
            Script = new ScriptClient(connection);
            Version = new VersionClient(connection);
        }

        /// <inheritdoc cref="IHttpPlatformHelperService.UserAgent"/>
        internal string UserAgent => http_helper.UserAgent;

        public virtual void ShowResponseErrorMessage(string message) => toast.Show(message);

        HttpClient IApiConnectionPlatformHelper.CreateClient() => CreateClient();

        Task<IApiResponse> ICloudServiceClient.Download(
            bool isAnonymous, string requestUri, string cacheFilePath,
            IProgress<float>? progress, CancellationToken cancellationToken)
            => connection.DownloadAsync(cancellationToken, requestUri, cacheFilePath, progress, isAnonymous);

        // 注：原实现还有一个 ICloudServiceClient.Forward 显式实现（对应已被标记
        // [Obsolete("Http Error 403", true)] 的服务端转发接口）。该成员已随接口一并移除。

        async Task<string> ICloudServiceClient.Info()
        {
            var api = await connection.GetHtml(default, "/info").ConfigureAwait(false);
            var str = api.Content;

            if (string.IsNullOrWhiteSpace(str)) return string.Empty;

            try
            {
                var jsonObj = JObject.Parse(str);
                return Serializable.SJSON(Serializable.JsonImplType.NewtonsoftJson, jsonObj, writeIndented: true);
            }
            catch
            {
                return str;
            }
        }
    }
}
