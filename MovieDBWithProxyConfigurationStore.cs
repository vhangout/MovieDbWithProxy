using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Logging;
using System.ComponentModel.DataAnnotations;

namespace MovieDbWithProxy
{
    internal class MovieDBWithProxyConfigurationStore : ConfigurationStore, IValidatingConfiguration

    {
        public const string ConfigurationKey = "moviedbwithproxy";
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
            var config = (MovieDbWithProxyConfiguration)newConfig;
            if (!config.Enable.GetValueOrDefault(false))
                return;
            if (!ProxyTypes.Contains(config.ProxyType))
                throw new ValidationException("Proxy type is invalid");            
            if (string.IsNullOrEmpty(config.ProxyUrl))
                throw new ValidationException("Proxy URL cannot by empty");
            if (config.ProxyPort == null)
                throw new ValidationException("Proxy port cannot be empty");
            if (config.ProxyPort.Value < 0 || config.ProxyPort > 65536)
                throw new ValidationException("Proxy port may be in 1-65535");            
            if (!config.EnableCredentials.GetValueOrDefault(false))
                return;
            if (string.IsNullOrEmpty(config.Login))
                throw new ValidationException("Login cannot by empty");
            if (string.IsNullOrEmpty(config.Password))
                throw new ValidationException("Password cannot by empty");
        }
    }
}
