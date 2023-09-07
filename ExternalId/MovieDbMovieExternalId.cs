using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace MovieDbWithProxy
{
    public class MovieDbMovieExternalId : IExternalId
    {
        public const string BaseMovieDbUrl = "https://www.themoviedb.org/";

        public string Name => "TheMovieDb";

        public string Key => MetadataProviders.Tmdb.ToString();

        public string UrlFormatString => "https://www.themoviedb.org/movie/{0}";

        public bool Supports(IHasProviderIds item) => item is LiveTvProgram liveTvProgram && liveTvProgram.IsMovie || item is Movie || item is MusicVideo || item is Trailer;
    }
}
