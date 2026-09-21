using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Application.Models;
using System.Application.Services;
using System.Application.Services.Implementation;
using System.Application.Settings;
using System.Application.UI;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using PlatformApplication = System.Application.UI.App;
using _ThisAssembly = System.Properties.ThisAssembly;
using static System.Application.Browser2;

namespace System.Application
{
    /// <summary>
    /// 应用启动与依赖注入装配。
    ///
    /// <para><b>裁剪说明</b></para>
    /// <para>
    /// 只装配加速功能所需的服务集合。相比原始版本移除了以下注册：
    /// 数据保护提供程序（AES/DPAPI）、<c>IUserManager</c>、AutoMapper、
    /// Steam 本地读写 / SteamDb / Steamworks WebApi、ArchiSteamFarm（挂卡）、
    /// 应用更新服务、通知播报服务、移动端权限与电话服务。
    /// </para>
    /// <para>
    /// 同时移除了 <c>__MOBILE__</c> / <c>CONSOLEAPP</c> 条件编译：
    /// 这些目标工程已随裁剪删除，保留条件分支只会增加阅读成本。
    /// </para>
    /// </summary>
    static partial class Startup
    {
        static bool isInitialized;

        /// <summary>初始化启动。</summary>
        public static void Init(DILevel level)
        {
            if (isInitialized) return;
            isInitialized = true;

            var options = new StartupOptions(level);

            if (options.HasServerApiClient)
            {
                ModelValidatorProvider.Init();
            }

            InitDI(options);

            Migrations.Up();
        }

        static void InitDI(StartupOptions options)
        {
            DI.Init(s => ConfigureServices(s, options));
        }

        static void ConfigureServices(IServiceCollection services, StartupOptions options)
        {
            ConfigureRequiredServices(services);
            ConfigureDemandServices(services, options);
        }

        /// <summary>配置任何进程都需要的服务。</summary>
        static void ConfigureRequiredServices(IServiceCollection services)
        {
            // 日志
            services.AddGeneralLogging();

            // app 配置项
            services.TryAddOptions(AppSettings);

            // 键值对存储（SerializableProperty 的持久化基础）
            services.TryAddSecureStorage();

            // 数据保护注册链 —— 必须保留。
            // 它注册了 ILocalDataProtectionProvider（由 LocalDataProtectionProvider 实现，
            // 基于 DPAPI + 机器密钥），以及 IProtectedData / IDataProtectionProvider 的兜底实现
            // 和 ISecurityService。缺了它，SecureStorage / 设置持久化会在运行期解析失败
            // （这类问题编译期无法发现）。
            // 内嵌 AES 提供程序在本分支恒返回 null（无内嵌密钥），基类会安全降级为透传。
            services.AddSecurityService<EmbeddedAesDataProtectionProvider, LocalDataProtectionProvider>();

            services.AddPreferences();
        }

        /// <summary>按需配置业务服务。</summary>
        static void ConfigureDemandServices(IServiceCollection services, StartupOptions options)
        {
            // 平台服务必须排在通用业务实现之前
            services.AddPlatformService(options);

            services.AddDnsAnalysisService();

            if (options.HasGUI)
            {
                services.AddPinyin();
                services.TryAddAvaloniaFontManager(useGdiPlusFirst: true);

#if !DEBUG
                services.AddStartupToastIntercept();
#endif
                services.TryAddToast();

                services.AddSingleton<IApplication>(_ => PlatformApplication.Instance);
                services.AddSingleton<IAvaloniaApplication>(_ => PlatformApplication.Instance);
                services.TryAddSingleton<IClipboardPlatformService>(_ => PlatformApplication.Instance);

                // 主线程助手
                services.AddMainThreadPlatformService();
                services.TryAddFilePickerPlatformService();

                // 窗口与视图模型管理
                services.TryAddWindowManager();
                services.AddViewModelManager();
            }

            if (options.HasHttpClientFactory || options.HasServerApiClient)
            {
                services.TryAddClientHttpPlatformHelperService();
            }

            if (options.HasHttpClientFactory)
            {
                services.AddHttpService();
            }

            // 代理脚本管理
            services.TryAddScriptManager();

            if (options.HasHttpProxy)
            {
                // 加速核心：本地反向代理
                services.AddHttpProxyService();
            }

            if (options.HasServerApiClient)
            {
                services.TryAddModelValidator();

                // 服务端接口（加速项目列表 / 脚本商店 / 版本策略）
                services.TryAddCloudServiceClient<CloudServiceClient>(c =>
                {
                    c.DefaultRequestVersion = HttpVersion.Version30;
                }, configureHandler: () => new SocketsHttpHandler
                {
                    UseCookies = false,
                    AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip,
                });

                // 仓储（仅脚本仓储）
                services.AddRepositories();
            }

            if (options.HasHosts)
            {
                services.AddHostsFileService();
            }
        }

        static AppSettings? mAppSettings;

        public static AppSettings AppSettings
        {
            get
            {
                if (mAppSettings != null) return mAppSettings;

                mAppSettings = new AppSettings();
                SetApiBaseUrl(mAppSettings);
                return mAppSettings;

                static void SetApiBaseUrl(AppSettings s)
                {
                    // ── 加速接口基地址 ───────────────────────────────────────────
                    // 上游原实现按「是否官方渠道包 / 是否 Debug 构建」二选一：
                    //   官方发布版 → https://api.steampp.net
                    //   其余（含本地 Debug）→ https://pan.mossimo.net:8862
                    // 后者是上游作者的内网开发服务器，公网不可达。
                    //
                    // 后果：本地 Debug 构建启动后 API 全部超时，加速项目列表拉不回来，
                    // 运行期表现为「加速项目 0 个，匹配规则 0 条」，也就是加速完全不生效。
                    //（实测日志：ApiConn Fail(1006)，Url：api/Accelerate/All）
                    //
                    // 本项目为自用构建，因此一律使用公网可用的官方匿名接口；
                    // 如需指向自建后端，设置环境变量 STEAMPP_API_BASE_URL 即可，无需重新编译。
                    var fromEnv = Environment.GetEnvironmentVariable(ApiBaseUrlEnvironmentVariable);
                    s.ApiBaseUrl = string.IsNullOrWhiteSpace(fromEnv)
                        ? DefaultApiBaseUrl
                        : fromEnv.Trim().TrimEnd('/');
                }
            }
        }

        /// <summary>默认加速接口基地址（官方匿名接口，无需登录）。</summary>
        public const string DefaultApiBaseUrl = Prefix_HTTPS + "api.steampp.net";

        /// <summary>覆盖加速接口基地址的环境变量名（指向自建后端时使用）。</summary>
        public const string ApiBaseUrlEnvironmentVariable = "STEAMPP_API_BASE_URL";

        /// <summary>主进程启动后的收尾工作。</summary>
        public static void OnStartup(bool isMainProcess)
        {
            // 原始版本在此上报活跃用户并检查更新；两者分别属于账号侧功能与独立模块，
            // 均已移除，因此这里不再需要后台任务。
            _ = isMainProcess;
        }
    }
}
