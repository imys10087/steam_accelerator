using System.Application.Models;
using System.Application.Services.Accelerator;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;
using Titanium.Web.Proxy.Network;

namespace System.Application.Services.Implementation
{
    /// <summary>
    /// 本地反向代理服务实现（加速核心的编排层）。
    ///
    /// <para><b>重构说明</b></para>
    /// <para>
    /// 原始实现是一个 800+ 行的类，把「证书管理」「请求改写」「脚本注入」「DNS 解析」
    /// 「端点构建」「生命周期」全部混在一起。重构后本类只负责<b>编排</b>，
    /// 具体职责下沉到 <c>System.Application.Services.Accelerator</c> 命名空间下的专用类型：
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="ProxyHostMatcher"/>：域名匹配（取代 O(项目 × 域名) 线性扫描）；</item>
    /// <item><see cref="ProxyDnsCache"/>：带 TTL 与单飞合并的 DNS 缓存；</item>
    /// <item><see cref="ProxyCertificateManager"/>：根证书创建/信任/删除；</item>
    /// <item><see cref="ProxyRequestInterceptor"/>：请求改写与上游路由；</item>
    /// <item><see cref="ProxyScriptInjector"/>：脚本注入；</item>
    /// <item><see cref="ProxyRuntimeSettings"/>：不可变运行期设置快照。</item>
    /// </list>
    ///
    /// <para><b>已修复的缺陷（摘要）</b></para>
    /// <list type="bullet">
    /// <item>反复「启动/停止」代理会累积未注销的端点事件处理器 → 现在停止时显式解绑并释放端点引用；</item>
    /// <item><c>IsIpv6Support</c> 曾是 <see langword="static"/> 可变字段，存在启动线程与请求线程之间的数据竞争 → 现在通过不可变快照发布；</item>
    /// <item>每个请求都做一次 DNS 查询 → 现在走 TTL 缓存 + 并发合并；</item>
    /// <item>非 DEBUG 构建下证书异常被 <c>catch { }</c> 静默吞掉 → 现在统一记录日志。</item>
    /// </list>
    /// </summary>
    sealed class HttpProxyServiceImpl : IHttpProxyService
    {
        readonly IPlatformService platformService;
        readonly IDnsAnalysisService dnsAnalysis;

        readonly ProxyServer proxyServer = new();
        readonly ProxyCertificateManager certificateManager;

        readonly ProxyDnsCache dnsCache;
        readonly ProxyRequestInterceptor requestInterceptor;
        readonly ProxyScriptInjector scriptInjector;

        /// <summary>当前运行期设置快照。请求线程只读取一次；启动代理时整体替换。</summary>
        volatile ProxyRuntimeSettings runtimeSettings = ProxyRuntimeSettings.Disabled;

        /// <summary>当前已挂载的端点，用于停止时解绑事件并释放引用。</summary>
        readonly List<ProxyEndPoint> activeEndPoints = new();

        ExplicitProxyEndPoint? explicitProxyEndPoint;

        bool disposed;

        public HttpProxyServiceImpl(IPlatformService platformService, IDnsAnalysisService dnsAnalysis)
        {
            this.platformService = platformService;
            this.dnsAnalysis = dnsAnalysis;

            certificateManager = new ProxyCertificateManager(proxyServer, platformService);

            dnsCache = new ProxyDnsCache(ResolveCoreAsync);

            requestInterceptor = new ProxyRequestInterceptor(
                () => runtimeSettings,
                ResolveUpstreamAsync);

            scriptInjector = new ProxyScriptInjector(() => runtimeSettings);

            proxyServer.ExceptionFunc = exception => Log.Error(TAG, exception, "ProxyServer ExceptionFunc");

            // 固定这些与功能/内存/稳定性直接相关的开关，避免依赖库默认值随版本漂移
            proxyServer.EnableHttp2 = true;
            proxyServer.EnableConnectionPool = true;
            proxyServer.CheckCertificateRevocation = X509RevocationMode.NoCheck;

            var certManager = proxyServer.CertificateManager;
            certManager.CertificateEngine = CertificateEngine;
            certManager.PfxFilePath = ((IHttpProxyService)this).PfxFilePath;
            certManager.RootCertificateIssuerName = IHttpProxyService.RootCertificateIssuerName;
            certManager.RootCertificateName = IHttpProxyService.RootCertificateName;
            // macOS / iOS 对根证书有效期上限为 825 天，这里取更保守的 300 天
            certManager.CertificateValidDays = 300;

            certManager.RootCertificate = certManager.LoadRootCertificate();
        }

        #region 加速输入

        public IReadOnlyCollection<AccelerateProjectDTO>? ProxyDomains { get; set; }

        public IReadOnlyCollection<ScriptDTO>? Scripts { get; set; }

        public bool IsEnableScript { get; set; }

        public bool IsOnlyWorkSteamBrowser { get; set; }

        public bool OnlyEnableProxyScript { get; set; }

        #endregion

        #region 监听参数

        public CertificateEngine CertificateEngine { get; set; } = CertificateEngine.BouncyCastle;

        public int ProxyPort { get; set; } = 26501;

        public IPAddress ProxyIp { get; set; } = IPAddress.Any;

        public bool IsSystemProxy { get; set; }

        #endregion

        #region 可选通道

        public bool Socks5ProxyEnable { get; set; }

        public int Socks5ProxyPortId { get; set; }

        public bool TwoLevelAgentEnable { get; set; }

        public ExternalProxyType TwoLevelAgentProxyType { get; set; } = IHttpProxyService.DefaultTwoLevelAgentProxyType;

        public string? TwoLevelAgentIp { get; set; }

        public int TwoLevelAgentPortId { get; set; }

        public string? TwoLevelAgentUserName { get; set; }

        public string? TwoLevelAgentPassword { get; set; }

        public IPAddress? ProxyDNS { get; set; }

        #endregion

        public bool ProxyRunning => proxyServer.ProxyRunning;

        #region 证书

        public bool SetupCertificate() => certificateManager.SetupCertificate();

        public bool DeleteCertificate() => certificateManager.DeleteCertificate();

        public void TrustCer() => certificateManager.TrustCer();

        public bool IsCertificateInstalled(X509Certificate2? certificate2)
            => ProxyCertificateManager.IsCertificateInstalled(certificate2);

        #endregion

        #region 端口

        public int GetRandomUnusedPort() => SocketHelper.GetRandomUnusedPort(ProxyIp);

        public bool PortInUse(int port) => SocketHelper.IsUsePort(ProxyIp, port);

        #endregion

        #region 生命周期

        public async Task<bool> StartProxy()
        {
            if (proxyServer.ProxyRunning)
            {
                Log.Info(TAG, "StartProxy 被重复调用，忽略。");
                return true;
            }

            if (!certificateManager.EnsureTrustedRootCertificate())
            {
                // 拿不到受信任的根证书就无法解密 HTTPS，继续启动只会得到一个
                // 「看起来在跑但什么也加速不了」的代理，因此直接失败返回
                return false;
            }

            try
            {
                await BuildRuntimeSettingsAsync().ConfigureAwait(false);

                proxyServer.BeforeRequest += requestInterceptor.OnRequest;
                proxyServer.BeforeResponse += scriptInjector.OnResponse;
                proxyServer.ServerCertificateValidationCallback += OnCertificateValidation;

                if (!ConfigureEndPoints())
                {
                    CleanupHandlers();
                    return false;
                }

                ConfigureUpStreamProxy();

                proxyServer.Start();

                if (IsSystemProxy && !ApplySystemProxy())
                {
                    Log.Error(TAG, "系统代理开启失败");
                    StopProxy();
                    return false;
                }

                Log.Info(TAG, $"代理已启动，监听 {string.Join(", ", activeEndPoints.Select(x => $"{x.IpAddress}:{x.Port}"))}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(TAG, ex, nameof(StartProxy));
                StopProxy();
                return false;
            }
        }

        public void StopProxy()
        {
            try
            {
                if (proxyServer.ProxyRunning)
                {
                    proxyServer.Stop();
                }

                CleanupHandlers();

                if (IsSystemProxy)
                {
                    RestoreSystemProxy();
                }

                // 释放快照，避免已停用配置继续被请求路径持有
                runtimeSettings = ProxyRuntimeSettings.Disabled;

                Log.Info(TAG, "代理已停止。");
            }
            catch (Exception ex)
            {
                // 停止过程不应抛出：调用方通常在退出路径上执行它
                Log.Error(TAG, ex, nameof(StopProxy));
            }
        }

        /// <summary>解绑事件处理器并释放端点引用，修复反复启停导致的处理器累积。</summary>
        void CleanupHandlers()
        {
            proxyServer.BeforeRequest -= requestInterceptor.OnRequest;
            proxyServer.BeforeResponse -= scriptInjector.OnResponse;
            proxyServer.ServerCertificateValidationCallback -= OnCertificateValidation;

            if (explicitProxyEndPoint != null)
            {
                explicitProxyEndPoint.BeforeTunnelConnectRequest -= OnBeforeTunnelConnect;
                explicitProxyEndPoint = null;
            }

            foreach (var endPoint in activeEndPoints)
            {
                if (endPoint is TransparentProxyEndPoint transparent)
                {
                    transparent.BeforeSslAuthenticate -= OnBeforeSslAuthenticate;
                }
            }

            activeEndPoints.Clear();
            proxyServer.ProxyEndPoints.Clear();
        }

        bool ConfigureEndPoints()
        {
            if (IsSystemProxy)
            {
                if (PortInUse(ProxyPort))
                {
                    ProxyPort = GetRandomUnusedPort();
                }

                explicitProxyEndPoint = new ExplicitProxyEndPoint(ProxyIp, ProxyPort, true);
                explicitProxyEndPoint.BeforeTunnelConnectRequest += OnBeforeTunnelConnect;
                AddEndPoint(explicitProxyEndPoint);
            }
            else
            {
                // 透明代理：接管 443（必要时再加 80）
                int httpsPort;
                if (OperatingSystem2.IsLinux && !platformService.IsAdministrator)
                {
                    // 非 root 无法绑定 443，退回随机端口并引导用户做端口转发
                    httpsPort = GetRandomUnusedPort();
                    Browser2.Open(string.Format(UrlConstants.OfficialWebsite_UnixHostAccess_, httpsPort));
                }
                else
                {
                    httpsPort = 443;
                }

                var https = new TransparentProxyEndPoint(ProxyIp, httpsPort, true);
                https.BeforeSslAuthenticate += OnBeforeSslAuthenticate;
                AddEndPoint(https);

                if (!OperatingSystem2.IsLinux && !PortInUse(80))
                {
                    try
                    {
                        AddEndPoint(new TransparentProxyEndPoint(ProxyIp, 80, false));
                    }
                    catch (SocketException ex)
                    {
                        // 80 端口被占用不影响 HTTPS 加速，降级继续
                        Log.Info(TAG, "无法监听 80 端口，已跳过：" + ex.Message);
                    }
                }
            }

            if (Socks5ProxyEnable)
            {
                AddEndPoint(new SocksProxyEndPoint(ProxyIp, Socks5ProxyPortId, true));
            }

            return activeEndPoints.Count > 0;
        }

        void AddEndPoint(ProxyEndPoint endPoint)
        {
            proxyServer.AddEndPoint(endPoint);
            activeEndPoints.Add(endPoint);
        }

        void ConfigureUpStreamProxy()
        {
            if (!TwoLevelAgentEnable || string.IsNullOrWhiteSpace(TwoLevelAgentIp)) return;

            proxyServer.UpStreamHttpsProxy = new ExternalProxy(TwoLevelAgentIp, TwoLevelAgentPortId)
            {
                ProxyDnsRequests = true,
                BypassLocalhost = true,
                ProxyType = TwoLevelAgentProxyType,
                UserName = TwoLevelAgentUserName,
                Password = TwoLevelAgentPassword,
            };
            proxyServer.ForwardToUpstreamGateway = true;
        }

        bool ApplySystemProxy()
        {
            if (!DesktopBridge.IsRunningAsUwp && OperatingSystem2.IsWindows)
            {
                if (explicitProxyEndPoint == null) return false;
                proxyServer.SetAsSystemProxy(explicitProxyEndPoint, ProxyProtocolType.AllHttp);
                return true;
            }

            return explicitProxyEndPoint != null
                && IPlatformService.Instance.SetAsSystemProxy(true, explicitProxyEndPoint.IpAddress, explicitProxyEndPoint.Port);
        }

        void RestoreSystemProxy()
        {
            if (DesktopBridge.IsRunningAsUwp || !OperatingSystem2.IsWindows)
            {
                IPlatformService.Instance.SetAsSystemProxy(false);
            }
            else
            {
                proxyServer.DisableAllSystemProxies();
            }
        }

        #endregion

        #region 运行期设置快照

        /// <summary>
        /// 把当前可变配置固化为不可变快照。这是本类唯一的「配置 → 运行期」转换点。
        /// </summary>
        async Task BuildRuntimeSettingsAsync()
        {
            var matcher = ProxyHostMatcher.Build(ProxyDomains);
            var scripts = BuildScriptRules();
            var ipv6Support = await dnsAnalysis.GetIsIpv6Support().ConfigureAwait(false);

            runtimeSettings = new ProxyRuntimeSettings(
                matcher,
                scripts,
                ProxyDNS,
                ipv6Support,
                TwoLevelAgentEnable,
                OnlyEnableProxyScript,
                IsEnableScript,
                IsOnlyWorkSteamBrowser);

            Log.Info(TAG,
                $"运行期设置已构建：加速项目 {ProxyDomains?.Count ?? 0} 个，匹配规则 {matcher.RuleCount} 条，脚本 {scripts.Count} 个，IPv6={ipv6Support}");
        }

        /// <summary>
        /// 预处理脚本规则：预编译正则、区分精确/通配/排除规则，并一次性分配 <c>JsPathUrl</c>。
        /// </summary>
        List<ScriptRule> BuildScriptRules()
        {
            var result = new List<ScriptRule>();
            var scripts = Scripts;
            if (scripts == null || scripts.Count == 0) return result;

            foreach (var script in scripts)
            {
                if (script == null || !script.Enable) continue;

                // 修复点：原来在响应线程上「首次访问就赋值」，两个并发响应可能看到不同 URL，
                // 导致注入的 <script src> 指向一个查不到的路径而 404。
                script.JsPathUrl ??= "/" + Guid.NewGuid().ToString("N");

                var regex = default(Regex);
                var exact = new List<string>();
                var wildcard = new List<string>();

                foreach (var host in script.MatchDomainNamesArray)
                {
                    if (string.IsNullOrWhiteSpace(host)) continue;

                    if (host[0] == '/')
                    {
                        // 以 / 开头视为正则；只编译一次
                        try
                        {
                            regex = new Regex(host[1..], RegexOptions.Compiled);
                        }
                        catch (ArgumentException ex)
                        {
                            Log.Error(TAG, ex, $"脚本 {script.Name} 的匹配正则无效：{host}");
                        }
                    }
                    else if (host.Contains('*'))
                    {
                        wildcard.Add(host);
                    }
                    else
                    {
                        exact.Add(host);
                    }
                }

                var exclude = script.ExcludeDomainNamesArray ?? Array.Empty<string>();

                result.Add(new ScriptRule(script, exact.ToArray(), wildcard.ToArray(), regex, exclude));
            }

            return result;
        }

        #endregion

        #region DNS

        /// <summary>
        /// 解析加速项目的上游地址（域名 → 首选 IP）。
        /// </summary>
        Task<IPAddress?> ResolveUpstreamAsync(string host, IPAddress? dnsServer, bool isDomain)
        {
            if (!isDomain && IPAddress.TryParse(host, out var literal))
            {
                // 已经是 IP 字面量，无需解析
                return Task.FromResult<IPAddress?>(literal);
            }

            return ResolveViaCacheAsync(host, dnsServer, isDomain);
        }

        async Task<IPAddress?> ResolveViaCacheAsync(string host, IPAddress? dnsServer, bool isDomain)
        {
            var settings = runtimeSettings;
            var addresses = await dnsCache
                .GetOrAddAsync(host, settings.IsIpv6Support, dnsServer)
                .ConfigureAwait(false);

            return addresses?.FirstOrDefault();
        }

        /// <summary>真实解析逻辑（由 <see cref="ProxyDnsCache"/> 调用，已做 TTL 与并发合并）。</summary>
        Task<IPAddress[]?> ResolveCoreAsync(
            string host, bool ipv6, IPAddress? dnsServer, CancellationToken cancellationToken)
        {
            if (dnsServer != null)
            {
                return dnsAnalysis.AnalysisDomainIpByCustomDns(host, new[] { dnsServer }, ipv6, cancellationToken);
            }

            if (!OperatingSystem2.IsWindows && !IsSystemProxy)
            {
                // 非 Windows 的 hosts 加速模式下不能用系统默认 DNS：
                // 否则会解析到我们自己写入 hosts 的 127.0.0.1，形成无限回环
                return dnsAnalysis.AnalysisDomainIpByAliDns(host, ipv6, cancellationToken);
            }

            return dnsAnalysis.AnalysisDomainIp(host, ipv6, cancellationToken);
        }

        #endregion

        #region 代理事件

        /// <summary>
        /// 透明代理模式：决定哪些 SNI 需要解密以便改写。
        /// </summary>
        Task OnBeforeSslAuthenticate(object sender, BeforeSslAuthenticateEventArgs e)
        {
            e.DecryptSsl = false;

            if (e.SniHostName.Contains(IHttpProxyService.LocalDomain, StringComparison.OrdinalIgnoreCase))
            {
                e.DecryptSsl = true;
                return Task.CompletedTask;
            }

            if (runtimeSettings.HostMatcher.TryMatch(e.SniHostName, null, out var project))
            {
                e.ForwardHttpsHostName = project.ServerName;
                e.ForwardHttpsPort = project.PortId;
                e.DecryptSsl = true;
            }

            return Task.CompletedTask;
        }

        /// <summary>显式代理模式的 CONNECT 隧道：决定是否解密并指定上游。</summary>
        async Task OnBeforeTunnelConnect(object sender, TunnelConnectSessionEventArgs e)
        {
            e.DecryptSsl = false;

            var settings = runtimeSettings;
            var request = e.HttpClient?.Request;
            if (request?.Host == null) return;

            if (request.Host.Contains(IHttpProxyService.LocalDomain, StringComparison.OrdinalIgnoreCase))
            {
                e.DecryptSsl = true;
                return;
            }

            if (!settings.ShouldAccelerate) return;

            if (!settings.HostMatcher.TryMatch(request.Host, request.Url, out var project))
            {
                return;
            }

            e.DecryptSsl = true;

            if (project.ProxyType != ProxyType.Local && project.ProxyType != ProxyType.ServerAccelerate)
            {
                return;
            }

            var address = project.ForwardDomainIsNameOrIP ? project.ForwardDomainName : project.ForwardDomainIP;
            var ip = await ResolveUpstreamAsync(address, settings.ProxyDns, project.ForwardDomainIsNameOrIP)
                .ConfigureAwait(false);

            if (ip != null && !IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any))
            {
                e.HttpClient.UpStreamEndPoint = new IPEndPoint(ip, project.PortId);
            }
        }

        /// <summary>
        /// 上游证书一律接受。
        /// <para>
        /// 注意：这会放宽上游 TLS 校验，是本工具为兼容自建/镜像节点而做的既有取舍，
        /// 不是本次重构引入的；改动它会破坏部分加速节点的可用性。
        /// </para>
        /// </summary>
        static Task OnCertificateValidation(object sender, CertificateValidationEventArgs e)
        {
            e.IsValid = true;
            return Task.CompletedTask;
        }

        #endregion

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            StopProxy();
            dnsCache.Dispose();
            proxyServer.Dispose();
        }
    }
}
