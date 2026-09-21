using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Application.Services;
using System.Application.Services.Implementation;
using System.Linq;

namespace System.Application
{
    /// <summary>
    /// 锁定「数据保护注册链」的回归测试。
    ///
    /// <para><b>为什么需要这个测试</b></para>
    /// <para>
    /// 精简重构时，<c>Startup2</c> 中的
    /// <c>services.AddSecurityService&lt;EmbeddedAesDataProtectionProvider, LocalDataProtectionProvider&gt;()</c>
    /// 被当作「账号侧功能」删除，但它实际注册的是通用基础设施：
    /// <c>ILocalDataProtectionProvider</c>（DPAPI + 机器密钥）、<c>IProtectedData</c>、
    /// <c>IDataProtectionProvider</c> 兜底实现，以及 <c>ISecurityService</c>。
    /// 缺了它，<c>ISecureStorage</c> / 设置持久化会在<b>运行期</b>解析失败 —— 
    /// 而这类问题<b>编译期完全无法发现</b>（当时确实是 0 错误但必崩）。
    /// </para>
    /// <para>
    /// 因此这里用断言把注册链钉住：后续任何人再裁剪这块，测试会立刻失败。
    /// </para>
    /// </summary>
    [TestFixture]
    public class DataProtectionRegistrationTest
    {
        /// <summary>
        /// <c>AddSecurityService</c> 必须注册出数据保护链上的关键服务。
        /// </summary>
        [Test]
        public void AddSecurityService_应注册完整的数据保护服务链()
        {
            var services = new ServiceCollection();

            services.AddSecurityService<EmbeddedAesDataProtectionProvider, LocalDataProtectionProvider>();

            var registeredTypes = services.Select(x => x.ServiceType).ToHashSet();

            Assert.Multiple(() =>
            {
                // 最关键的一项：键值对存储依赖它
                Assert.That(registeredTypes, Does.Contain(typeof(ILocalDataProtectionProvider)),
                    "缺少 ILocalDataProtectionProvider 注册 —— SecureStorage / 设置持久化将在运行期解析失败");

                Assert.That(registeredTypes, Does.Contain(typeof(ISecurityService)),
                    "缺少 ISecurityService 注册");

                Assert.That(registeredTypes, Does.Contain(typeof(IEmbeddedAesDataProtectionProvider)),
                    "缺少 IEmbeddedAesDataProtectionProvider 注册");
            });
        }

        /// <summary>
        /// 本分支不再内嵌 AES 密钥，提供程序必须返回 <see langword="null"/>，
        /// 且基类要安全降级为透传（与原「非官方渠道包」路径行为一致）。
        /// </summary>
        [Test]
        public void 内嵌AES提供程序_在无密钥时应安全降级为透传()
        {
            var provider = new EmbeddedAesDataProtectionProvider();

            Assert.That(provider.Aes, Is.Null,
                "本分支不内嵌 aes-key.pfx，Aes 必须为 null");

            const string payload = "二级代理密码 abc-123";

            var encrypted = provider.E(payload);
            Assert.That(encrypted, Is.Not.Null);
            Assert.That(provider.D(encrypted), Is.EqualTo(payload),
                "无 AES 时应为 UTF8 透传，往返必须一致");

            var raw = new byte[] { 1, 2, 3 };
            Assert.That(provider.EB(raw), Is.SameAs(raw), "EB 在无 AES 时应原样返回");
            Assert.That(provider.DB(raw), Is.SameAs(raw), "DB 在无 AES 时应原样返回");
        }
    }
}
