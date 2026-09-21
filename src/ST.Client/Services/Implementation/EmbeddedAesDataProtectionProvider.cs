using System.Security.Cryptography;

namespace System.Application.Services.Implementation
{
    /// <summary>
    /// 「内嵌 AES 密钥」数据保护提供程序的客户端实现。
    ///
    /// <para><b>裁剪说明（重要）</b></para>
    /// <para>
    /// 原实现从 <c>AppSettings.Aes</c> 取密钥（来自构建时内嵌的
    /// <c>aes-key.pfx</c>，该文件并不在仓库里），并在缺少密钥时抛
    /// <c>IsNotOfficialChannelPackageException</c>，被本类捕获后返回 <see langword="null"/>。
    /// </para>
    /// <para>
    /// 本次重建只保留了 <see cref="AppSettings.ApiBaseUrl"/>，
    /// 「官方渠道包」判定也不再依赖内嵌密钥（见 <c>AppSettings.GetIsOfficialChannelPackage</c>），
    /// 因此这里直接返回 <see langword="null"/> ——
    /// <b>这与原实现在非官方构建下的行为完全一致</b>：
    /// 基类 <see cref="EmbeddedAesDataProtectionProviderBase"/> 的
    /// <c>E</c>/<c>EB</c>/<c>D</c>/<c>DB</c> 在 <c>Aes == null</c> 时会安全降级为
    /// 透传（或纯 UTF-8 编解码），真正的保护由
    /// <see cref="LocalDataProtectionProvider"/>（DPAPI + 机器密钥）承担。
    /// </para>
    /// <para>
    /// 该类型必须保留：<c>AddSecurityService&lt;TEmbeddedAes, TLocal&gt;</c> 要求一个
    /// <see cref="EmbeddedAesDataProtectionProviderBase"/> 泛型实参，否则整条数据保护注册链
    /// （含 <c>ILocalDataProtectionProvider</c>）都无法装配，键值对存储会在运行期解析失败。
    /// </para>
    /// </summary>
    public class EmbeddedAesDataProtectionProvider : EmbeddedAesDataProtectionProviderBase
    {
        /// <inheritdoc />
        /// <remarks>没有内嵌密钥可用，返回 <see langword="null"/> 表示「不做 AES 混淆层」。</remarks>
        public override Aes[]? Aes => null;
    }
}
