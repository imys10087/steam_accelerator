using Microsoft.Extensions.Logging;
using System.Application.UI;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    public static partial class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加通用日志实现。
        ///
        /// <para>
        /// 本文件原先位于 <c>ST.Client.Desktop</c>。该工程经核查<b>没有任何其他工程引用</b>，
        /// 且内部包含与 <c>ST.Client</c> 重名的 <c>ProxyService</c>、
        /// <c>ProxyScriptManagePageViewModel</c>，并引用了早已不存在的
        /// <c>ProxySettings.HostProxyPortId</c> / <c>IHttpProxyService.IsWindowsProxy</c>，
        /// 属于编译不通过的遗留死代码，已在本次裁剪中整体删除。
        /// 其中唯一仍被使用的成员（本方法）迁移至 <c>ST.Client</c>。
        /// </para>
        /// </summary>
        public static IServiceCollection AddGeneralLogging(this IServiceCollection services)
        {
            var (minLevel, action) = IApplication.ConfigureLogging();

            services.AddLogging(b =>
            {
                action(b);
#if MONO_MAC
                b.AddProvider(PlatformLoggerProvider.Instance);
#elif XAMARIN_MAC
                b.AddProvider(global::Uno.Extensions.Logging.OSLogLoggerProvider.Instance);
#endif
            });

            services.Configure<LoggerFilterOptions>(o =>
            {
                o.MinLevel = minLevel;
            });

            return services;
        }
    }
}
