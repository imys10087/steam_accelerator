using System.IO;
using System.Properties;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Application.UI.Resx;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.Network;

namespace System.Application.Services.Accelerator
{
    /// <summary>
    /// 本地根证书的创建、信任、删除与检测。
    ///
    /// <para>
    /// 从原来的 <c>HttpProxyServiceImpl</c> 中抽出。原实现把证书逻辑与代理生命周期、
    /// 请求拦截混在一个 800+ 行的类里，且 <c>SetupCertificate</c> 在非 DEBUG 构建下用
    /// <c>catch { }</c> 静默吞掉所有异常，导致「HTTPS 加速不生效但没有日志也没有提示」，
    /// 是最难排查的一类线上问题。这里统一改为记录日志 + 返回明确结果。
    /// </para>
    /// </summary>
    public sealed class ProxyCertificateManager
    {
        const string TAG = "ProxyCert";

        readonly ProxyServer proxyServer;
        readonly IPlatformService platformService;

        public ProxyCertificateManager(ProxyServer proxyServer, IPlatformService platformService)
        {
            this.proxyServer = proxyServer;
            this.platformService = platformService;
        }

        CertificateManager Manager => proxyServer.CertificateManager;

        /// <summary>根证书私钥文件路径（.pfx）。</summary>
        public static string PfxFilePath => Path.Combine(IOPath.AppDataDirectory, IHttpProxyService.PfxFileName);

        /// <summary>导出的根证书公钥文件路径（.cer）。</summary>
        public static string CerFilePath => Path.Combine(IOPath.AppDataDirectory, IHttpProxyService.CerFileName);

        /// <summary>当前是否已加载根证书。</summary>
        public bool HasRootCertificate => Manager.RootCertificate != null;

        /// <summary>
        /// 确保本机存在可用的、被信任的根证书。返回 <see langword="false"/> 表示无法建立
        /// HTTPS 解密能力，调用方应中止启动代理而不是带着半可用状态继续。
        /// </summary>
        public bool EnsureTrustedRootCertificate()
        {
            if (IsCertificateInstalled(Manager.RootCertificate))
            {
                return true;
            }

            DeleteCertificate();
            return SetupCertificate();
        }

        /// <summary>创建根证书并写入本机信任存储。</summary>
        public bool SetupCertificate()
        {
            if (!Manager.CreateRootCertificate(true) || Manager.RootCertificate == null)
            {
                Log.Error(TAG, AppResources.CreateCertificateFaild);
                Toast.Show(AppResources.CreateCertificateFaild);
                return false;
            }

            try
            {
                Manager.RootCertificate.SaveCerCertificateFile(CerFilePath);
            }
            catch (Exception e)
            {
                // 导出 .cer 失败不致命：证书已进内存，仍可工作
                Log.Error(TAG, e, "SaveCerCertificateFile");
            }

            try
            {
                Manager.TrustRootCertificate();
            }
            catch (Exception e)
            {
                // 修复点：原实现在 Release 下静默吞异常
                Log.Error(TAG, e, "TrustRootCertificate");
            }

            try
            {
                Manager.EnsureRootCertificate();
            }
            catch (Exception e)
            {
                Log.Error(TAG, e, "EnsureRootCertificate");
            }

            if (OperatingSystem2.IsMacOS)
            {
                TrustCer();
            }

            if (OperatingSystem2.IsLinux && !OperatingSystem2.IsAndroid)
            {
                // Linux 下无统一信任存储写入方式，引导用户按官方指引手动导入
                Browser2.Open(UrlConstants.OfficialWebsite_LiunxSetupCer);
                return true;
            }

            return IsCertificateInstalled(Manager.RootCertificate);
        }

        /// <summary>在 macOS 上把根证书加入系统信任链（需要管理员权限）。</summary>
        public void TrustCer()
        {
            platformService.RunShell(
                $"security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain \"{CerFilePath}\"",
                true);
        }

        /// <summary>删除本机信任存储中的根证书及其文件。</summary>
        public bool DeleteCertificate()
        {
            if (proxyServer.ProxyRunning)
            {
                return false;
            }

            if (Manager.RootCertificate == null)
            {
                return true;
            }

            try
            {
                Manager.RemoveTrustedRootCertificate();

                if (!IsCertificateInstalled(Manager.RootCertificate))
                {
                    Manager.RootCertificate = null;

                    if (File.Exists(Manager.PfxFilePath))
                    {
                        File.Delete(Manager.PfxFilePath);
                    }
                }
            }
            catch (CryptographicException)
            {
                // 用户取消了证书删除操作，保持现状
            }
            catch (Exception e)
            {
                Log.Error(TAG, e, nameof(DeleteCertificate));
                throw;
            }

            return true;
        }

        /// <summary>判断给定证书是否已存在于当前用户的信任存储中且未过期。</summary>
        public static bool IsCertificateInstalled(X509Certificate2? certificate2)
        {
            if (certificate2 == null) return false;
            if (certificate2.NotAfter <= DateTime.Now) return false;

            if (OperatingSystem2.IsLinux)
            {
                // Linux 无统一的用户级信任存储 API，由 SetupCertificate 单独引导处理
                return true;
            }

            try
            {
                using var store = new X509Store(
                    OperatingSystem2.IsMacOS ? StoreName.My : StoreName.Root,
                    StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);
                return store.Certificates.Contains(certificate2);
            }
            catch (Exception e)
            {
                Log.Error(TAG, e, nameof(IsCertificateInstalled));
                return false;
            }
        }
    }
}
