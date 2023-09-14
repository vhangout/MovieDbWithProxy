using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Logging;

namespace MovieDbWithProxy
{
    public class MovieDbWithProxyConfigurationFactory : IConfigurationFactory
    {
        public static string Key => "moviedbwithproxy";
        private readonly ILogger _logger;

        public MovieDbWithProxyConfigurationFactory(ILogger logger)
        {
            _logger = logger;
        }

        public IEnumerable<ConfigurationStore> GetConfigurations() => new ConfigurationStore[1]
        {
            new MovieDBWithProxyConfigurationStore(_logger)            
        };
    }
}
