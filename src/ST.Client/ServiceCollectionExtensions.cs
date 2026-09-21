using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Application.Entities;
using System.Application.Repositories;
using System.Application.Repositories.Implementation;
using System.Application.Services;
using System.Application.Services.Implementation;
using System.Net.Http;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    public static partial class ServiceCollectionExtensions
    {
        /// <summary>
        /// 注册仓储。
        /// <para>
        /// 裁剪后仅保留脚本仓储（<c>IScriptRepository</c>）——
        /// 用户仓储与令牌仓储随对应功能模块移除。
        /// </para>
        /// </summary>
        public static IServiceCollection AddRepositories(this IServiceCollection services)
        {
            services.AddSingleton<IScriptRepository, ScriptRepository>();
            return services;
        }

        /// <summary>添加通用 Http 服务。</summary>
        public static IServiceCollection AddHttpService(this IServiceCollection services)
        {
            services.AddHttpClient(); // System.Net.Http.HttpClient / HttpClientFactory
            services.AddSingleton<IHttpService, HttpServiceImpl>();
            return services;
        }

        /// <summary>添加 JS 脚本管理服务。</summary>
        public static IServiceCollection TryAddScriptManager(this IServiceCollection services)
        {
            services.TryAddSingleton<IScriptManager, ScriptManager>();
            return services;
        }

        /// <summary>添加 HttpProxy 代理服务（加速核心）。</summary>
        public static IServiceCollection AddHttpProxyService(this IServiceCollection services)
        {
            services.AddSingleton<IHttpProxyService, HttpProxyServiceImpl>();
            return services;
        }

        /// <summary>添加启动期 Toast 拦截（在 UI 就绪前缓存提示）。</summary>
        public static IServiceCollection AddStartupToastIntercept(this IServiceCollection services)
        {
            services.AddSingleton<StartupToastIntercept>();
            services.AddSingleton<IToastIntercept>(s => s.GetRequiredService<StartupToastIntercept>());
            return services;
        }

        /// <summary>尝试添加使用 <see cref="ToastService"/> 实现的 <see cref="IToast"/>。</summary>
        public static IServiceCollection TryAddToast(this IServiceCollection services)
            => ToastImpl.TryAddToast(services);

        /// <summary>添加 hosts 文件助手服务（非系统代理模式下的加速依赖它）。</summary>
        public static IServiceCollection AddHostsFileService(this IServiceCollection services)
        {
            services.AddSingleton<IHostsFileService, HostsFileServiceImpl>();
            return services;
        }

        /// <summary>添加适用于客户端的 <see cref="IHttpPlatformHelperService"/>。</summary>
        public static IServiceCollection TryAddClientHttpPlatformHelperService(this IServiceCollection services)
        {
            services.TryAddSingleton<IHttpPlatformHelperService, ClientHttpPlatformHelperServiceImpl>();
            return services;
        }

        /// <summary>添加 <see cref="IViewModelManager"/>。</summary>
        public static IServiceCollection AddViewModelManager(this IServiceCollection services)
        {
            services.AddSingleton<IViewModelManager, ViewModelManager>();
            return services;
        }
    }
}
