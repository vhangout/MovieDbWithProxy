using MediaBrowser.Model.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MovieDbWithProxy
{
    public class MovieDbWithProxyConfiguration : BasePluginConfiguration
    {
        public string? ProxyType { get; set; }
        public string? ProxyUrl { get; set; }
        public string? ProxyPort { get; set; }
    }
}
