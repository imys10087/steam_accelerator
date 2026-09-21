using System.Application.Models;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Titanium.Web.Proxy.Network;

namespace System.Application.Services
{
    /// <summary>
    /// 本地反向代理服务（加速核心）。
    ///
    /// <para>
    /// 与原始版本相比，本接口<b>只保留加速所需的成员</b>：
    /// 移除了 GOG 平台 PEM 注入（<c>WirtePemCertificateToGoGSteamPlugins</c>）、
    /// 以及语义反了且从未被使用的 <c>IsCertificate</c>
    /// （原实现为 <c>CertificateManager == null || RootCertificate == null</c>，
    /// 名字读作「证书存在」而返回的是「证书缺失」，是典型的误导性 API）。
    /// </para>
    ///
    /// <para>
    /// 需要注意的是，本接口设置了<b>大量可变属性</b>，这是历史遗留的装配方式：
    /// <c>ProxyService</c> 在启动前逐项赋值，然后调用 <see cref="StartProxy"/>。
    /// 这些属性会在启动时被固化为一个不可变快照
    /// （<see cref="Accelerator.ProxyRuntimeSettings"/>）供请求热路径使用，
    /// 因此运行期修改这些属性不会影响已经启动的代理，需要重启代理才生效。
    /// </para>
    /// </summary>
    public interface IHttpProxyService : IDisposable
    {
        /// <summary>证书名称，硬编码不可改动，确保与历史版本兼容。</summary>
        const string CertificateName = "SteamTools";

        const string RootCertificateName = $"{CertificateName} Certificate";

        const string RootCertificateIssuerName = $"{CertificateName} Certificate Authority";

        /// <summary>用于承接注入脚本与跨域转发的本地域名。</summary>
        const string LocalDomain = "local.steampp.net";

        const string TAG = "HttpProxyS";

        static IHttpProxyService Instance => DI.Get<IHttpProxyService>();

        // ------------------------------------------------------------------
        // 加速输入
        // ------------------------------------------------------------------

        /// <summary>当前启用的加速项目。</summary>
        IReadOnlyCollection<AccelerateProjectDTO>? ProxyDomains { get; set; }

        /// <summary>当前启用的注入脚本。</summary>
        IReadOnlyCollection<ScriptDTO>? Scripts { get; set; }

        /// <summary>是否启用脚本注入。</summary>
        bool IsEnableScript { get; set; }

        /// <summary>是否仅对 Steam 内置浏览器注入脚本。</summary>
        bool IsOnlyWorkSteamBrowser { get; set; }

        /// <summary>开启加速后仅注入脚本而不做加速改写。</summary>
        bool OnlyEnableProxyScript { get; set; }

        // ------------------------------------------------------------------
        // 监听参数
        // ------------------------------------------------------------------

        /// <summary>证书引擎。</summary>
        CertificateEngine CertificateEngine { get; set; }

        /// <summary>显式代理监听端口。</summary>
        int ProxyPort { get; set; }

        /// <summary>监听地址。</summary>
        IPAddress ProxyIp { get; set; }

        /// <summary>是否以系统代理模式运行（否则走透明代理接管 80/443）。</summary>
        bool IsSystemProxy { get; set; }

        // ------------------------------------------------------------------
        // 可选通道
        // ------------------------------------------------------------------

        /// <summary>是否启用 SOCKS5 监听。</summary>
        bool Socks5ProxyEnable { get; set; }

        /// <summary>SOCKS5 监听端口。</summary>
        int Socks5ProxyPortId { get; set; }

        /// <summary>是否启用二级（上游）代理。</summary>
        bool TwoLevelAgentEnable { get; set; }

        /// <summary>二级代理协议类型。</summary>
        ExternalProxyType TwoLevelAgentProxyType { get; set; }

        const ExternalProxyType DefaultTwoLevelAgentProxyType = ExternalProxyType.Socks5;

        string? TwoLevelAgentIp { get; set; }

        int TwoLevelAgentPortId { get; set; }

        string? TwoLevelAgentUserName { get; set; }

        string? TwoLevelAgentPassword { get; set; }

        /// <summary>自定义上游 DNS；<see langword="null"/> 表示使用系统默认。</summary>
        IPAddress? ProxyDNS { get; set; }

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------

        /// <summary>代理是否正在运行。</summary>
        bool ProxyRunning { get; }

        /// <summary>安装并信任本地根证书。返回是否成功。</summary>
        bool SetupCertificate();

        /// <summary>移除本地根证书。代理运行中时拒绝执行并返回 <see langword="false"/>。</summary>
        bool DeleteCertificate();

        /// <summary>把根证书加入 macOS 系统信任链（需要管理员权限）。</summary>
        void TrustCer();

        /// <summary>判断给定证书是否已被信任。</summary>
        bool IsCertificateInstalled(System.Security.Cryptography.X509Certificates.X509Certificate2? certificate2);

        /// <summary>启动代理。返回是否启动成功。</summary>
        Task<bool> StartProxy();

        /// <summary>停止代理并还原系统代理设置。</summary>
        void StopProxy();

        // ------------------------------------------------------------------
        // 端口与路径
        // ------------------------------------------------------------------

        /// <summary>指定端口是否已被占用。</summary>
        bool PortInUse(int port);

        /// <summary>取得一个随机可用端口。</summary>
        int GetRandomUnusedPort();

        const string PfxFileName = $"{CertificateName}.Certificate{FileEx.PFX}";

        const string CerFileName = $"{CertificateName}.Certificate{FileEx.CER}";

        /// <summary>根证书私钥文件完整路径。</summary>
        string PfxFilePath => Path.Combine(IOPath.AppDataDirectory, PfxFileName);

        /// <summary>根证书公钥文件完整路径。</summary>
        string CerFilePath => Path.Combine(IOPath.AppDataDirectory, CerFileName);

        /// <summary>导出给用户时的证书文件名（带时间戳）。</summary>
        static string CerExportFileName
        {
            get
            {
                const string format = $"{ThisAssembly.AssemblyTrademark}  Certificate {{0}}{FileEx.CER}";
                return string.Format(format, DateTime.Now.ToString(DateTimeFormat.File));
            }
        }
    }
}
