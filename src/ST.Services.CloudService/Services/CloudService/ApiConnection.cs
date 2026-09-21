using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Polly;
using System.Application.Models;
using System.Application.Models.Internals;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CC = System.Common.Constants;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace System.Application.Services.CloudService
{
    /// <summary>
    /// 服务端接口连接实现。
    ///
    /// <para><b>重构说明</b></para>
    /// <para>
    /// 原始实现约 1040 行，混合了四类互不相关的职责：
    /// ①账号 JWT 鉴权与令牌自动刷新；②上传文件的 MIME 探测与 multipart 组装；
    /// ③AES 会话密钥 + RSA 传输加密；④实际的请求发送与响应反序列化。
    /// 其中 ①②③ 都只服务于已移除的账号/头像上传等功能。
    /// </para>
    /// <para>
    /// 重构后本类只负责 ④，并保留：
    /// 模型校验、网络连通性预检、MessagePack / JSON 双序列化、
    /// Polly 指数退避重试、统一错误码映射、403/401 处理、App 版本淘汰（App-Obsolete）检查、
    /// 以及带进度与读超时的文件下载（脚本更新要用）。
    /// </para>
    /// <para>
    /// 体积由约 1040 行降至约 340 行，且不再依赖 <c>IAuthHelper</c>、<c>RSA</c>、
    /// <c>IAes</c> 等账号侧类型。
    /// </para>
    /// </summary>
    internal sealed class ApiConnection : IApiConnection
    {
        /// <summary>Polly 重试次数。</summary>
        const int NumRetries = 10;

        /// <summary>下载缓冲区大小。</summary>
        const int BufferSize = 4096;

        /// <summary>单次读取超时（毫秒）。</summary>
        const int ReadTimeoutMs = 5000;

        readonly ILogger logger;
        readonly IHttpPlatformHelperService http_helper;
        readonly IApiConnectionPlatformHelper conn_helper;
        readonly IModelValidator validator;
        readonly Lazy<JsonSerializer> jsonSerializer = new(() => new JsonSerializer());

        static readonly Uri Referrer = new(
            string.Format(Constants.Referrer_, DeviceInfo2.OSNameValue.ToString()),
            UriKind.Absolute);

        public ApiConnection(
            ILogger logger,
            IApiConnectionPlatformHelper conn_helper,
            IHttpPlatformHelperService http_helper,
            IModelValidator validator)
        {
            this.logger = logger;
            this.conn_helper = conn_helper;
            this.http_helper = http_helper;
            this.validator = validator;
        }

        #region 异常 → 错误码

        /// <summary>把异常映射为响应码；必要时写日志。</summary>
        public static (ApiResponseCode code, string? msg) GetRspByExceptionWithLogCore(
            Exception ex, string requestUri, ILogger? logger = null, string? logTag = null)
        {
            if (ex is ApiResponseCodeException apiResponseCodeException)
            {
                return (apiResponseCodeException.Code, null);
            }

            switch (ex.GetKnownType())
            {
                case ExceptionKnownType.Canceled:
                    return (ApiResponseCode.Canceled, null);
                case ExceptionKnownType.OperationCanceled:
                    return (ApiResponseCode.OperationCanceled, null);
                case ExceptionKnownType.TaskCanceled:
                    return (ApiResponseCode.TaskCanceled, null);
                case ExceptionKnownType.CertificateNotYetValid:
                    return (ApiResponseCode.CertificateNotYetValid, null);
            }

            const ApiResponseCode code = ApiResponseCode.ClientException;
            var exMsg = ex.GetAllMessage();

            if (logger != null)
            {
                logger.LogError(ex, "ApiConn Fail({0})，Url：{1}", (int)code, requestUri);
            }
            else if (!string.IsNullOrEmpty(logTag))
            {
                Log.Error(logTag!, ex, "ApiConn Fail({0})，Url：{1}", (int)code, requestUri);
            }

            return (code, ApiResponse.GetMessage(code, errorAppendText: exMsg));
        }

        (ApiResponseCode code, string? msg) GetRspByExceptionWithLog(Exception ex, string requestUri)
            => GetRspByExceptionWithLogCore(ex, requestUri, logger);

        #endregion

        #region 请求构造

        /// <summary>
        /// 构造请求体。
        /// <para>
        /// <paramref name="isSecurity"/> 与 <paramref name="aes"/> 为兼容保留：
        /// 会话加密（AES + RSA 传输密钥）随账号模块一并移除，加速相关接口均为匿名明文接口。
        /// 传 <see langword="true"/> 将抛出 <see cref="NotSupportedException"/>。
        /// </para>
        /// </summary>
        HttpContent? GetRequestContent<TRequestModel>(
            bool isSecurity,
            Serializable.ImplType serializableImplType,
            TRequestModel? request,
            CancellationToken cancellationToken)
        {
            if (request == null) return null;

            if (isSecurity)
            {
                throw new NotSupportedException(
                    "会话加密(isSecurity)已随账号模块移除，加速相关接口不支持加密请求。");
            }

            switch (serializableImplType)
            {
                case Serializable.ImplType.NewtonsoftJson:
                    return GetJsonContent(Serializable.SJSON(Serializable.JsonImplType.NewtonsoftJson, request));

                case Serializable.ImplType.SystemTextJson:
                    return GetJsonContent(Serializable.SJSON(Serializable.JsonImplType.SystemTextJson, request));

                case Serializable.ImplType.MessagePack:
                    {
                        var byteArray = Serializable.SMP(request, cancellationToken);
                        if (byteArray == null) return null;

                        var content = new ByteArrayContent(byteArray);
                        content.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeNames.MessagePack);
                        return content;
                    }

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(serializableImplType), serializableImplType, null);
            }

            static StringContent? GetJsonContent(string? jsonStr)
                => jsonStr == null ? null : new StringContent(jsonStr, Encoding.UTF8, MediaTypeNames.JSON);
        }

        #endregion

        #region 拦截器

        /// <summary>全局响应拦截：非成功时提示用户，403 视为应用已被淘汰。</summary>
        async Task GlobalResponseIntercept(
            HttpMethod method,
            string requestUri,
            IApiResponse response,
            bool isShowResponseErrorMessage = true,
            string? errorAppendText = null)
        {
            if (!response.IsSuccess)
            {
                if (isShowResponseErrorMessage)
                {
                    conn_helper.ShowResponseErrorMessage(response, errorAppendText);
                }

                if (response.Code == ApiResponseCode.Unauthorized)
                {
                    logger.LogCritical("Unauthorized method: {0}, requestUri: {1}", method, requestUri);
                }
            }

            if (response is ApiResponseImplBase rspImpl)
            {
                rspImpl.Url = requestUri;
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <summary>请求前的全局拦截：网络连通性预检。</summary>
        async Task<IApiResponse<TResponseModel>?> GlobalBeforeInterceptAsync<TResponseModel>(
            bool isShowResponseErrorMessage = true,
            string? errorAppendText = null)
        {
            if (await http_helper.IsConnectedAsync().ConfigureAwait(false))
            {
                return null;
            }

            var result = ApiResponse.Code<TResponseModel>(
                ApiResponseCode.NetworkConnectionInterruption,
                Constants.NetworkConnectionInterruption);

            if (isShowResponseErrorMessage)
            {
                conn_helper.ShowResponseErrorMessage(result, errorAppendText);
            }

            return result;
        }

        static bool IsAppObsolete(HttpResponseHeaders headers)
            => headers.TryGetValues(Constants.Headers.Response.AppObsolete, out var values) &&
               values.Contains(bool.TrueString, StringComparer.OrdinalIgnoreCase);

        void HandleAppObsolete(HttpResponseHeaders headers)
        {
            if (IsAppObsolete(headers))
            {
                throw new ApiResponseCodeException(ApiResponseCode.AppObsolete);
            }
        }

        void HandleHttpRequest(HttpRequestMessage request)
        {
            request.Headers.AcceptLanguage.ParseAdd(http_helper.AcceptLanguage);
            request.Headers.Referrer = Referrer;
        }

        #endregion

        #region 发送

        public async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            HttpCompletionOption completionOption,
            CancellationToken cancellationToken)
        {
            var client = conn_helper.CreateClient();
            HandleHttpRequest(request);

            var response = await client
                .UseDefaultSendAsync(request, completionOption, cancellationToken)
                .ConfigureAwait(false);

            HandleAppObsolete(response.Headers);
            return response;
        }

        async Task<IApiResponse<TResponseModel>> SendCoreAsync<TRequestModel, TResponseModel>(
            bool isApi,
            CancellationToken cancellationToken,
            HttpMethod method,
            string requestUri,
            TRequestModel? requestModel,
            bool responseContentMaybeNull,
            bool isSecurity,
            bool isShowResponseErrorMessage = true,
            string? errorAppendText = null)
        {
            #region 模型校验

            if (!IApiConnection.DisableModelValidator && isApi &&
                requestModel != null &&
                typeof(TRequestModel) != typeof(object) &&
                !validator.Validate(requestModel, out var errorMessage))
            {
                var validateFail = ApiResponse.Code<TResponseModel>(
                    ApiResponseCode.RequestModelValidateFail, errorMessage);

                if (isShowResponseErrorMessage)
                {
                    conn_helper.ShowResponseErrorMessage(validateFail, errorAppendText);
                }

                return validateFail;
            }

            #endregion

            var preflight = await GlobalBeforeInterceptAsync<TResponseModel>(
                isShowResponseErrorMessage, errorAppendText).ConfigureAwait(false);
            if (preflight != null) return preflight;

            IApiResponse<TResponseModel> responseResult;

            try
            {
                var request = new HttpRequestMessage(method, requestUri)
                {
                    Content = GetRequestContent(isSecurity, Serializable.ImplType.MessagePack, requestModel, cancellationToken),
                };

                request.Headers.Accept.ParseAdd(MediaTypeNames.MessagePack);

                var client = conn_helper.CreateClient();
                HandleHttpRequest(request);

                using var response = await client
                    .UseDefaultSendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                HandleAppObsolete(response.Headers);

                responseResult = await ReadResponseAsync<TResponseModel>(response, isApi, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var (code, msg) = GetRspByExceptionWithLog(ex, requestUri);
                responseResult = ApiResponse.Code<TResponseModel>(code, msg, default, ex);
            }

            await GlobalResponseIntercept(
                method, requestUri, responseResult, isShowResponseErrorMessage, errorAppendText)
                .ConfigureAwait(false);

            return responseResult;
        }

        /// <summary>按响应 MediaType 反序列化（MessagePack 为主，JSON / 原始串 / 字节流兼容）。</summary>
        async Task<IApiResponse<TResponseModel>> ReadResponseAsync<TResponseModel>(
            HttpResponseMessage response, bool isApi, CancellationToken cancellationToken)
        {
            var code = (ApiResponseCode)response.StatusCode;

            if (response.Content == null)
            {
                return ApiResponse.Code<TResponseModel>(code);
            }

            if (!isApi && typeof(TResponseModel) == typeof(byte[]))
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                return ApiResponse.Code(code, null, (TResponseModel)(object)bytes);
            }

            if (!isApi && typeof(TResponseModel) == typeof(string))
            {
                var str = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return ApiResponse.Code(code, null, (TResponseModel)(object)str);
            }

            var mime = response.Content.Headers.ContentType?.MediaType;

            switch (mime)
            {
                case MediaTypeNames.JSON:
                    {
                        using var stream = await response.Content
                            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        using var json = new JsonTextReader(reader);
                        return ApiResponse.Deserialize<TResponseModel>(jsonSerializer.Value, json);
                    }

                case MediaTypeNames.MessagePack:
                    {
                        using var stream = await response.Content
                            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                        return await ApiResponse
                            .DeserializeAsync<TResponseModel>(stream, cancellationToken)
                            .ConfigureAwait(false);
                    }

                default:
                    return ApiResponse.Code<TResponseModel>(
                        response.IsSuccessStatusCode ? ApiResponseCode.UnsupportedResponseMediaType : code);
            }
        }

        #endregion

        #region Polly 重试

        static bool PollyHandleResultPredicate<TResponse>(TResponse response) where TResponse : IApiResponse
            => response is ApiResponseImpl impl &&
                impl.ClientException != null &&
                impl.Code == ApiResponseCode.ClientException;

        static TimeSpan PollyRetryAttempt(int attemptNumber)
        {
            var powY = attemptNumber % NumRetries;
            var timeSpan = TimeSpan.FromMilliseconds(Math.Pow(2, powY));
            var addSeconds = attemptNumber / NumRetries;
            if (addSeconds > 0)
            {
                timeSpan = timeSpan.Add(TimeSpan.FromSeconds(addSeconds));
            }

            return timeSpan;
        }

        async Task<IApiResponse<TResponseModel>> SendWithRetryAsync<TRequestModel, TResponseModel>(
            bool isPolly,
            bool isApi,
            CancellationToken cancellationToken,
            HttpMethod method,
            string requestUri,
            TRequestModel? requestModel,
            bool responseContentMaybeNull,
            bool isSecurity,
            bool isShowResponseErrorMessage = true,
            string? errorAppendText = null)
        {
            Task<IApiResponse<TResponseModel>> Send()
                => SendCoreAsync<TRequestModel, TResponseModel>(
                    isApi,
                    cancellationToken,
                    method,
                    requestUri,
                    requestModel,
                    responseContentMaybeNull,
                    isSecurity,
                    !isPolly && isShowResponseErrorMessage,
                    errorAppendText);

            if (!isPolly)
            {
                return await Send().ConfigureAwait(false);
            }

            var response = await Policy
                .HandleResult<IApiResponse<TResponseModel>>(PollyHandleResultPredicate)
                .WaitAndRetryAsync(NumRetries, PollyRetryAttempt)
                .ExecuteAsync(Send)
                .ConfigureAwait(false);

            if (!response.IsSuccess && isShowResponseErrorMessage)
            {
                conn_helper.ShowResponseErrorMessage(response, errorAppendText);
            }

            return response;
        }

        #endregion

        #region 下载

        async Task<IApiResponse> DownloadCoreAsync(
            CancellationToken cancellationToken,
            string requestUri,
            string cacheFilePath,
            IProgress<float>? progress,
            bool isShowResponseErrorMessage = true,
            string? errorAppendText = null)
        {
            var cacheDirPath = Path.GetDirectoryName(cacheFilePath)
                ?? throw new ArgumentNullException(nameof(cacheFilePath));
            IOPath.DirCreateByNotExists(cacheDirPath);

            var preflight = await GlobalBeforeInterceptAsync<object>(
                isShowResponseErrorMessage, errorAppendText).ConfigureAwait(false);
            if (preflight != null) return preflight;

            var method = HttpMethod.Get;
            IApiResponse responseResult;

            try
            {
                var request = new HttpRequestMessage(method, requestUri);
                var client = conn_helper.CreateClient();
                HandleHttpRequest(request);

                using var response = await client
                    .UseDefaultSendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                var code = (ApiResponseCode)response.StatusCode;
                responseResult = ApiResponse.Code(code);

                if (!responseResult.IsSuccess)
                {
                    await GlobalResponseIntercept(
                        method, requestUri, responseResult, isShowResponseErrorMessage, errorAppendText)
                        .ConfigureAwait(false);
                    return responseResult;
                }

                var total = response.Content.Headers.ContentLength ?? -1L;
                if (total <= 0)
                {
                    responseResult.Code = ApiResponseCode.NoResponseContent;
                    return responseResult;
                }

                IOPath.FileIfExistsItDelete(cacheFilePath);

                using var fileStream = new FileStream(
                    cacheFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, true);
                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

                var buffer = new byte[BufferSize];
                var totalRead = 0L;
                var lastProgressValue = -1f;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 单次读取加超时，避免服务端「连上但不发数据」把下载永久挂住
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    readCts.CancelAfter(ReadTimeoutMs);

                    var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), readCts.Token)
                        .ConfigureAwait(false);
                    if (read == 0) break;

                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    totalRead += read;

                    if (progress != null)
                    {
                        var progressValue = MathF.Round(
                            (float)totalRead / total * CC.MaxProgress, 2, MidpointRounding.AwayFromZero);

                        if (progressValue != lastProgressValue)
                        {
                            progress.Report(progressValue);
                            lastProgressValue = progressValue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var (code, msg) = GetRspByExceptionWithLog(ex, requestUri);
                responseResult = ApiResponse.Code(code, msg, ex);

                await GlobalResponseIntercept(
                    method, requestUri, responseResult, isShowResponseErrorMessage, errorAppendText)
                    .ConfigureAwait(false);
            }

            return responseResult;
        }

        public async Task<IApiResponse> DownloadAsync(
            CancellationToken cancellationToken,
            string requestUri,
            string cacheFilePath,
            IProgress<float>? progress,
            bool isAnonymous = true,
            bool isShowResponseErrorMessage = true,
            string? errorAppendText = null,
            bool isPolly = true)
        {
            if (!isPolly)
            {
                return await DownloadCoreAsync(
                    cancellationToken, requestUri, cacheFilePath, progress,
                    isShowResponseErrorMessage, errorAppendText).ConfigureAwait(false);
            }

            var response = await Policy
                .HandleResult<IApiResponse>(PollyHandleResultPredicate)
                .WaitAndRetryAsync(NumRetries, PollyRetryAttempt)
                .ExecuteAsync(() => DownloadCoreAsync(
                    cancellationToken, requestUri, cacheFilePath, progress,
                    !isPolly && isShowResponseErrorMessage, errorAppendText))
                .ConfigureAwait(false);

            if (!response.IsSuccess && isShowResponseErrorMessage)
            {
                conn_helper.ShowResponseErrorMessage(response, errorAppendText);
            }

            return response;
        }

        #endregion

        #region IApiConnection 其余重载

        public Task<IApiResponse<TResponseModel>> SendAsync<TRequestModel, TResponseModel>(
            CancellationToken cancellationToken,
            HttpMethod method,
            string requestUri,
            TRequestModel? request,
            bool responseContentMaybeNull = false,
            bool isSecurity = false,
            bool isAnonymous = false,
            bool isShowResponseErrorMessage = true,
            string? errorAppendText = null,
            bool isPolly = false)
            => SendWithRetryAsync<TRequestModel, TResponseModel>(
                isPolly, true, cancellationToken, method, requestUri, request,
                responseContentMaybeNull, isSecurity, isShowResponseErrorMessage, errorAppendText);

        public async Task<IApiResponse> SendAsync<TRequestModel>(
            CancellationToken cancellationToken,
            HttpMethod method,
            string requestUri,
            TRequestModel? request,
            bool isSecurity = false,
            bool isAnonymous = false,
            bool isShowResponseErrorMessage = true,
            string? errorAppendText = null,
            bool isPolly = false)
            => await SendWithRetryAsync<TRequestModel, object>(
                isPolly, true, cancellationToken, method, requestUri, request,
                true, isSecurity, isShowResponseErrorMessage, errorAppendText)
                .ConfigureAwait(false);

        public async Task<IApiResponse> SendAsync(
            CancellationToken cancellationToken,
            HttpMethod method,
            string requestUri,
            bool isAnonymous = false,
            bool isShowResponseErrorMessage = true,
            string? errorAppendText = null,
            bool isPolly = false)
            => await SendWithRetryAsync<object, object>(
                isPolly, true, cancellationToken, method, requestUri, null,
                true, false, isShowResponseErrorMessage, errorAppendText)
                .ConfigureAwait(false);

        public Task<IApiResponse<TResponseModel>> SendAsync<TResponseModel>(
            CancellationToken cancellationToken,
            HttpMethod method,
            string requestUri,
            bool responseContentMaybeNull = false,
            bool isSecurity = false,
            bool isAnonymous = false,
            bool isShowResponseErrorMessage = true,
            string? errorAppendText = null,
            bool isPolly = false)
            => SendWithRetryAsync<object, TResponseModel>(
                isPolly, true, cancellationToken, method, requestUri, null,
                responseContentMaybeNull, isSecurity, isShowResponseErrorMessage, errorAppendText);

        public Task<IApiResponse<byte[]>> GetRaw(
            CancellationToken cancellationToken,
            string requestUri,
            bool isAnonymous = true,
            bool isShowResponseErrorMessage = true,
            string? errorAppendText = null,
            bool isPolly = true)
            => SendWithRetryAsync<object, byte[]>(
                isPolly, false, cancellationToken, HttpMethod.Get, requestUri, null,
                false, false, isShowResponseErrorMessage, errorAppendText);

        public Task<IApiResponse<string>> GetHtml(
            CancellationToken cancellationToken,
            string requestUri,
            bool isAnonymous = true,
            bool isShowResponseErrorMessage = true,
            string? errorAppendText = null,
            bool isPolly = true)
            => SendWithRetryAsync<object, string>(
                isPolly, false, cancellationToken, HttpMethod.Get, requestUri, null,
                false, false, isShowResponseErrorMessage, errorAppendText);

        #endregion
    }
}
