using MediaBrowser.Model.Plugins;

namespace MovieDbWithProxy
{
    public class MovieDbWithProxyConfiguration : BasePluginConfiguration
    {
        public bool? Enable { get; set; }
        public string? ProxyType { get; set; }
        public string? ProxyUrl { get; set; }
        public int? ProxyPort { get; set; }
        public bool? EnableCredentials { get; set; }
        public string? Login { get; set; }
        public string? Password { get; set; }
        public bool? EnableDebugLog { get; set; }
    }
}
