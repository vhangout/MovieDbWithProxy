using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Logging;
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
        private readonly ILogger _logger;
        private readonly IConfigurationManager _config;        

        public EntryPoint(
            ILogger logger,
            IConfigurationManager config)
        {            
            _logger = logger;
            _config = config;
            _config.NamedConfigurationUpdated += new EventHandler<ConfigurationUpdateEventArgs>(ConfigWasUpdated);
        }        

        private void ConfigWasUpdated(object sender, ConfigurationUpdateEventArgs e)
        {
            if (!string.Equals(e.Key, MovieDbWithProxyConfigurationFactory.Key, StringComparison.OrdinalIgnoreCase))
                return;
            ReloadComponents(CancellationToken.None);
        }

        private async void ReloadComponents(CancellationToken cancellationToken)
        {
            var options = (MovieDbWithProxyConfiguration)_config.GetConfiguration(MovieDbWithProxyConfigurationFactory.Key);
            
        }

        public void Dispose()
        {            
        }

        public void Run()
        {
            throw new NotImplementedException();
        }
    }
}
