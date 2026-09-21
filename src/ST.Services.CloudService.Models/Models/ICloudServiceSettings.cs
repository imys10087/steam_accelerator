namespace System.Application.Models
{
    /// <summary>
    /// 服务端接口配置。
    ///
    /// <para><b>裁剪说明</b></para>
    /// <para>
    /// 原接口除 <see cref="ApiBaseUrl"/> 外还暴露了 <c>RSA</c>（会话密钥传输加密）、
    /// <c>AppVersion</c>（已标记 <c>Obsolete("Delete", true)</c>）、
    /// <c>AppVersionStr</c>（已标记 <c>Obsolete("Delete", true)</c>）。
    /// 这些成员只服务于账号体系与已下线的能力，加速链路均不依赖，故一并移除。
    /// </para>
    /// </summary>
    public interface ICloudServiceSettings
    {
        /// <summary>服务端接口根地址，例如 <c>https://api.steampp.net</c>。</summary>
        string? ApiBaseUrl { get; set; }
    }
}
