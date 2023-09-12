using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Security;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Logging;
using MovieDbWithProxy.Commons;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MovieDbWithProxy
{
    public class EntryPoint : IServerEntryPoint, IDisposable
    {
        public static EntryPoint Current;

        private readonly ILogger _logger;
        private readonly IConfigurationManager _config;
        private MovieDbWithProxyConfiguration options;
        public readonly IServerConfigurationManager ServerConfiguration;
        public readonly IAuthenticationRepository AuthRepo;

        public IHttpClient HttpClient { get; private set;}

        public EntryPoint(
            ILogger logger,
            IConfigurationManager config,
            IServerConfigurationManager serverConfiguration,
            IAuthenticationRepository authRepo)
        {            
            _logger = logger;
            _config = config;
            ServerConfiguration = serverConfiguration;
            AuthRepo = authRepo;
            _config.NamedConfigurationUpdated += new EventHandler<ConfigurationUpdateEventArgs>(ConfigWasUpdated);
            Current = this;            
        }        

        private void ConfigWasUpdated(object sender, ConfigurationUpdateEventArgs e)
        {
            if (!string.Equals(e.Key, MovieDbWithProxyConfigurationFactory.Key, StringComparison.OrdinalIgnoreCase))
                return;
            options = (MovieDbWithProxyConfiguration)_config.GetConfiguration(MovieDbWithProxyConfigurationFactory.Key);
            HttpClient = new HttpClientWithProxy(options);
        }        

        public void Run()
        {
            options = (MovieDbWithProxyConfiguration)_config.GetConfiguration(MovieDbWithProxyConfigurationFactory.Key);            
            HttpClient = new HttpClientWithProxy(options);
        }

        public void Dispose()
        {
            HttpClient = null;
            Current = null;
        }

        public void Log(object sender, LogSeverity severity, string message, params object[] paramList)
        {
            if (_logger != null && options.EnableDebugLog != null && options.EnableDebugLog.Value)
                _logger.Log(severity, $"{sender.GetType().Name}: {message}", paramList);
        }
    }
}
