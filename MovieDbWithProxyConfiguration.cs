using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MovieDbWithProxy
{
    public class MovieDbWithProxyConfiguration : BasePluginConfiguration
    {
        public string? ProxyType { get; set; }
        public string? ProxyUrl { get; set; }
        public int? ProxyPort { get; set; }        
        public bool? EnableDebugLog { get; set; }
    }
}
