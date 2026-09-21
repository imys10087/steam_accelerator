using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Application.Services;
using System.Application.Services.CloudService;
using System.Net.Http;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 注册服务端接口客户端。
        ///
        /// <para>
        /// 相比原实现移除了 <c>useMock</c> 分支（它只在 <c>UI_DEMO</c> 构建下挂载
        /// <c>MockCloudServiceClient</c>，而该类型已随演示工程一并删除），
        /// 同时不再注册已移除的 <c>IAuthHelper</c> 相关服务。
        /// </para>
        /// </summary>
        public static IServiceCollection TryAddCloudServiceClient<T>(
            this IServiceCollection services,
            Action<HttpClient>? config = null,
            Func<HttpMessageHandler>? configureHandler = null)
            where T : CloudServiceClientBase
        {
            var builder = services.AddHttpClient(CloudServiceClientBase.ClientName_, (s, c) =>
            {
                var sc = s.GetRequiredService<CloudServiceClientBase>();
                c.Timeout = GeneralHttpClientFactory.DefaultTimeout;
                c.BaseAddress = new Uri(sc.ApiBaseUrl, UriKind.Absolute);
                c.DefaultRequestHeaders.UserAgent.ParseAdd(sc.UserAgent);
                config?.Invoke(c);
            });

            if (configureHandler != null)
            {
                builder.ConfigurePrimaryHttpMessageHandler(configureHandler);
            }

            services.TryAddSingleton<T>();
            services.TryAddSingleton<CloudServiceClientBase>(s => s.GetRequiredService<T>());
            services.TryAddSingleton<IApiConnectionPlatformHelper>(s => s.GetRequiredService<T>());
            services.TryAddSingleton<ICloudServiceClient>(s => s.GetRequiredService<T>());

            return services;
        }
    }
}
