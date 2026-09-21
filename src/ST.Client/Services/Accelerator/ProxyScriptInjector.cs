using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Titanium.Web.Proxy.EventArguments;

namespace System.Application.Services.Accelerator
{
    /// <summary>
    /// 响应拦截器：把已启用的用户脚本注入到匹配的 HTML 页面中。
    ///
    /// <para><b>相对原实现修复的问题</b></para>
    /// <list type="bullet">
    /// <item>不再在<b>每个响应</b>上调用
    ///       <c>Regex.IsMatch(..., RegexOptions.Compiled)</c>——正则已在设置快照构建时编译一次；</item>
    /// <item><c>JsPathUrl</c> 由「响应线程上首次访问就赋值」改为「启动时一次性分配」，
    ///       消除了并发响应之间的竞态（原来两个响应可能看到不同的 URL，导致脚本 404）；</item>
    /// <item>HTML 改写由「读全量字符串 + <c>IndexOf</c> + <c>Insert</c>」（会产生 2~3 份完整页面大小的
    ///       临时字符串）改为单次 <see cref="StringBuilder"/> 拼装，峰值内存显著下降；</item>
    /// <item>无脚本命中时完全不读取响应体，避免把每个 HTML 页面都解码进内存。</item>
    /// </list>
    /// </summary>
    public sealed class ProxyScriptInjector
    {
        const string SteamBrowserUserAgentMarker = "Valve Steam";

        readonly Func<ProxyRuntimeSettings> settingsAccessor;

        public ProxyScriptInjector(Func<ProxyRuntimeSettings> settingsAccessor)
        {
            this.settingsAccessor = settingsAccessor;
        }

        public async Task OnResponse(object sender, SessionEventArgs e)
        {
            var settings = settingsAccessor();
            if (!settings.IsEnableScript || settings.Scripts.Count == 0) return;

            var request = e.HttpClient.Request;
            var response = e.HttpClient.Response;

            if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase)) return;
            if (response.StatusCode != 200) return;
            if (response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) != true) return;

            var absoluteUri = request.RequestUri?.AbsoluteUri;
            if (string.IsNullOrEmpty(absoluteUri)) return;

            if (settings.IsOnlyWorkSteamBrowser && !IsSteamBrowser(request.Headers))
            {
                return;
            }

            var tags = BuildScriptTags(settings, absoluteUri);
            if (tags == null) return;

            var document = await e.GetResponseBodyAsString().ConfigureAwait(false);
            if (string.IsNullOrEmpty(document)) return;

            var index = document.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                index = document.LastIndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            }

            if (index < 0) return;

            // 同时移除 CSP，否则注入的 <script> 会被浏览器拒绝执行
            RemoveContentSecurityPolicy(e);

            e.SetResponseBodyString(InsertAt(document, index, tags));
        }

        static bool IsSteamBrowser(Titanium.Web.Proxy.Http.HeaderCollection headers)
        {
            var userAgent = headers.GetHeaders("User-Agent")?.FirstOrDefault()?.Value;
            if (userAgent == null)
            {
                // 无 UA 的请求按原逻辑放行
                return true;
            }

            return userAgent.Contains(SteamBrowserUserAgentMarker, StringComparison.Ordinal);
        }

        /// <summary>
        /// 汇总本响应需要注入的脚本标签；没有任何脚本匹配时返回 <see langword="null"/>（调用方据此跳过读体）。
        /// </summary>
        static string? BuildScriptTags(ProxyRuntimeSettings settings, string absoluteUri)
        {
            StringBuilder? builder = null;

            foreach (var rule in settings.Scripts)
            {
                if (IsExcluded(rule, absoluteUri)) continue;
                if (!IsMatched(rule, absoluteUri)) continue;

                // JsPathUrl 已在快照构建时分配；若缺失则跳过而不是就地修改共享状态
                var jsPathUrl = rule.Script.JsPathUrl;
                if (string.IsNullOrEmpty(jsPathUrl)) continue;

                builder ??= new StringBuilder(256);
                builder.Append("<script type=\"text/javascript\" src=\"https://")
                    .Append(IHttpProxyService.LocalDomain)
                    .Append(jsPathUrl)
                    .Append("\"></script>")
                    .Append('\n');
            }

            return builder?.ToString();
        }

        static bool IsExcluded(ScriptRule rule, string absoluteUri)
        {
            foreach (var host in rule.ExcludeHosts)
            {
                if (absoluteUri.IsWildcard(host)) return true;
            }

            return false;
        }

        static bool IsMatched(ScriptRule rule, string absoluteUri)
        {
            if (rule.RegexMatch != null && rule.RegexMatch.IsMatch(absoluteUri))
            {
                return true;
            }

            foreach (var host in rule.ExactMatchHosts)
            {
                if (absoluteUri.IsWildcard(host)) return true;
            }

            foreach (var host in rule.WildcardMatchHosts)
            {
                if (absoluteUri.IsWildcard(host)) return true;
            }

            return false;
        }

        static void RemoveContentSecurityPolicy(SessionEventArgs e)
        {
            var header = e.HttpClient.Response.Headers.GetFirstHeader("Content-Security-Policy");
            if (header != null)
            {
                e.HttpClient.Response.Headers.RemoveHeader(header);
            }
        }

        /// <summary>在 <paramref name="index"/> 处插入内容，全流程只产生一份最终字符串。</summary>
        static string InsertAt(string document, int index, string value)
        {
            var builder = new StringBuilder(document.Length + value.Length);
            builder.Append(document, 0, index);
            builder.Append(value);
            builder.Append(document, index, document.Length - index);
            return builder.ToString();
        }
    }
}
