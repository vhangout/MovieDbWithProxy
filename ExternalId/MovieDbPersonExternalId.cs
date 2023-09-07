using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace MovieDbWithProxy
{
    public class MovieDbPersonExternalId : IExternalId
    {
        public string Name => "TheMovieDb";

        public string Key => MetadataProviders.Tmdb.ToString();

        public string UrlFormatString => "https://www.themoviedb.org/person/{0}";

        public bool Supports(IHasProviderIds item) => item is Person;
    }
}
