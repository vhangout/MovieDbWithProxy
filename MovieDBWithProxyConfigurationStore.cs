using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MovieDbWithProxy
{
    internal class MovieDBWithProxyConfigurationStore : ConfigurationStore, IValidatingConfiguration

    {
        public const string ConfigurationKey = "moviedbwithproxy";
        public static string[] ConfigFields => new string[] { "ProxyType", "ProxyUrl", "ProxyPort", "EnableDebugLog" };
        public static string[] ProxyTypes => new string[] { "", "HTTP", "SOCKS4", "SOCKS5" };
        private readonly ILogger _logger;

        public MovieDBWithProxyConfigurationStore(ILogger logger)
        {
            ConfigurationType = typeof(MovieDbWithProxyConfiguration);
            Key = ConfigurationKey;
            _logger = logger;
        }

        public void Validate(object oldConfig, object newConfig)
        {
            _logger.Info("Call my vlidate");
            var config = (MovieDbWithProxyConfiguration)newConfig;
            if (!ProxyTypes.Contains(config.ProxyType))
                throw new ValidationException("Proxy type is invalid");
            if (!string.IsNullOrEmpty(config.ProxyType))
            {
                if (string.IsNullOrEmpty(config.ProxyUrl))
                    throw new ValidationException("Proxy URL cannot by empty");
                if (config.ProxyPort == null)
                    throw new ValidationException("Proxy port cannot be empty");
                if (config.ProxyPort.Value < 0 || config.ProxyPort > 65536)
                    throw new ValidationException("Proxy port may be in 1-65535");
            }
        }
    }
}
