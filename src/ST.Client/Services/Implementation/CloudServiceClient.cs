using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Application.Models;
using System.Application.Services.CloudService;
using System.Net.Http;

namespace System.Application.Services.Implementation
{
    /// <summary>
    /// 桌面端服务端接口客户端。
    ///
    /// <para><b>裁剪说明</b></para>
    /// <para>
    /// 原实现注入 <c>IUserManager</c> 并重写 <c>SaveAuthTokenAsync</c> /
    /// <c>OnLoginedAsync</c> 来把 JWT 与用户资料写回本地用户存储。
    /// 账号模块移除后本类不再持有任何用户状态，仅是一个纯粹的匿名接口客户端。
    /// </para>
    /// </summary>
    public class CloudServiceClient : CloudServiceClientBase
    {
        public CloudServiceClient(
            ILoggerFactory loggerFactory,
            IHttpClientFactory clientFactory,
            IHttpPlatformHelperService httpPlatformHelper,
            IToast toast,
            IOptions<AppSettings> options,
            IModelValidator validator)
            : base(
                loggerFactory.CreateLogger(ClientName_),
                clientFactory,
                httpPlatformHelper,
                toast,
                options.Value,
                validator)
        {
        }
    }
}
