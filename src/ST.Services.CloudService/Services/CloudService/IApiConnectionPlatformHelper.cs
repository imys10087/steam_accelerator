using System.Net.Http;
using System.Threading.Tasks;

namespace System.Application.Services.CloudService
{
    /// <summary>
    /// 平台侧为 <see cref="IApiConnection"/> 提供的能力。
    ///
    /// <para><b>裁剪说明</b></para>
    /// <para>
    /// 原接口还包含账号体系相关能力：<c>Auth</c>、<c>SaveAuthTokenAsync</c>、
    /// <c>OnLoginedAsync</c>、<c>GetAuthenticationHeaderValue</c>、<c>RSA</c>、
    /// <c>RefreshToken</c>。加速功能所用的服务端接口
    /// （<c>api/Accelerate/All</c>、<c>api/Accelerate/Scripts</c>、<c>api/Script/*</c>、
    /// <c>api/Version/*</c>）全部是匿名接口，不需要 JWT 与 RSA 加密，
    /// 因此这些成员已随账号模块一并移除。
    /// </para>
    /// </summary>
    public interface IApiConnectionPlatformHelper
    {
        /// <summary>显示响应错误消息。</summary>
        void ShowResponseErrorMessage(string message);

        /// <summary>显示响应错误消息（默认实现会在「已取消」时静默）。</summary>
        void ShowResponseErrorMessage(IApiResponse response, string? errorAppendText = null)
        {
            if (response.Code == ApiResponseCode.Canceled) return;
            ShowResponseErrorMessage(response.GetMessageByAppendText(errorAppendText));
        }

        /// <summary>创建用于访问服务端接口的 <see cref="HttpClient"/>。</summary>
        HttpClient CreateClient();
    }
}
