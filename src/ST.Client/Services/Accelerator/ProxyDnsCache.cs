using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace System.Application.Services.Accelerator
{
    /// <summary>
    /// 带 TTL 与「单飞（single-flight）」合并的 DNS 结果缓存。
    ///
    /// <para><b>为什么需要它</b></para>
    /// <para>
    /// 原来的请求拦截器会对<b>每一个</b>进入代理的请求调用一次 DNS 解析来求解上游 IP。
    /// 浏览器打开一个页面通常产生数十到上百个请求，同一域名会在几十毫秒内被反复解析：
    /// 既给上游 DNS 造成压力（易被限流从而表现为「加速失效」），又持续产生大量短命
    /// 对象与并发 Task，直接抬高 GC 频率与内存峰值。
    /// </para>
    ///
    /// <para><b>三条设计约束</b></para>
    /// <list type="number">
    /// <item>TTL 内命中直接返回，热路径不分配；</item>
    /// <item>同一键的并发查询合并为一次真实查询，其余调用方共享同一个 <see cref="Task{TResult}"/>；</item>
    /// <item>容量有上界：超限时先清过期项，再按插入序淘汰，保证长期运行内存不无界增长。</item>
    /// </list>
    /// </summary>
    public sealed class ProxyDnsCache : IDisposable
    {
        /// <summary>缓存键：域名 + 是否 IPv6 + 自定义 DNS 服务器。</summary>
        readonly struct CacheKey : IEquatable<CacheKey>
        {
            public CacheKey(string host, bool ipv6, IPAddress? server)
            {
                Host = host;
                IsIPv6 = ipv6;
                Server = server;
            }

            public string Host { get; }
            public bool IsIPv6 { get; }
            public IPAddress? Server { get; }

            public bool Equals(CacheKey other)
                => IsIPv6 == other.IsIPv6
                && string.Equals(Host, other.Host, StringComparison.Ordinal)
                && Equals(Server, other.Server);

            public override bool Equals(object? obj) => obj is CacheKey other && Equals(other);

            public override int GetHashCode()
            {
                var hash = new HashCode();
                hash.Add(Host, StringComparer.Ordinal);
                hash.Add(IsIPv6);
                hash.Add(Server);
                return hash.ToHashCode();
            }
        }

        sealed class Entry
        {
            public Entry(IPAddress[]? addresses, long expiresAt, Task<IPAddress[]?>? inFlight)
            {
                Addresses = addresses;
                ExpiresAt = expiresAt;
                InFlight = inFlight;
            }

            public IPAddress[]? Addresses { get; }
            public long ExpiresAt { get; }
            public Task<IPAddress[]?>? InFlight { get; }
        }

        /// <summary>解析委托签名：输入域名与是否 IPv6，输出地址数组。</summary>
        public delegate Task<IPAddress[]?> Resolver(string host, bool ipv6, IPAddress? dnsServer, CancellationToken cancellationToken);

        readonly object gate = new();
        readonly Dictionary<CacheKey, Entry> cache = new();
        readonly Queue<CacheKey> insertionOrder = new();
        readonly Resolver resolver;
        readonly TimeSpan positiveTtl;
        readonly TimeSpan negativeTtl;
        readonly int maxEntries;

        long hits;
        long misses;
        long coalesced;
        bool disposed;

        /// <param name="resolver">真实解析实现。</param>
        /// <param name="positiveTtl">解析成功结果的存活时间。</param>
        /// <param name="negativeTtl">解析失败结果的抑制时间。</param>
        /// <param name="maxEntries">缓存条目上限。</param>
        public ProxyDnsCache(
            Resolver resolver,
            TimeSpan? positiveTtl = null,
            TimeSpan? negativeTtl = null,
            int maxEntries = 1024)
        {
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            this.positiveTtl = positiveTtl ?? TimeSpan.FromSeconds(60);
            this.negativeTtl = negativeTtl ?? TimeSpan.FromSeconds(5);
            this.maxEntries = Math.Max(16, maxEntries);
        }

        /// <summary>命中缓存（含负缓存）的次数。</summary>
        public long Hits => Interlocked.Read(ref hits);

        /// <summary>未命中、需要发起真实查询的次数。</summary>
        public long Misses => Interlocked.Read(ref misses);

        /// <summary>被单飞合并掉、从而省下的真实查询次数。</summary>
        public long Coalesced => Interlocked.Read(ref coalesced);

        /// <summary>当前缓存条目数。</summary>
        public int Count { get { lock (gate) return cache.Count; } }

        static long Now => Stopwatch.GetTimestamp();

        static long ToTicks(TimeSpan value) => (long)(value.TotalSeconds * Stopwatch.Frequency);

        /// <summary>
        /// 获取（或解析）指定域名的地址。相同键的并发调用只会触发一次 <see cref="Resolver"/>。
        /// </summary>
        public Task<IPAddress[]?> GetOrAddAsync(
            string host,
            bool ipv6 = false,
            IPAddress? dnsServer = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return Task.FromResult<IPAddress[]?>(null);
            }

            var key = new CacheKey(host, ipv6, dnsServer);
            var now = Now;

            lock (gate)
            {
                if (cache.TryGetValue(key, out var entry))
                {
                    if (entry.Addresses is { Length: > 0 } && entry.ExpiresAt > now)
                    {
                        Interlocked.Increment(ref hits);
                        return Task.FromResult<IPAddress[]?>(entry.Addresses);
                    }

                    if (entry.InFlight != null)
                    {
                        // 已有同键查询在飞，合并
                        Interlocked.Increment(ref coalesced);
                        return entry.InFlight;
                    }

                    if (entry.ExpiresAt > now)
                    {
                        // 负缓存窗口内，直接返回失败
                        Interlocked.Increment(ref hits);
                        return Task.FromResult<IPAddress[]?>(null);
                    }
                }

                Interlocked.Increment(ref misses);
                return StartQueryLocked(key);
            }
        }

        /// <summary>必须在持有 <see cref="gate"/> 时调用。发起真实查询并登记占位条目。</summary>
        Task<IPAddress[]?> StartQueryLocked(CacheKey key)
        {
            var tcs = new TaskCompletionSource<IPAddress[]?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            cache[key] = new Entry(null, 0, tcs.Task);
            insertionOrder.Enqueue(key);

            _ = QueryAsync(key, tcs);
            return tcs.Task;
        }

        async Task QueryAsync(CacheKey key, TaskCompletionSource<IPAddress[]?> tcs)
        {
            IPAddress[]? result = null;
            Exception? failure = null;

            try
            {
                result = await resolver(key.Host, key.IsIPv6, key.Server, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            var ttl = result is { Length: > 0 } ? positiveTtl : negativeTtl;

            lock (gate)
            {
                cache[key] = new Entry(result, Now + ToTicks(ttl), null);
                TrimLocked();
            }

            if (failure == null)
            {
                tcs.TrySetResult(result);
            }
            else
            {
                tcs.TrySetException(failure);
            }
        }

        /// <summary>必须在持有 <see cref="gate"/> 时调用。</summary>
        void TrimLocked()
        {
            if (cache.Count <= maxEntries) return;

            var now = Now;

            // 先清过期
            List<CacheKey>? expired = null;
            foreach (var pair in cache)
            {
                if (pair.Value.InFlight == null && pair.Value.ExpiresAt <= now)
                {
                    (expired ??= new List<CacheKey>()).Add(pair.Key);
                }
            }

            if (expired != null)
            {
                foreach (var key in expired)
                {
                    cache.Remove(key);
                }
            }

            // 仍超限则按插入序淘汰最老的
            var excess = cache.Count - maxEntries;
            var guard = insertionOrder.Count + 1;
            while (excess > 0 && guard-- > 0 && insertionOrder.TryDequeue(out var oldest))
            {
                if (cache.Remove(oldest))
                {
                    excess--;
                }
            }
        }

        /// <summary>清空全部缓存。</summary>
        public void Clear()
        {
            lock (gate)
            {
                cache.Clear();
                insertionOrder.Clear();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Clear();
        }
    }
}
