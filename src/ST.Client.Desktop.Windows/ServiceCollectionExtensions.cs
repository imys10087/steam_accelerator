using System;
using System.Application;
using System.Application.Services;
using System.Application.Services.Implementation;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPlatformService(this IServiceCollection services, StartupOptions options)
        {
#pragma warning disable CA1416 // 验证平台兼容性
            if (OperatingSystem2.IsWindows)
            {
                services.AddSingleton<IHttpPlatformHelperService, WindowsClientHttpPlatformHelperServiceImpl>();
                services.AddSingleton<WindowsPlatformServiceImpl>();
                services.AddSingleton<IPlatformService>(s => s.GetRequiredService<WindowsPlatformServiceImpl>());

                // 已移除的注册（随对应功能模块裁剪）：
                //   options.HasSteam          → ISteamworksLocalApiService / SteamworksLocalApiServiceImpl
                //   options.HasGUI            → IBiometricService / PlatformBiometricServiceImpl（生物识别）
                //   INativeWindowApiService   → NativeWindowApiServiceImpl（窗口工具）
                //   IJumpListService          → Windows10JumpListServiceImpl / JumpListServiceImpl
                //   ISevenZipHelper           → SevenZipHelper（应用更新解压）
                //
                // 保留 WindowsProtectedData：它是通用的 DPAPI 数据保护实现
                // （ILocalDataProtectionProvider.IProtectedData / IProtectedData），
                // 供键值对存储等基础设施使用，与账号功能无关。

                services.AddSingleton<WindowsProtectedData>();
                services.AddSingleton<IProtectedData>(s => s.GetRequiredService<WindowsProtectedData>());
                services.AddSingleton<ILocalDataProtectionProvider.IProtectedData>(s => s.GetRequiredService<WindowsProtectedData>());
                if (OperatingSystem2.IsWindows10AtLeast)
                {
                    services.AddSingleton<IEmailPlatformService>(s => s.GetRequiredService<WindowsPlatformServiceImpl>());
                    services.AddSingleton<ILocalDataProtectionProvider.IDataProtectionProvider, Windows10DataProtectionProvider>();
                }
                if (options.HasMainProcessRequired)
                {
                    services.AddSingleton(typeof(NotifyIcon), NotifyIcon.ImplType);
                }
            }
            else
            {
                throw new PlatformNotSupportedException();
            }
            // 已移除 services.AddMSAppCenterApplicationSettings()：
            // 该扩展由已删除的 VisualStudioAppCenterSDK.cs（App Center 遥测接入）提供，
            // 属于账号侧的用户行为上报，与本工具功能无关。
            return services;
#pragma warning restore CA1416 // 验证平台兼容性
        }
    }
}