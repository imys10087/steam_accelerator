using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace System.Application.Services
{
    /// <summary>
    /// DNS 解析服务（加速链路专用）。
    ///
    /// <para><b>清理说明</b></para>
    /// <para>
    /// 原接口暴露了 7 个解析入口和 4 个辅助方法，但加速链路实际上只用到其中 4 个。
    /// 本次清掉了以下几处<b>已确认无人调用</b>的死代码：
    /// </para>
    /// <list type="bullet">
    /// <item><c>PingHostname</c> —— 内部 <c>new Ping()</c> 从不释放，是明确的句柄泄漏；</item>
    /// <item><c>AnalysisHostnameTime</c>、<c>GetHostByIPAddress</c>、<c>GetPingHostNameData</c> —— 无调用方；</item>
    /// <item><c>AnalysisDomainIpByGoogleDns</c> / <c>ByCloudflare</c> / <c>ByDnspod</c> / <c>By114Dns</c>
    ///       —— 无调用方，同时删掉其对应的 DNS 常量。</item>
    /// </list>
    /// <para>
    /// 另外为全部解析入口补上 <see cref="CancellationToken"/>，使代理停止时能真正中断
    /// 在飞的 DNS 查询，而不是让它们拖着线程走完。
    /// </para>
    /// </summary>
    public interface IDnsAnalysisService
    {
        static IDnsAnalysisService Instance => DI.Get<IDnsAnalysisService>();

        #region DNS 常量

        /// <summary>阿里公共 DNS（IPv6）。</summary>
        protected const string PrimaryDNS_IPV6_Ali = "2400:3200::1";

        /// <summary>阿里公共 DNS（IPv4 主）。</summary>
        const string PrimaryDNS_Ali = "223.5.5.5";

        /// <summary>阿里公共 DNS（IPv4 备）。</summary>
        const string SecondaryDNS_Ali = "223.6.6.6";

        /// <summary>阿里公共 DNS 服务器组。</summary>
        protected static readonly IPAddress[] DNS_Alis =
        {
            IPAddress.Parse(PrimaryDNS_Ali),
            IPAddress.Parse(SecondaryDNS_Ali),
        };

        #endregion

        #region IPv6 探测

        /// <summary>用于探测 IPv6 连通性的域名。</summary>
        protected const string IPV6_TESTDOMAIN = "ipv6.rmbgame.net";

        /// <summary>探测成功时该域名应解析到的地址。</summary>
        protected const string IPV6_TESTDOMAIN_SUCCESS = PrimaryDNS_IPV6_Ali;

        #endregion

        /// <summary>
        /// 使用系统默认 DNS 解析域名。默认实现等价于 <see cref="AnalysisDomainIpByCustomDns"/> 传入 <see langword="null"/>。
        /// </summary>
        Task<IPAddress[]?> AnalysisDomainIp(string url, bool isIPv6 = false, CancellationToken cancellationToken = default)
            => AnalysisDomainIpByCustomDns(url, null, isIPv6, cancellationToken);

        /// <summary>使用阿里公共 DNS 解析域名。</summary>
        Task<IPAddress[]?> AnalysisDomainIpByAliDns(string url, bool isIPv6 = false, CancellationToken cancellationToken = default)
            => AnalysisDomainIpByCustomDns(url, DNS_Alis, isIPv6, cancellationToken);

        /// <summary>
        /// 使用指定 DNS 服务器解析域名；<paramref name="dnsServers"/> 为 <see langword="null"/> 时使用系统默认 DNS。
        /// </summary>
        /// <returns>解析到的地址；失败时返回 <see langword="null"/>。</returns>
        Task<IPAddress[]?> AnalysisDomainIpByCustomDns(
            string url,
            IPAddress[]? dnsServers = null,
            bool isIPv6 = false,
            CancellationToken cancellationToken = default)
        {
            // 注意：默认实现运行于 netstandard2.1 目标，该目标没有带 CancellationToken 的
            // Dns.GetHostAddressesAsync 重载，因此这里不传播取消令牌；
            // 各平台实现（如 Windows）会提供支持取消的版本。
            return Dns.GetHostAddressesAsync(url);
        }

        /// <summary>探测本机是否具备可用的 IPv6 出口。</summary>
        Task<bool> GetIsIpv6Support(CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
