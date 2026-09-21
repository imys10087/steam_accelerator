using Microsoft.Extensions.Options;
using System.Linq;
using System.Properties;
using MPIgnore = MessagePack.IgnoreMemberAttribute;
using MPKey = MessagePack.KeyAttribute;
using MPObj = MessagePack.MessagePackObjectAttribute;
using N_JsonIgnore = Newtonsoft.Json.JsonIgnoreAttribute;
using N_JsonProperty = Newtonsoft.Json.JsonPropertyAttribute;
using S_JsonIgnore = System.Text.Json.Serialization.JsonIgnoreAttribute;
using S_JsonProperty = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace System.Application.Models
{
    /// <summary>
    /// 应用配置项。
    ///
    /// <para><b>裁剪说明</b></para>
    /// <para>
    /// 原实现还持有 <c>AppVersion</c>（已 <c>Obsolete</c>）、<c>AesSecret</c>/<c>Aes</c>、
    /// <c>RSASecret</c>/<c>RSA</c>，并在缺少密钥时抛
    /// <see cref="Security.IsNotOfficialChannelPackageException"/>。
    /// 这三个字段服务于「官方渠道包 + 账号接口的传输加密」。
    /// </para>
    /// <para>
    /// 加速相关接口（<c>api/Accelerate/*</c>、<c>api/Script/*</c>）均为匿名明文接口，
    /// 不需要 AES/RSA；因此这里只保留 <see cref="ApiBaseUrl"/>，
    /// 并把「官方渠道包」判定简化为仅基于程序集公钥的校验。
    /// </para>
    /// <para>
    /// 附带影响：构建产物不再需要 embed <c>aes-key.pfx</c> / <c>rsa-public-key-*.pfx</c>
    /// 也能完整跑通全部保留功能（<c>Startup</c> 会据此退回到开发用 API 地址）。
    /// </para>
    /// </summary>
    [MPObj]
    public sealed class AppSettings : ICloudServiceSettings
    {
        [MPKey(1)]
        [N_JsonProperty("1")]
        [S_JsonProperty("1")]
        public string? ApiBaseUrl { get; set; }

        bool? mGetIsOfficialChannelPackage;

        /// <summary>当前程序集是否具备官方渠道签名。</summary>
        public bool GetIsOfficialChannelPackage()
        {
            if (mGetIsOfficialChannelPackage.HasValue)
            {
                return mGetIsOfficialChannelPackage.Value;
            }

            mGetIsOfficialChannelPackage = GetIsOfficialChannelPackageCore();
            return mGetIsOfficialChannelPackage.Value;

            static bool GetIsOfficialChannelPackageCore()
            {
#if SIGN_ASSEMBLY
                var pk = typeof(AppSettings).Assembly.GetName().GetPublicKey();
                if (pk == null) return false;

                var pkStr = ", PublicKey=" + string.Join(string.Empty, pk.Select(x => x.ToString("x2")));
                return pkStr == ThisAssembly.PublicKey;
#else
                return false;
#endif
            }
        }

        static readonly Lazy<bool> mIsOfficialChannelPackage = new(() =>
        {
            var s = DI.Get_Nullable<IOptions<AppSettings>>()?.Value;
            return s != null && s.GetIsOfficialChannelPackage();
        });

        /// <summary>当前运行程序是否为官方渠道包。</summary>
        public static bool IsOfficialChannelPackage => mIsOfficialChannelPackage.Value;
    }
}
