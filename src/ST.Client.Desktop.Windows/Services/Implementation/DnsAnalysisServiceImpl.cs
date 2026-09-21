using DnsClient;
using DnsClient.Protocol;
using System.Application.Services;
using System.Application.Services.Implementation;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using static System.Application.Services.IDnsAnalysisService;

namespace System.Application.Services.Implementation
{
    /// <summary>
    /// Windows 平台 DNS 解析实现（基于 DnsClient.NET）。
    ///
    /// <para><b>清理说明</b></para>
    /// <list type="bullet">
    /// <item>移除 <c>AnalysisHostnameTime</c> / <c>GetPingHostNameData</c> / <c>GetHostByIPAddress</c>
    ///       三个无调用方的方法，减少维护面；</item>
    /// <item>全部查询改为支持 <see cref="CancellationToken"/>，代理停止时可中断在飞的解析；</item>
    /// <item>复用共享的 <see cref="LookupClient"/> 实例（自带结果缓存），避免每次查询重新建连接池。</item>
    /// </list>
    /// </summary>
    internal sealed class DnsAnalysisServiceImpl : IDnsAnalysisService
    {
        /// <summary>
        /// 共享查询客户端。DnsClient 默认启用结果缓存，这里保留其缓存能力，
        /// 上层还会再套一层 <c>ProxyDnsCache</c> 做显式 TTL 与并发合并。
        ///
        /// <para><b>为什么显式收紧超时</b></para>
        /// <para>
        /// DnsClient 的默认参数是 <c>Timeout=5s</c> + <c>Retries=2</c>，即单次查询最坏要等 15 秒。
        /// 而本解析会被用在**请求路径**上（未命中加速项目的域名复核、加速项目上游地址解析），
        /// 一旦某个 DNS 服务器不可达，每个新域名都会把请求拖住十几秒，表现为「网页能开但极卡」。
        /// 这里收紧为 2 秒 × 1 次重试，最坏约 4 秒，把抖动的上限压下来。
        /// </para>
        /// </summary>
        static readonly LookupClient lookupClient = new(new LookupClientOptions
        {
            Timeout = TimeSpan.FromSeconds(2),
            Retries = 1,
            UseCache = true,
        });

        public async Task<IPAddress[]?> AnalysisDomainIpByCustomDns(
            string url,
            IPAddress[]? dnsServers = null,
            bool isIPv6 = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            var options = dnsServers != null && dnsServers.Length > 0
                ? new DnsQueryAndServerOptions(dnsServers)
                {
                    // 与上面的 lookupClient 保持一致：自定义 DNS 也要收紧超时，
                    // 否则一旦用户选中的 DNS 不可达，同样会把请求路径拖慢十几秒
                    Timeout = TimeSpan.FromSeconds(2),
                    Retries = 1,
                    UseCache = true,
                }
                : null;

            if (isIPv6)
            {
                var aaaa = await QueryAsync(url, QueryType.AAAA, options, cancellationToken).ConfigureAwait(false);
                if (aaaa is { Length: > 0 }) return aaaa;
            }

            // 无 IPv6 结果（或未请求 IPv6）时回落到 A 记录
            return await QueryAsync(url, QueryType.A, options, cancellationToken).ConfigureAwait(false);
        }

        static async Task<IPAddress[]?> QueryAsync(
            string url,
            QueryType queryType,
            DnsQueryAndServerOptions? options,
            CancellationToken cancellationToken)
        {
            try
            {
                var question = new DnsQuestion(url, queryType);

                var response = options != null
                    ? await lookupClient.QueryAsync(question, options, cancellationToken).ConfigureAwait(false)
                    : await lookupClient.QueryAsync(question, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (response.HasError) return null;

                var answers = queryType == QueryType.AAAA
                    ? response.Answers.AaaaRecords().Select(s => s.Address).ToArray()
                    : response.Answers.ARecords().Select(s => s.Address).ToArray();

                return answers.Length > 0 ? answers : null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception e)
            {
                // 解析失败（网络不可达、超时、DNS 返回异常）不应把异常抛进代理请求路径
                Log.Error(nameof(DnsAnalysisServiceImpl), e, $"DNS 查询失败：{url} ({queryType})");
                return null;
            }
        }

        public async Task<bool> GetIsIpv6Support(CancellationToken cancellationToken = default)
        {
            try
            {
                var options = new LookupClientOptions
                {
                    Retries = 1,
                    Timeout = TimeSpan.FromSeconds(1),
                    UseCache = false,
                };

                // 注意：LookupClient 不实现 IDisposable，原写法 `using var` 会编译失败
                var client = new LookupClient(options);

                // 硬性上限：本机没有 IPv6 出口时，向 IPv6 DNS 地址发包可能在 socket
                // 层面长时间不返回（实测该探测阻塞了 11.7 秒，直接拖慢代理启动）。
                // 这里再加一层 2 秒硬超时，超时即判定「不支持 IPv6」——
                // 该标志只影响「是否额外查询 AAAA 记录」，误判为 false 是安全的。
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

                var response = await client
                    .QueryServerAsync(
                        new[] { IPAddress.Parse(PrimaryDNS_IPV6_Ali) },
                        IPV6_TESTDOMAIN,
                        QueryType.AAAA,
                        QueryClass.IN,
                        timeoutCts.Token)
                    .ConfigureAwait(false);

                var expected = IPAddress.Parse(IPV6_TESTDOMAIN_SUCCESS);
                return response.Answers.AaaaRecords().Any(s => s.Address.Equals(expected));
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception e)
            {
                // IPv6 探测失败只影响「是否优先解析 AAAA」，不影响加速主流程
                Log.Info(nameof(DnsAnalysisServiceImpl), "IPv6 探测失败：" + e.Message);
                return false;
            }
        }
    }
}

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDnsAnalysisService(this IServiceCollection services)
        {
            services.AddSingleton<IDnsAnalysisService, DnsAnalysisServiceImpl>();
            return services;
        }
    }
}
