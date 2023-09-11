using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
