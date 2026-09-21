using System.Application.Models;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace System.Application.Services.Accelerator
{
    /// <summary>
    /// 代理运行期设置的<b>不可变快照</b>。
    ///
    /// <para><b>为什么用快照</b></para>
    /// <para>
    /// 原实现把 <c>IsIpv6Support</c> 等状态放在 <see langword="static"/> 可变字段里，
    /// 启动代理时写入、请求线程上读取，属于典型的数据竞争：可见性无保证，会偶发
    /// 解析到不符合预期的地址。此外热路径上还要反复读取若干个可变属性。
    /// </para>
    /// <para>
    /// 改为：设置变更时构造一个新的不可变快照，用一次引用赋值发布；请求线程只在入口
    /// 读取一次引用。这样既消除竞争，也省掉热路径上的重复属性访问。
    /// </para>
    /// </summary>
    public sealed class ProxyRuntimeSettings
    {
        /// <summary>空设置，代理未启动时使用。</summary>
        public static readonly ProxyRuntimeSettings Disabled = new();

        ProxyRuntimeSettings()
        {
            HostMatcher = ProxyHostMatcher.Empty;
            Scripts = Array.Empty<ScriptRule>();
        }

        internal ProxyRuntimeSettings(
            ProxyHostMatcher hostMatcher,
            IReadOnlyList<ScriptRule> scripts,
            IPAddress? proxyDns,
            bool isIpv6Support,
            bool twoLevelAgentEnable,
            bool onlyEnableProxyScript,
            bool isEnableScript,
            bool isOnlyWorkSteamBrowser)
        {
            HostMatcher = hostMatcher ?? ProxyHostMatcher.Empty;
            Scripts = scripts ?? (IReadOnlyList<ScriptRule>)Array.Empty<ScriptRule>();
            ProxyDns = proxyDns;
            IsIpv6Support = isIpv6Support;
            TwoLevelAgentEnable = twoLevelAgentEnable;
            OnlyEnableProxyScript = onlyEnableProxyScript;
            IsEnableScript = isEnableScript;
            IsOnlyWorkSteamBrowser = isOnlyWorkSteamBrowser;
        }

        /// <summary>加速项目域名匹配器。</summary>
        public ProxyHostMatcher HostMatcher { get; }

        /// <summary>已启用的注入脚本（匹配规则已预编译）。</summary>
        public IReadOnlyList<ScriptRule> Scripts { get; }

        /// <summary>自定义上游 DNS；<see langword="null"/> 表示使用系统默认 DNS。</summary>
        public IPAddress? ProxyDns { get; }

        /// <summary>本机是否具备 IPv6 出口能力。</summary>
        public bool IsIpv6Support { get; }

        /// <summary>是否启用二级代理（启用时不再做本地加速改写）。</summary>
        public bool TwoLevelAgentEnable { get; }

        /// <summary>是否只注入脚本而不做加速改写。</summary>
        public bool OnlyEnableProxyScript { get; }

        /// <summary>是否启用脚本注入。</summary>
        public bool IsEnableScript { get; }

        /// <summary>是否仅对 Steam 内置浏览器注入脚本。</summary>
        public bool IsOnlyWorkSteamBrowser { get; }

        /// <summary>是否需要执行加速改写（既非二级代理、也非仅脚本模式）。</summary>
        public bool ShouldAccelerate => !TwoLevelAgentEnable && !OnlyEnableProxyScript;
    }

    /// <summary>
    /// 一条已预处理好的注入脚本规则。
    ///
    /// <para>
    /// 原实现在<b>每个响应</b>上对每个脚本重新执行
    /// <c>Regex.IsMatch(..., RegexOptions.Compiled)</c> 与若干次通配符匹配。
    /// 前者会为每次调用生成并 JIT 一份新的正则程序，是明显的 CPU 与分配热点；
    /// 这里把匹配规则在设置变更时一次性编译好，热路径只做必要的字符串比较。
    /// </para>
    /// </summary>
    public sealed class ScriptRule
    {
        internal ScriptRule(
            ScriptDTO script,
            string[] exactMatchHosts,
            string[] wildcardMatchHosts,
            Regex? regexMatch,
            string[] excludeHosts)
        {
            Script = script;
            ExactMatchHosts = exactMatchHosts;
            WildcardMatchHosts = wildcardMatchHosts;
            RegexMatch = regexMatch;
            ExcludeHosts = excludeHosts;
        }

        public ScriptDTO Script { get; }

        /// <summary>形如 <c>github.com</c> 的精确主机规则。</summary>
        public string[] ExactMatchHosts { get; }

        /// <summary>含 <c>*</c> 的通配符规则。</summary>
        public string[] WildcardMatchHosts { get; }

        /// <summary>以 <c>/</c> 开头的正则规则（单实例、已编译）。</summary>
        public Regex? RegexMatch { get; }

        /// <summary>排除规则。</summary>
        public string[] ExcludeHosts { get; }
    }
}
