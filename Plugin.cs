using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace MovieDbWithProxy
{
    public class Plugin : BasePlugin<MovieDbWithProxyConfiguration>, IHasThumbImage, IHasWebPages
    {

        public static Plugin Instance { get; set; }

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public override string Name => StaticName;
        public static string StaticName => "MovieDbWithProxy";
        public static string ProviderName => "TheMovieDb (proxy)";

        private Guid _id = new Guid(g: "522045F8-14D4-46E3-8DD6-580BE2E09F95");

        public override Guid Id => _id;

        public override string Description => "MovieDb metadata for movies with using proxy";

        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(name: $"{type.Namespace}.thumb.png");
        }

        public IEnumerable<PluginPageInfo> GetPages() => new[]
        {
            new PluginPageInfo()
            {
                Name = "proxysettings",
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.MovieDbWithProxyPage.html",
            },
            new PluginPageInfo()
            {
                Name = "proxysettingsjs",
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.MovieDbWithProxyPage.js",                
            },
        };

        
    }
}