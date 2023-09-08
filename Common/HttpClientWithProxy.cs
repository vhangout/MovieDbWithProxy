using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Emby.Model.Sanitation;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;

namespace MovieDbWithProxy.Commons
{
    internal class HttpClientWithProxy : IHttpClient
    {
        private const int TimeoutSeconds = 30;
        private readonly
#nullable disable
        ILogger _logger;
        private readonly IApplicationPaths _appPaths;
        private readonly IFileSystem _fileSystem;
        private string _defaultUserAgent => "Emby";
        private DateTimeOffset _lastTimeout;

        private readonly WebProxy _proxy;
        private readonly HttpClientHandler _handler;
        private readonly HttpClient _httpClient;

        private static HttpClientWithProxy instance;

        private HttpClientWithProxy(ILogger logger)
        {
            _logger = logger;
            _lastTimeout = DateTimeOffset.UtcNow;
            _proxy = new WebProxy
            {
                Address = new Uri($"socks5://localhost:2080"),
                BypassProxyOnLocal = false,
                UseDefaultCredentials = false
            };

            _handler = new HttpClientHandler
            {
                Proxy = _proxy
            };

            _httpClient = new HttpClient(_handler);
        }

        public static HttpClientWithProxy getInstance()
        {
            if (instance == null)
                instance = new HttpClientWithProxy(null);
            return instance;
        }

        public async Task<HttpResponseInfo> SendAsync(MediaBrowser.Common.Net.HttpRequestOptions options, string httpMethod)
        {
            ValidateParams(options);
            CancellationToken cancellationToken = options.CancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            HttpRequestMessage request = GetRequest(options, httpMethod);
            bool flag1 = !options.RequestContentBytes.IsEmpty;
            if (flag1 || !options.RequestContent.IsEmpty || options.RequestHttpContent != null || string.Equals(httpMethod, "post", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    bool flag2 = true;
                    if (flag1)
                        request.Content = new ReadOnlyMemoryContent(options.RequestContentBytes);
                    else if (!options.RequestContent.IsEmpty)
                    {
                        ReadOnlyMemory<char> requestContent = options.RequestContent;
                        byte[] array = new byte[Encoding.UTF8.GetMaxByteCount(requestContent.Length)];
                        int bytes = Encoding.UTF8.GetBytes(requestContent.Span, new Span<byte>(array));
                        request.Content = new ReadOnlyMemoryContent(new ReadOnlyMemory<byte>(array, 0, bytes));
                    }
                    else if (options.RequestHttpContent != null)
                    {
                        flag2 = false;
                        request.Content = options.RequestHttpContent;
                    }
                    else
                        request.Content = new ReadOnlyMemoryContent(new ReadOnlyMemory<byte>(Array.Empty<byte>()));
                    if (flag2)
                    {
                        MediaTypeHeaderValue mediaTypeHeaderValue = new MediaTypeHeaderValue(options.RequestContentType ?? "application/x-www-form-urlencoded");
                        if (options.AppendCharsetToMimeType)
                            mediaTypeHeaderValue.CharSet = "utf-8";
                        request.Content.Headers.ContentType = mediaTypeHeaderValue;
                    }
                }
                catch (Exception ex)
                {
                    throw new HttpException(ex.Message)
                    {
                        IsTimedOut = true
                    };
                }
            }
            
            Log(LogSeverity.Info, "{0} {1}", httpMethod, GetLogUrl(options));

            DateTimeOffset now = DateTimeOffset.UtcNow;
            try
            {
                cancellationToken = options.CancellationToken;
                cancellationToken.ThrowIfCancellationRequested();
                HttpCompletionOption completionOption = options.BufferContent ? HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead;
                HttpResponseMessage response = await _httpClient.SendAsync(request, completionOption, options.CancellationToken).ConfigureAwait(false);
                HttpResponseMessage httpResponse;
                if (response.StatusCode >= HttpStatusCode.Ambiguous && response.StatusCode <= (HttpStatusCode)399)
                {
                    Uri location = response.Headers.Location;
                    if (location != null)
                    {
                        options.Url = location.ToString();
                        httpResponse = response;
                        try
                        {
                            return await SendAsync(options, httpMethod).ConfigureAwait(false);
                        }
                        finally
                        {
                            httpResponse?.Dispose();
                        }
                    }
                }
                //await EnsureSuccessStatusCode(client, response, options, now, true).ConfigureAwait(false);
                cancellationToken = options.CancellationToken;
                cancellationToken.ThrowIfCancellationRequested();
                IDisposable[] disposableArray;
                if (!options.SingleTcpConnection)
                    disposableArray = new HttpResponseMessage[1]
                    {
                        response
                    };
                else
                    disposableArray = new IDisposable[2]
                    {
                        response,
                        _httpClient
                    };
                IDisposable[] disposables = disposableArray;
                httpResponse = response;
                string url = options.Url;
                Stream content = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                return GetResponseInfo(httpResponse, url, content, GetContentLength(response), disposables);
            }
            catch (OperationCanceledException ex)
            {
                throw GetCancellationException(options, options.CancellationToken, ex);
            }
            catch (Exception ex)
            {
                throw GetException(ex, options);
            }
        }

        private long? GetContentLength(HttpResponseMessage response)
        {
            long? contentLength = response.Content.Headers.ContentLength;
            int num = contentLength.HasValue ? 1 : 0;
            return contentLength;
        }

        private HttpResponseInfo GetResponseInfo(
      HttpResponseMessage httpResponse,
      string requestedUrl,
      Stream content,
      long? contentLength,
      IDisposable[] disposables)
        {
            HttpResponseInfo responseInfo = new HttpResponseInfo(disposables)
            {
                Content = content,
                StatusCode = httpResponse.StatusCode,
                ContentLength = contentLength,
                ResponseUrl = httpResponse.RequestMessage.RequestUri?.ToString() ?? requestedUrl
            };
            SetHeaders(httpResponse, responseInfo);
            string mediaType = httpResponse.Content.Headers?.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(mediaType))
                responseInfo.Headers.TryGetValue("Content-Type", out mediaType);
            responseInfo.ContentType = mediaType;
            return responseInfo;
        }

        private void SetHeaders(HttpResponseMessage message, HttpResponseInfo responseInfo)
        {
            HttpResponseHeaders headers1 = message.Headers;
            if (headers1 != null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> keyValuePair in (HttpHeaders)headers1)
                    responseInfo.Headers[keyValuePair.Key] = string.Join(" ", keyValuePair.Value.ToArray<string>());
            }
            HttpContentHeaders headers2 = message.Content?.Headers;
            if (headers2 == null)
                return;
            foreach (KeyValuePair<string, IEnumerable<string>> keyValuePair in (HttpHeaders)headers2)
                responseInfo.Headers[keyValuePair.Key] = string.Join(" ", keyValuePair.Value.ToArray<string>());
        }

        private void ValidateParams(MediaBrowser.Common.Net.HttpRequestOptions options)
        {
            if (string.IsNullOrEmpty(options.Url))
                throw new ArgumentNullException(nameof(options));
        }

        private HttpRequestMessage GetRequest(MediaBrowser.Common.Net.HttpRequestOptions options, string method)
        {
            string str = options.Url;
            string userInfo = new Uri(str).UserInfo;
            if (!string.IsNullOrWhiteSpace(userInfo))
            {
                Log(LogSeverity.Info, "Found userInfo in url: {0} ... url: {1}", userInfo, GetLogUrl(options));
                str = str.Replace(userInfo + "@", string.Empty);
            }
            HttpRequestMessage request = new HttpRequestMessage(new HttpMethod(method), str);
            AddRequestHeaders(request, options);
            return request;
        }

        private void AddRequestHeaders(HttpRequestMessage request, MediaBrowser.Common.Net.HttpRequestOptions options)
        {
            bool flag = false;
            foreach (KeyValuePair<string, string> keyValuePair in options.RequestHeaders.ToList())
            {
                if (string.Equals(keyValuePair.Key, "User-Agent", StringComparison.OrdinalIgnoreCase))
                {
                    SetUserAgent(request, keyValuePair.Value);
                    flag = true;
                }
                else if (string.Equals(keyValuePair.Key, "Range", StringComparison.OrdinalIgnoreCase))
                {
                    RangeHeaderValue parsedValue;
                    if (RangeHeaderValue.TryParse(keyValuePair.Value, out parsedValue))
                        request.Headers.Range = parsedValue;
                    else
                        Log(LogSeverity.Debug, "Invalid range value {0}", keyValuePair.Value);
                }
                else
                    request.Headers.TryAddWithoutValidation(keyValuePair.Key, keyValuePair.Value);
            }
            if (!flag && options.EnableDefaultUserAgent)
                SetUserAgent(request, _defaultUserAgent);
            if (options.EnableKeepAlive)
                request.Headers.Connection.Add("Keep-Alive");
            if (!string.IsNullOrEmpty(options.Host))
                request.Headers.TryAddWithoutValidation("Host", options.Host);
            if (string.IsNullOrEmpty(options.Referer))
                return;
            request.Headers.TryAddWithoutValidation("Referer", options.Referer);
        }

        public string GetLogUrl(MediaBrowser.Common.Net.HttpRequestOptions options) => !string.IsNullOrEmpty(options.LogUrl) ? options.LogUrl.SanitizeUrl(options.Sanitation) : (string.IsNullOrEmpty(options.LogUrlPrefix) ? string.Empty : options.LogUrlPrefix + " ") + options.Url.SanitizeUrl(options.Sanitation);

        private void SetUserAgent(HttpRequestMessage request, string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return;
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        }

        void Log(LogSeverity severity, string message, params object[] paramList)
        {
            if (_logger != null)
                _logger.Log(severity, message, paramList);
        }

        private Exception GetException(Exception ex, MediaBrowser.Common.Net.HttpRequestOptions options)
        {
            return ex;
            /*            switch (ex)
                        {
                            case HttpException _:
                                return ex;
                            case WebException innerException2:
             label_3:
                                WebException innerException1 = innerException2;
                                if (innerException1 != null)
                                {
                                    if (options.LogErrors)
                                        _logger.ErrorException("Error " + innerException1.Status.ToString() + " getting response from " + GetLogUrl(options), (Exception)innerException1);
                                    HttpException exception = new HttpException(innerException1.Message, (Exception)innerException1);
                                    if (innerException1.Response is HttpWebResponse response)
                                    {
                                        exception.StatusCode = new HttpStatusCode?(response.StatusCode);
                                        if (response.StatusCode == HttpStatusCode.TooManyRequests)
                                            _lastTimeout = DateTimeOffset.UtcNow;
                                    }
                                    if (!exception.StatusCode.HasValue && (innerException1.Status == WebExceptionStatus.NameResolutionFailure || innerException1.Status == WebExceptionStatus.ConnectFailure))
                                        exception.IsTimedOut = true;
                                    return (Exception)exception;
                                }
                                if (!(ex is OperationCanceledException canceledException))
                                    canceledException = ex.InnerException as OperationCanceledException;
                                OperationCanceledException exception1 = canceledException;
                                if (exception1 != null)
                                    return GetCancellationException(options, client, options.CancellationToken, exception1);
                                if (options.LogErrors)
                                    _logger.ErrorException("Error getting response from {0}", ex, (object)GetLogUrl(options));
                                return ex;
                            default:
                                innerException2 = ex.InnerException as WebException;
                                goto label_3;
                        }*/
        }

        private Exception GetCancellationException(
            MediaBrowser.Common.Net.HttpRequestOptions options,
            CancellationToken cancellationToken,
            OperationCanceledException exception)
        {
            if (cancellationToken.IsCancellationRequested)
                return exception;
            string message = string.Format("Connection to {0} timed out", options.Url);
            Log(LogSeverity.Error, message);                
            _lastTimeout = DateTimeOffset.UtcNow;
            return new HttpException(message, exception)
            {
                IsTimedOut = true
            };
        }

        public Task<HttpResponseInfo> GetResponse(MediaBrowser.Common.Net.HttpRequestOptions options) => SendAsync(options, "GET");
        public Task<HttpResponseInfo> Post(MediaBrowser.Common.Net.HttpRequestOptions options) => throw new NotImplementedException();
        public Task<Stream> Get(MediaBrowser.Common.Net.HttpRequestOptions options) => throw new NotImplementedException();
        public IDisposable GetConnectionContext(MediaBrowser.Common.Net.HttpRequestOptions options) => throw new NotImplementedException();
        public Task<string> GetTempFile(MediaBrowser.Common.Net.HttpRequestOptions options) => throw new NotImplementedException();
        public Task<HttpResponseInfo> GetTempFileResponse(MediaBrowser.Common.Net.HttpRequestOptions options) => throw new NotImplementedException();
    }

}


/*
 * using System.Net;

var proxy = new WebProxy
{
    Address = new Uri("socks5://localhost:2080"),
    BypassProxyOnLocal = false,
    UseDefaultCredentials = false,
};

var handler = new HttpClientHandler
{
    Proxy = proxy
};

var httpClient = new HttpClient(handler);

var result = await httpClient.GetStringAsync("https://api.ipify.org/");
Console.WriteLine(result);
*/