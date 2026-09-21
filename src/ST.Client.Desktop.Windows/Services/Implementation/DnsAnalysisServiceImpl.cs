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
        /// 共享查询客户端。DnsClient 默认启用结果缓存，这里保留其默认配置，
        /// 上层还会再套一层 <c>ProxyDnsCache</c> 做显式 TTL 与并发合并。
        /// </summary>
        static readonly LookupClient lookupClient = new();

        public async Task<IPAddress[]?> AnalysisDomainIpByCustomDns(
            string url,
            IPAddress[]? dnsServers = null,
            bool isIPv6 = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            var options = dnsServers != null && dnsServers.Length > 0
                ? new DnsQueryAndServerOptions(dnsServers)
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

                var response = await client
                    .QueryServerAsync(
                        new[] { IPAddress.Parse(PrimaryDNS_IPV6_Ali) },
                        IPV6_TESTDOMAIN,
                        QueryType.AAAA,
                        QueryClass.IN,
                        cancellationToken)
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
