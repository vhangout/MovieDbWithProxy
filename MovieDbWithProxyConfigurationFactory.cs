using MediaBrowser.Common.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MovieDbWithProxy
{
    public class MovieDbWithProxyConfigurationFactory : IConfigurationFactory
    {
        public IEnumerable<ConfigurationStore> GetConfigurations() => (IEnumerable<ConfigurationStore>)new ConfigurationStore[1]
        {
            new ConfigurationStore()
            {
                ConfigurationType = typeof(MovieDbWithProxyConfiguration),
                Key = "moviedbwithproxy"
            }
        };
    }
}
