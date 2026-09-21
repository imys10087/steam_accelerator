using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// ReSharper disable once CheckNamespace
namespace System
{
    public static partial class StringExtensions
    {
        /// <summary>
        /// 将版本号字符串转换为 <see cref="Version"/> 对象，传入的字符串不能为 <see langword="null"/>
        /// </summary>
        /// <param name="version"></param>
        /// <returns></returns>
        public static Version? VersionTryParse(string version)
        {
            var versionChars = GetVersion(version).ToArray();
            if (versionChars.Any() && Version.TryParse(new string(versionChars), out var versionObj))
            {
                return versionObj;
            }
            static IEnumerable<char> GetVersion(string s)
            {
                var dianCount = 0;
                var findChar = false;
                foreach (var item in s)
                {
                    var isDian = item == '.';
                    if (isDian)
                    {
                        if (dianCount++ >= 3)
                        {
                            yield break;
                        }
                    }
                    if (isDian || item >= '0' && item <= '9')
                    {
                        findChar = true;
                        yield return item;
                    }
                    else if (findChar)
                    {
                        yield break;
                    }
                }
            }
            return null;
        }

        #region TryParse
        /// <summary>
        /// 尝试将数字的字符串表示形式转换为 双精度浮点型
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static decimal? TryParseDecimal(this string value) => decimal.TryParse(value, out var temp) ? temp : null;
        /// <summary>
        /// 尝试将数字的字符串表示形式转换为 双精度浮点型
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static double? TryParseDouble(this string value) => double.TryParse(value, out var temp) ? temp : null;

        /// <summary>
        /// 尝试将数字的字符串表示形式转换为它的等效 16 位有符号整数
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static short? TryParseInt16(this string value) => short.TryParse(value, out var temp) ? temp : null;

        /// <summary>
        /// 尝试将数字的字符串表示形式转换为它的等效 16 位无符号整数
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static ushort? TryParseUInt16(this string value) => ushort.TryParse(value, out var temp) ? temp : null;

        /// <summary>
        /// 尝试将数字的字符串表示形式转换为它的等效 32 位有符号整数
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static int? TryParseInt32(this string value) => int.TryParse(value, out var temp) ? temp : null;

        /// <summary>
        /// 尝试将数字的字符串表示形式转换为它的等效 32 位无符号整数
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static uint? TryParseUInt32(this string value) => uint.TryParse(value, out var temp) ? temp : null;

        /// <summary>
        /// 尝试将数字的字符串表示形式转换为它的等效 64 位有符号整数
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static long? TryParseInt64(this string value) => long.TryParse(value, out var temp) ? temp : null;

        /// <summary>
        /// 尝试将数字的字符串表示形式转换为它的等效 64 位无符号整数
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static ulong? TryParseUInt64(this string value) => ulong.TryParse(value, out var temp) ? temp : null;

        /// <summary>
        /// 尝试将数字的字符串表示形式转换为它的等效 byte 值
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte? TryParseByte(this string value) => byte.TryParse(value, out var temp) ? temp : null;

        public static bool? TryParseBoolean(this string value) => bool.TryParse(value, out var temp) ? temp : null;

        /// <summary>
        /// 尝试将数字的字符串表示形式转换为它的等效 sbyte 值
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static sbyte? TryParseSByte(this string value) => sbyte.TryParse(value, out sbyte temp) ? temp : null;

        public static Version? TryParseVersion(this string value) => Version.TryParse(value, out var temp) ? temp : null;

        #endregion

        #region 数字字母判断

        /// <summary>
        /// 字符串是否是一个数字（可以零开头 无符号不能+-.）
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static bool IsDigital(this string input) => input?.All(IsDigital) ?? false;

        public static bool IsDigital(this char input) => input >= '0' && input <= '9';

        /// <summary>
        /// 字符串内含有数字
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static bool HasDigital(this string input) => input?.Any(x => x >= '0' && x <= '9') ?? false;

        /// <summary>
        /// 字符串内含有小写字母
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static bool HasLowerLetter(this string input) => input?.Any(x => x >= 'a' && x <= 'z') ?? false;

        /// <summary>
        /// 字符串内含有大写字母
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static bool HasUpperLetter(this string input) => input?.Any(x => x >= 'A' && x <= 'Z') ?? false;

        /// <summary>
        /// 字符串内含有其他字符
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static bool HasOther(this string input) => input?.Any(x => !(x >= 'a' && x <= 'z' || x >= 'A' && x <= 'Z' || x >= '0' && x <= '9')) ?? false;

        #endregion

        /// <summary>
        /// 返回当前最后相对URL
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public static string GetLastRelativeUrl(this string url)
        {
            int index = url.LastIndexOf("/");

            if (index != -1)
            {
                return url.Substring(0, index) + "/";
            }
            else
            {
                return "";
            }
        }

        /// <summary>
        /// 移除字符串内所有\r \n \t
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string RemovePattern(this string s)
            => s.Replace("\t", "").Replace("\r", "").Replace("\n", "");

        /// <summary>
        /// 获取两个字符串中间的字符串
        /// </summary>
        /// <param name="s">要处理的字符串，例 ABCD</param>
        /// <param name="left">第1个字符串，例 AB</param>
        /// <param name="right">第2个字符串，例 D</param>
        /// <param name="isContains">是否包含标志字符串</param>
        /// <returns>例 返回C</returns>
        public static string Substring(this string s, string left, string right, bool isContains = false)
        {
            int i1 = s.IndexOf(left);
            if (i1 < 0) // 找不到返回空
            {
                return "";
            }

            int i2 = s.IndexOf(right, i1 + left.Length); // 从找到的第1个字符串后再去找
            if (i2 < 0) // 找不到返回空
            {
                return "";
            }

            if (isContains) return s.Substring(i1, i2 - i1 + left.Length);
            else return s.Substring(i1 + left.Length, i2 - i1 - left.Length);
        }

        /// <summary>
        /// Converts a wildcard to a regex.
        ///
        /// <para><b>修复说明</b></para>
        /// <para>
        /// 原实现为：
        /// <c>Regex.IsMatch(str, "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.Compiled)</c>
        /// </para>
        /// <list type="number">
        /// <item><b>性能</b>：<see cref="RegexOptions.Compiled"/> 与「每次调用新建模式字符串」组合在一起，
        /// 意味着<b>每一次调用都会动态生成并 JIT 一份新的正则程序集</b>。本方法在脚本注入的响应热路径上
        /// 按「脚本数 × 规则数」被调用，是明显的 CPU 与内存分配热点。</item>
        /// <item><b>稳定性 / 安全</b>：正则模式来自可下载的第三方用户脚本，原实现没有设置匹配超时，
        /// 恶意或写得不好的模式会造成灾难性回溯，把代理线程挂住。这里统一加上 1 秒匹配超时。</item>
        /// </list>
        /// <para>
        /// 现在按模式缓存已编译实例（容量有上界），并把超时异常当作「不匹配」处理。
        /// </para>
        /// </summary>
        /// <param name="str">被匹配的字符串。</param>
        /// <param name="pattern">通配符模式，<c>*</c> 匹配任意长度，<c>?</c> 匹配单个字符。</param>
        public static bool IsWildcard(this string str, string pattern)
        {
            if (str == null || pattern == null) return false;

            var regex = WildcardRegexCache.GetOrAdd(pattern);

            try
            {
                return regex.IsMatch(str);
            }
            catch (RegexMatchTimeoutException)
            {
                // 视为不匹配，避免一个病态模式拖垮代理
                return false;
            }
        }

        /// <summary>通配符模式 → 已编译正则 的有界缓存。</summary>
        static class WildcardRegexCache
        {
            const int MaxEntries = 512;

            static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

            static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

            public static Regex GetOrAdd(string pattern)
            {
                if (Cache.TryGetValue(pattern, out var cached))
                {
                    return cached;
                }

                if (Cache.Count >= MaxEntries)
                {
                    // 简单策略：超过上限即整体清空。脚本规则数量很小，冷启动代价可接受，
                    // 但能保证长期运行时的内存上界。
                    Cache.Clear();
                }

                var regex = new Regex(
                    "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                    RegexOptions.Compiled | RegexOptions.CultureInvariant,
                    MatchTimeout);

                return Cache.GetOrAdd(pattern, regex);
            }
        }

        #region Compressor

        /// <summary>
        /// Compresses the string.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns></returns>
        [return: NotNullIfNotNull("text")]
        public static string? CompressString(this string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            byte[] buffer = Encoding.UTF8.GetBytes(text);
            var gZipBuffer = buffer.CompressByteArray();
            return Convert.ToBase64String(gZipBuffer);
        }

        /// <inheritdoc cref="CompressString(string?)"/>
        [return: NotNullIfNotNull("text")]
        public static string? CompressStringByBrotli(this string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            byte[] buffer = Encoding.UTF8.GetBytes(text);
            var gZipBuffer = buffer.CompressByteArrayByBrotli();
            return Convert.ToBase64String(gZipBuffer);
        }

        /// <summary>
        /// Decompresses the string.
        /// </summary>
        /// <param name="compressedText">The compressed text.</param>
        /// <returns></returns>
        [return: NotNullIfNotNull("compressedText")]
        public static string? DecompressString(this string? compressedText)
        {
            if (string.IsNullOrEmpty(compressedText)) return null;
            byte[] gZipBuffer = Convert.FromBase64String(compressedText);
            var buffer = gZipBuffer.DecompressByteArray();
            return Encoding.UTF8.GetString(buffer);
        }

        /// <inheritdoc cref="DecompressString(string?)"/>
        [return: NotNullIfNotNull("compressedText")]
        public static string? DecompressStringByBrotli(this string? compressedText)
        {
            if (string.IsNullOrEmpty(compressedText)) return null;
            byte[] gZipBuffer = Convert.FromBase64String(compressedText);
            var buffer = gZipBuffer.DecompressByteArrayByBrotli();
            return Encoding.UTF8.GetString(buffer);
        }

        #endregion

#if DEBUG

        [Obsolete("use GetLastRelativeUrl", true)]
        public static string GetCurrentPath(this string url) => GetCurrentPath(url);

#endif

        public static string Format(this string format, params object?[] args)
        {
            try
            {
                return string.Format(format, args);
            }
            catch
            {
                var args_ = args.ToList();
                args_.Insert(0, format);
                return string.Join(' ', args_);
            }
        }
    }
}