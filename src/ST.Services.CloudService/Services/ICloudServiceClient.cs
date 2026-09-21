using System.Application.Services.CloudService.Clients.Abstractions;
using System.Threading;
using System.Threading.Tasks;

namespace System.Application.Services
{
    /// <summary>
    /// 服务端接口客户端。
    ///
    /// <para><b>裁剪说明</b></para>
    /// <para>
    /// 原接口暴露 8 个子客户端（<c>Account</c>/<c>Manage</c>/<c>AuthMessage</c>/<c>Version</c>/
    /// <c>ActiveUser</c>/<c>Accelerate</c>/<c>Script</c>/<c>DonateRanking</c>）以及一个
    /// 已标记 <c>[Obsolete("Http Error 403", true)]</c> 的 <c>Forward</c>。
    /// 现在只保留加速功能实际使用的三个：<see cref="Accelerate"/>、<see cref="Script"/>、
    /// <see cref="Version"/>，并保留 <see cref="ApiBaseUrl"/>、<see cref="Download"/>、<see cref="Info"/>。
    /// </para>
    /// </summary>
    public interface ICloudServiceClient
    {
        /// <summary>服务端接口根地址。</summary>
        string ApiBaseUrl { get; }

        /// <summary>加速项目与脚本列表。</summary>
        IAccelerateClient Accelerate { get; }

        /// <summary>脚本商店与内置脚本。</summary>
        IScriptClient Script { get; }

        /// <summary>服务端版本策略。</summary>
        IVersionClient Version { get; }

        /// <summary>下载远端内容到缓存文件（脚本更新使用）。</summary>
        Task<IApiResponse> Download(
            bool isAnonymous,
            string requestUri,
            string cacheFilePath,
            IProgress<float>? progress,
            CancellationToken cancellationToken = default);

        /// <summary>获取服务端 <c>/info</c> 信息（诊断用）。</summary>
        Task<string> Info();

        static ICloudServiceClient Instance => DI.Get<ICloudServiceClient>();
    }
}
