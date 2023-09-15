using System.Diagnostics;
using System.Runtime.CompilerServices;

using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Security;
using MediaBrowser.Model.Logging;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;
using Emby.Server.Implementations.HttpClientManager;
using System.Net;

namespace MovieDbWithProxy
{
    public class EntryPoint : IServerEntryPoint, IDisposable
    {
        public static EntryPoint Current;

        private readonly ILogger _logger;
        private readonly IConfigurationManager _config;
        private MovieDbWithProxyConfiguration _options;
        public readonly IServerConfigurationManager ServerConfiguration;
        public readonly IAuthenticationRepository AuthRepo;

        private readonly IHttpClient _httpClient;
        private readonly HttpClientInfo[] _clientInfo;


        public EntryPoint(
            ILogger logger,
            IHttpClient httpClient,
            IConfigurationManager config,
            IServerConfigurationManager serverConfiguration,
            IAuthenticationRepository authRepo)
        {            
            _logger = logger;
            _config = config;
            ServerConfiguration = serverConfiguration;
            AuthRepo = authRepo;
            _httpClient = httpClient;
            _clientInfo = new HttpClientInfo[]
            {
                (HttpClientInfo)_httpClient.GetConnectionContext(new HttpRequestOptions { Url = "https://image.tmdb.org" }),
                (HttpClientInfo)_httpClient.GetConnectionContext(new HttpRequestOptions { Url = "https://api.themoviedb.org" }),
            };

            _options = (MovieDbWithProxyConfiguration)_config.GetConfiguration(MovieDbWithProxyConfigurationFactory.Key);

            _config.NamedConfigurationUpdated += new EventHandler<ConfigurationUpdateEventArgs>(ConfigWasUpdated);
            Current = this;
        }

        private void ConfigWasUpdated(object sender, ConfigurationUpdateEventArgs e)
        {
            if (!string.Equals(e.Key, MovieDbWithProxyConfigurationFactory.Key, StringComparison.OrdinalIgnoreCase))
                return;
            LogCall();
            updateClients();
        }

        public void Run()
        {
            LogCall();            
            updateClients();
        }

        private void updateClients()
        {
            _options = (MovieDbWithProxyConfiguration)_config.GetConfiguration(MovieDbWithProxyConfigurationFactory.Key);
            foreach (HttpClientInfo context in _clientInfo)
            {
                context.HttpClient = null;
                var handler = new HttpClientHandler();

                if (_options.Enable.GetValueOrDefault(false))
                {                    
                    handler.Proxy = new WebProxy
                    {
                        Address = new Uri($"{_options.ProxyType.ToLower()}://{_options.ProxyUrl}:{_options.ProxyPort}"),
                        BypassProxyOnLocal = false,
                        UseDefaultCredentials = false                                               
                    };
                    if (_options.EnableCredentials.GetValueOrDefault(false))
                    {
                        Log("Setup credential");
                        handler.Proxy.Credentials = new NetworkCredential(_options.Login, _options.Password);
                    }
                }

                context.HttpClient = new HttpClient(handler);
            }
        }

        public void Dispose()
        {
            Current = null;
        }

        public void Log(object sender, LogSeverity severity, string message, params object[] paramList)
        {
            if (_options.EnableDebugLog.GetValueOrDefault(false))
                _logger.Log(severity, $"{sender.GetType().Name}: {message}", paramList);
        }

        public void Log(string message)
        {
            if (_options.EnableDebugLog.GetValueOrDefault(false))
                _logger.Log(LogSeverity.Info, message);
        }

        public void LogStack()
        {
            if (_options.EnableDebugLog.GetValueOrDefault(false))
                _logger.Log(LogSeverity.Info, new StackTrace(true).ToString());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void LogCall()
        {
            if (_options.EnableDebugLog.GetValueOrDefault(false))
            {
                var st = new StackTrace(true);
                var sf = st.GetFrame(1);
                _logger.Log(LogSeverity.Info, $"*** {sf.GetMethod().ReflectedType.Name}:{sf.GetMethod().Name} ***");
            }

        }

        public void LogDump(object obj)
        {
            if (_options.EnableDebugLog.GetValueOrDefault())
            {
                _logger.Log(LogSeverity.Info, $"{ObjectDumper.Dump(obj, DumpStyle.CSharp)}");
            }

        }
    }
}
