using System;
using System.Application.Models;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Application.Services.Accelerator
{
    /// <summary>
    /// 加速项目域名匹配器。
    /// <para>
    /// 原实现每收到一个请求都要把 <c>ProxyDomains</c> 与其中每个条目的
    /// <c>DomainNamesArray</c> 全部展开做一次 <see cref="string.Contains(string, StringComparison)"/>，
    /// 时间复杂度为 O(请求数 × 项目数 × 域名数)，且每次都会产生枚举器与字符串比较开销。
    /// </para>
    /// <para>
    /// 本实现改为在配置变更时<b>一次性</b>构建索引，请求路径上只做：
    /// <list type="number">
    /// <item>规范化 Host（小写、去端口）；</item>
    /// <item>哈希字典精确命中；</item>
    /// <item>必要时做「点边界」后缀匹配（如 <c>a.github.com</c> 命中 <c>github.com</c>）；</item>
    /// <item>仅当规则本身含路径/Scheme 时才回退到完整 URI 子串匹配。</item>
    /// </list>
    /// 同时修正了原子串匹配的安全缺陷：<c>evil-github.com</c> 不再误命中 <c>github.com</c>。
    /// </para>
    /// </summary>
    public sealed class ProxyHostMatcher
    {
        /// <summary>不含任何规则的匹配器，用于替代 <see langword="null"/> 判断以减少分支。</summary>
        public static readonly ProxyHostMatcher Empty = new(
            new Dictionary<string, HostRule>(StringComparer.Ordinal),
            Array.Empty<HostRule>(),
            Array.Empty<UriRule>());

        /// <summary>规则条目。</summary>
        public readonly struct HostRule
        {
            public HostRule(string host, AccelerateProjectDTO project)
            {
                Host = host;
                Project = project;
            }

            /// <summary>已规范化为小写的纯主机名（不含端口）。</summary>
            public string Host { get; }

            public AccelerateProjectDTO Project { get; }
        }

        /// <summary>需要按完整 URI 匹配的规则（规则值本身带 Scheme 或路径）。</summary>
        readonly struct UriRule
        {
            public UriRule(string fragment, AccelerateProjectDTO project)
            {
                Fragment = fragment;
                Project = project;
            }

            public string Fragment { get; }

            public AccelerateProjectDTO Project { get; }
        }

        readonly Dictionary<string, HostRule> exactIndex;
        readonly HostRule[] suffixRules;   // 便于按长度倒序做后缀匹配
        readonly UriRule[] uriRules;

        ProxyHostMatcher(Dictionary<string, HostRule> exactIndex, HostRule[] suffixRules, UriRule[] uriRules)
        {
            this.exactIndex = exactIndex;
            this.suffixRules = suffixRules;
            this.uriRules = uriRules;
        }

        /// <summary>规则条数（用于诊断）。</summary>
        public int RuleCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => exactIndex.Count + suffixRules.Length + uriRules.Length;
        }

        /// <summary>
        /// 根据加速项目集合构建匹配器。传入 <see langword="null"/> 或空集合返回 <see cref="Empty"/>。
        /// </summary>
        public static ProxyHostMatcher Build(IEnumerable<AccelerateProjectDTO>? projects)
        {
            if (projects == null) return Empty;

            var exact = new Dictionary<string, HostRule>(StringComparer.Ordinal);
            var suffix = new List<HostRule>();
            var uri = new List<UriRule>();

            foreach (var project in projects)
            {
                if (project == null) continue;

                foreach (var raw in project.DomainNamesArray)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    var value = raw.Trim();

                    // 规则里出现 Scheme / 路径 / 通配符 / 端口，说明它不是纯主机名
                    if (value.Contains('/') ||
                        value.Contains('*') ||
                        value.Contains(':'))
                    {
                        uri.Add(new UriRule(value, project));
                        continue;
                    }

                    var host = value.ToLowerInvariant();

                    if (host.StartsWith(".", StringComparison.Ordinal))
                    {
                        // ".github.com" 语义为「github.com 及其子域」
                        host = host[1..];
                        if (host.Length == 0) continue;
                        suffix.Add(new HostRule(host, project));
                        continue;
                    }

                    // 同一主机名可能被多个项目声明，先到先得（与原实现首次命中即返回的语义一致）
                    if (!exact.ContainsKey(host))
                    {
                        exact[host] = new HostRule(host, project);
                        // 精确规则同时参与子域后缀匹配
                        suffix.Add(new HostRule(host, project));
                    }
                }
            }

            // 长后缀优先，保证 a.b.github.com 命中更具体的规则
            suffix.Sort(static (x, y) => y.Host.Length.CompareTo(x.Host.Length));

            return new ProxyHostMatcher(exact, suffix.ToArray(), uri.ToArray());
        }

        /// <summary>
        /// 尝试为请求找到匹配的加速项目。
        /// </summary>
        /// <param name="host">请求 Host（可能带端口）。</param>
        /// <param name="absoluteUri">请求完整 URI，仅在存在 URI 规则时使用。</param>
        /// <param name="project">命中的加速项目。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryMatch(string? host, string? absoluteUri, [NotNullWhen(true)] out AccelerateProjectDTO? project)
        {
            project = null;
            if (host == null) return false;

            var normalized = NormalizeHost(host);

            if (exactIndex.TryGetValue(normalized, out var exactRule))
            {
                project = exactRule.Project;
                return true;
            }

            // suffixRules 已按长度倒序，第一个命中的即最具体的规则
            foreach (var rule in suffixRules)
            {
                if (!normalized.EndsWith(rule.Host, StringComparison.Ordinal)) continue;

                // 必须落在「点边界」上：a.github.com 命中 github.com，但 notgithub.com 不命中
                var boundary = normalized.Length - rule.Host.Length;
                if (boundary == 0 ||
                    (boundary > 0 && normalized[boundary - 1] == '.'))
                {
                    project = rule.Project;
                    return true;
                }
            }

            if (uriRules.Length > 0 && absoluteUri != null)
            {
                foreach (var rule in uriRules)
                {
                    if (absoluteUri.Contains(rule.Fragment, StringComparison.OrdinalIgnoreCase))
                    {
                        project = rule.Project;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>去除端口并转为小写，避免在请求热路径上反复分配。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static string NormalizeHost(string host)
        {
            var colon = host.IndexOf(':');
            var value = colon >= 0 ? host[..colon] : host;

            // 只有确有大写时才分配新串
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c >= 'A' && c <= 'Z')
                {
                    return value.ToLowerInvariant();
                }
            }

            return value;
        }
    }
}
