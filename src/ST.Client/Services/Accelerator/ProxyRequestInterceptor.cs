using System.Application.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;
using Titanium.Web.Proxy.StreamExtended.Models;

namespace System.Application.Services.Accelerator
{
    /// <summary>
    /// 代理请求拦截器：决定一个请求是「本机脚本服务请求」还是「需要加速的上游请求」，
    /// 并在需要时改写上游地址、端口与 SNI。
    ///
    /// <para><b>相对原实现修复的问题</b></para>
    /// <list type="bullet">
    /// <item>用 <see cref="ProxyHostMatcher"/> 取代「遍历所有项目 × 遍历所有域名 × 子串匹配」，
    ///       消除了 O(项目 × 域名) 的热路径开销与 <c>evil-github.com</c> 误命中问题；</item>
    /// <item>HTTP→HTTPS 升级改用 <see cref="UriBuilder"/>，不再用
    ///       <c>Remove(0,4).Insert(0,"https")</c> 这种依赖字符串长度的脆弱写法；</item>
    /// <item>重定向域名改写同样改用 <see cref="UriBuilder"/>，避免 <c>string.Replace</c>
    ///       误伤路径与查询串中的同名片段；</item>
    /// <item>上游 IP 解析走带缓存的 <see cref="ProxyDnsCache"/>（由构造函数注入的委托间接使用）；</item>
    /// <item><c>local.steampp.net</c> 的 POST 转发不再写错 <c>Content-Length</c>
    ///       （原来传的是「字符数」而不是「字节数」），也不再因为漏掉 Flush 而丢失请求体。</item>
    /// </list>
    /// </summary>
    public sealed class ProxyRequestInterceptor
    {
        /// <summary>解析上游地址的委托：域名、自定义 DNS、是否按域名解析。</summary>
        public delegate Task<IPAddress?> UpstreamResolver(string host, IPAddress? dnsServer, bool isDomain);

        readonly Func<ProxyRuntimeSettings> settingsAccessor;
        readonly UpstreamResolver resolveUpstream;

        static readonly IList<HttpHeader> JsContentTypeHeader = new List<HttpHeader>
        {
            new HttpHeader("Content-Type", "text/javascript;charset=UTF-8"),
        };

        public ProxyRequestInterceptor(
            Func<ProxyRuntimeSettings> settingsAccessor,
            UpstreamResolver resolveUpstream)
        {
            this.settingsAccessor = settingsAccessor;
            this.resolveUpstream = resolveUpstream;
        }

        public async Task OnRequest(object sender, SessionEventArgs e)
        {
            var request = e.HttpClient.Request;
            var host = request.Host;
            if (host == null) return;

            // 读取一次快照，后续全部基于同一个不可变对象
            var settings = settingsAccessor();

            if (host.Contains(IHttpProxyService.LocalDomain, StringComparison.OrdinalIgnoreCase))
            {
                await HandleLocalDomainAsync(e, settings).ConfigureAwait(false);
                return;
            }

            if (!settings.ShouldAccelerate) return;
            if (settings.HostMatcher.RuleCount == 0) return;

            if (!settings.HostMatcher.TryMatch(host, request.RequestUri?.AbsoluteUri, out var project))
            {
                await HandleUnmatchedAsync(e, settings).ConfigureAwait(false);
                return;
            }

            await ApplyAccelerationAsync(e, project, settings).ConfigureAwait(false);
        }

        /// <summary>处理指向 <c>local.steampp.net</c> 的请求：脚本内容与跨域转发。</summary>
        async Task HandleLocalDomainAsync(SessionEventArgs e, ProxyRuntimeSettings settings)
        {
            var request = e.HttpClient.Request;

            if (string.Equals(request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                e.Ok(string.Empty, BuildCorsHeaders(e));
                return;
            }

            var requestType = request.Headers.GetFirstHeader("requestType")?.Value;
            if (string.Equals(requestType, "xhr", StringComparison.OrdinalIgnoreCase))
            {
                await ForwardXhrAsync(e).ConfigureAwait(false);
                return;
            }

            // 其余情况按「注入脚本的 JS 资源」返回
            var path = request.RequestUri?.LocalPath;
            var content = FindScriptContent(settings, path);

            if (string.IsNullOrEmpty(content))
            {
                e.Ok("404", JsContentTypeHeader);
                return;
            }

            e.Ok(content, JsContentTypeHeader);
        }

        static string? FindScriptContent(ProxyRuntimeSettings settings, string? path)
        {
            if (path == null || settings.Scripts.Count == 0) return null;

            foreach (var rule in settings.Scripts)
            {
                var jsPathUrl = rule.Script.JsPathUrl;
                if (jsPathUrl != null && string.Equals(jsPathUrl, path, StringComparison.Ordinal))
                {
                    return rule.Script.Content;
                }
            }

            return null;
        }

        /// <summary>
        /// 转发注入脚本发出的 XHR。这是「跨域读取任意页面内容」的通道，属于脚本能力的一部分。
        /// </summary>
        static async Task ForwardXhrAsync(SessionEventArgs e)
        {
            var request = e.HttpClient.Request;
            var query = request.RequestUri?.Query;
            if (string.IsNullOrEmpty(query))
            {
                e.Ok("500", BuildCorsHeaders(e));
                return;
            }

            // 原始实现：Query.Replace("?request=", "") 后 UrlDecode
            var encoded = query.StartsWith("?request=", StringComparison.Ordinal)
                ? query["?request=".Length..]
                : query.TrimStart('?');
            var url = Web.HttpUtility.UrlDecode(encoded);

            if (string.IsNullOrWhiteSpace(url) || !Browser2.IsHttpUrl(url))
            {
                e.Ok("400", BuildCorsHeaders(e));
                return;
            }

            var cookie = request.Headers.GetFirstHeader("cookie-steamTool")?.Value
                         ?? request.Headers.GetFirstHeader("Cookie")?.Value;

            var headers = BuildCorsHeaders(e);
            if (request.ContentType != null)
            {
                headers.Add(new HttpHeader("Content-Type", request.ContentType));
            }

            try
            {
                if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    var body = await IHttpService.Instance.GetAsync<string>(url, cookie: cookie).ConfigureAwait(false);
                    e.Ok(body ?? "500", headers);
                    return;
                }

                if (string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    if (request.ContentLength <= 0)
                    {
                        e.Ok("500", headers);
                        return;
                    }

                    var mediaType = request.ContentType;
                    var bodyString = request.BodyString;

                    var content = await IHttpService.Instance.SendAsync<string>(
                        url,
                        () =>
                        {
                            // 修复点：用 StringContent 让 HttpClient 自行计算 Byte 长度的 Content-Length，
                            // 不再把「字符数」当作「字节数」写入（含中文时会直接截断请求体）
                            var httpContent = new StringContent(bodyString, Encoding.UTF8);
                            if (!string.IsNullOrWhiteSpace(mediaType) &&
                                MediaTypeHeaderValue.TryParse(mediaType, out var parsed))
                            {
                                httpContent.Headers.ContentType = parsed;
                            }

                            return new HttpRequestMessage(HttpMethod.Post, url)
                            {
                                Content = httpContent,
                            };
                        },
                        null,
                        default).ConfigureAwait(false);

                    e.Ok(content ?? "500", headers);
                    return;
                }

                e.Ok("405", headers);
            }
            catch (Exception error)
            {
                Log.Error(nameof(ProxyRequestInterceptor), error, "ForwardXhr");
                e.Ok(error.Message ?? "500", headers);
            }
        }

        static List<HttpHeader> BuildCorsHeaders(SessionEventArgs e)
        {
            var origin = e.HttpClient.Request.Headers.GetFirstHeader("Origin")?.Value ?? "*";
            return new List<HttpHeader>
            {
                new HttpHeader("Access-Control-Allow-Origin", origin),
                new HttpHeader("Access-Control-Allow-Headers", "*"),
                new HttpHeader("Access-Control-Allow-Methods", "*"),
                new HttpHeader("Access-Control-Allow-Credentials", "true"),
            };
        }

        /// <summary>执行加速改写：升级到 HTTPS、重定向或指定上游 IP 与 SNI。</summary>
        async Task ApplyAccelerationAsync(SessionEventArgs e, AccelerateProjectDTO project, ProxyRuntimeSettings settings)
        {
            var request = e.HttpClient.Request;
            var uri = request.RequestUri;
            if (uri == null) return;

            // 强制走 HTTPS，避免上游以明文响应被中间设备改写
            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                uri = new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri;
                request.RequestUri = uri;
            }

            if (project.Redirect)
            {
                var rewritten = BuildRedirectUri(uri, project);
                if (rewritten != null)
                {
                    request.RequestUri = rewritten;
                }
                return;
            }

            await AssignUpstreamAsync(e, project, settings).ConfigureAwait(false);
            ApplySniExtension(e, project);
        }

        /// <summary>
        /// 依据 <c>ForwardDomainName</c> 中的 <c>{path}</c> / <c>{args}</c> 占位符构造重定向地址。
        /// </summary>
        static Uri? BuildRedirectUri(Uri original, AccelerateProjectDTO project)
        {
            var target = project.ForwardDomainName;
            if (string.IsNullOrWhiteSpace(target)) return null;

            target = target.Replace("{path}", original.AbsolutePath);
            target = target.Replace("{args}", original.Query);

            if (Browser2.IsHttpUrl(target))
            {
                return Uri.TryCreate(target, UriKind.Absolute, out var absolute) ? absolute : null;
            }

            // 仅替换主机（保留原 scheme / path / query）
            return Uri.TryCreate(
                new UriBuilder(original) { Host = target, Port = -1 }.Uri.ToString(),
                UriKind.Absolute,
                out var replaced)
                ? replaced
                : null;
        }

        /// <summary>为本请求指定上游 IP:Port（若尚未指定）。</summary>
        async Task AssignUpstreamAsync(SessionEventArgs e, AccelerateProjectDTO project, ProxyRuntimeSettings settings)
        {
            if (e.HttpClient.UpStreamEndPoint != null) return;

            var address = project.ForwardDomainIsNameOrIP ? project.ForwardDomainName : project.ForwardDomainIP;
            if (string.IsNullOrWhiteSpace(address)) return;

            var ip = await resolveUpstream(address, settings.ProxyDns, project.ForwardDomainIsNameOrIP)
                .ConfigureAwait(false);

            if (ip == null || IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any))
            {
                // 解析失败或解析回环地址：保持默认路由，让代理按原始目标直连，
                // 而不是把请求导向一个不可用的地址（原来会直接跳出全部匹配）
                return;
            }

            e.HttpClient.UpStreamEndPoint = new IPEndPoint(ip, project.PortId);
        }

        /// <summary>按项目配置改写或移除 TLS SNI 扩展。</summary>
        static void ApplySniExtension(SessionEventArgs e, AccelerateProjectDTO project)
        {
            var extensions = e.HttpClient.ConnectRequest?.ClientHelloInfo?.Extensions;
            if (extensions == null) return;

            if (!string.IsNullOrEmpty(project.ServerName))
            {
                var sni = extensions.GetValueOrDefault("server_name");
                if (sni != null)
                {
                    extensions["server_name"] = new SslExtension(
                        sni.Value, sni.Name, project.ServerName, sni.Position);
                }
            }
            else
            {
                extensions.Remove("server_name");
            }
        }

        /// <summary>
        /// 未命中任何加速项目时的兜底：部分运营商会把未知域名解析到 127.0.0.1，
        /// 这里用 Ali DNS 复核一次，确认是污染就终止会话，否则按解析结果指定上游。
        /// </summary>
        async Task HandleUnmatchedAsync(SessionEventArgs e, ProxyRuntimeSettings settings)
        {
            var remote = e.ClientRemoteEndPoint;
            if (remote == null || !IPAddress.IsLoopback(remote.Address)) return;

            var host = e.HttpClient.Request.Host;
            if (string.IsNullOrWhiteSpace(host)) return;

            var ip = await resolveUpstream(host, null, true).ConfigureAwait(false);

            if (ip == null || IPAddress.IsLoopback(ip))
            {
                if (e.HttpClient.Request.RequestUri != null)
                {
                    Log.Info(nameof(ProxyRequestInterceptor),
                        "IsLoopback OnRequest: " + e.HttpClient.Request.RequestUri.AbsoluteUri);
                }

                e.TerminateSession();
            }
            else
            {
                e.HttpClient.UpStreamEndPoint = new IPEndPoint(ip, remote.Port);
            }
        }
    }
}
