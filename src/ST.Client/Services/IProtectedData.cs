namespace System.Security.Cryptography
{
    /// <summary>
    /// 受保护数据（原样加密/解密）的抽象，由平台提供实现
    /// （例如 Windows 上为 DPAPI 的 <c>WindowsProtectedData</c>）。
    ///
    /// <para><b>位置说明（本次重构调整）</b></para>
    /// <para>
    /// 该接口原本声明在已移除的账号令牌兼容层文件中
    /// （<c>ST.Services.CloudService/.../GAPAuthenticatorValueDTO.Compat_WinAuth3_ProtectedData.cs</c>），
    /// 但它的使用者其实是通用基础设施：<c>WindowsProtectedData</c>、
    /// <c>ILocalDataProtectionProvider.IProtectedData</c> 的注册，以及键值对存储。
    /// 令牌模块删除后接口会随之消失，故把它迁到本文件独立保存，
    /// 命名空间保持 <c>System.Security.Cryptography</c> 不变，避免影响实现方。
    /// </para>
    /// </summary>
    public interface IProtectedData
    {
        /// <summary>从 DI 取得当前平台的实现；不存在时抛出 <see cref="PlatformNotSupportedException"/>。</summary>
        public static IProtectedData Instance
        {
            get
            {
                var value = DI.Get_Nullable<IProtectedData>();
                return value ?? throw new PlatformNotSupportedException();
            }
        }

        /// <summary>保护范围。</summary>
        public enum DataProtectionScope
        {
            /// <summary>仅当前用户可解密。</summary>
            CurrentUser = 0,

            /// <summary>本机任意用户均可解密。</summary>
            LocalMachine = 1,
        }

        /// <summary>加密数据。<paramref name="optionalEntropy"/> 为附加熵（可为 <see langword="null"/>）。</summary>
        byte[] Protect(byte[] userData, byte[]? optionalEntropy, DataProtectionScope scope);

        /// <summary>解密数据。</summary>
        byte[] Unprotect(byte[] encryptedData, byte[]? optionalEntropy, DataProtectionScope scope);
    }
}
