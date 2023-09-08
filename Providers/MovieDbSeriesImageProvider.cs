using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using MovieDbWithProxy.Models;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace MovieDbWithProxy
{
    public class MovieDbSeriesImageProvider :
      IRemoteImageProviderWithOptions,
      IRemoteImageProvider,
      IImageProvider,
      IHasOrder
    {
        public string Name => "TheMovieDb (proxy)";

        private readonly IJsonSerializer _jsonSerializer;
        private readonly IHttpClient _httpClient;
        private readonly IFileSystem _fileSystem;

        public MovieDbSeriesImageProvider(
          IJsonSerializer jsonSerializer,
          IHttpClient httpClient,
          IFileSystem fileSystem)
        {
            _jsonSerializer = jsonSerializer;
            _httpClient = httpClient;
            _fileSystem = fileSystem;
        }

        public bool Supports(BaseItem item) => item is Series;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new List<ImageType>()
        {
            ImageType.Primary,
            ImageType.Backdrop,
            ImageType.Logo
        };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(
          RemoteImageFetchOptions options,
          CancellationToken cancellationToken)
        {
            List<RemoteImageInfo> list = new List<RemoteImageInfo>();
            MovieDbSeriesProvider.Images results = await FetchImages(options.Item, null, null, _jsonSerializer, cancellationToken).ConfigureAwait(false);
            if (results == null)
                return list;
            TmdbSettingsResult tmdbSettings = await MovieDbProvider.Current.GetTmdbSettings(cancellationToken).ConfigureAwait(false);
            string tmdbImageUrl = tmdbSettings.images.GetImageUrl("original");
            list.AddRange(GetPosters(results).Select(i => new RemoteImageInfo()
            {
                Url = tmdbImageUrl + i.file_path,
                ThumbnailUrl = tmdbSettings.images.GetPosterThumbnailImageUrl(i.file_path),
                CommunityRating = new double?(i.vote_average),
                VoteCount = new int?(i.vote_count),
                Width = new int?(i.width),
                Height = new int?(i.height),
                Language = MovieDbImageProvider.NormalizeImageLanguage(i.iso_639_1),
                ProviderName = Name,
                Type = ImageType.Primary,
                RatingType = RatingType.Score
            }));
            list.AddRange(GetLogos(results).Select(i => new RemoteImageInfo()
            {
                Url = tmdbImageUrl + i.file_path,
                ThumbnailUrl = tmdbSettings.images.GetLogoThumbnailImageUrl(i.file_path),
                CommunityRating = new double?(i.vote_average),
                VoteCount = new int?(i.vote_count),
                Width = new int?(i.width),
                Height = new int?(i.height),
                Language = MovieDbImageProvider.NormalizeImageLanguage(i.iso_639_1),
                ProviderName = Name,
                Type = ImageType.Logo,
                RatingType = RatingType.Score
            }));
            list.AddRange(GetBackdrops(results).Where(i => string.IsNullOrEmpty(i.iso_639_1)).Select(i => new RemoteImageInfo()
            {
                Url = tmdbImageUrl + i.file_path,
                ThumbnailUrl = tmdbSettings.images.GetBackdropThumbnailImageUrl(i.file_path),
                CommunityRating = new double?(i.vote_average),
                VoteCount = new int?(i.vote_count),
                Width = new int?(i.width),
                Height = new int?(i.height),
                ProviderName = Name,
                Type = ImageType.Backdrop,
                RatingType = RatingType.Score
            }));
            return list;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(
          BaseItem item,
          LibraryOptions libraryOptions,
          CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private IEnumerable<TmdbImage> GetLogos(MovieDbSeriesProvider.Images images) => (IEnumerable<TmdbImage>)images.logos ?? new List<TmdbImage>();

        private IEnumerable<TmdbImage> GetPosters(MovieDbSeriesProvider.Images images) => (IEnumerable<TmdbImage>)images.posters ?? new List<TmdbImage>();

        private IEnumerable<TmdbImage> GetBackdrops(MovieDbSeriesProvider.Images images) => (images.backdrops == null ? new List<TmdbImage>() : (IEnumerable<TmdbImage>)images.backdrops).OrderByDescending(i => i.vote_average).ThenByDescending(i => i.vote_count);

        private async Task<MovieDbSeriesProvider.Images> FetchImages(
          BaseItem item,
          string language,
          string country,
          IJsonSerializer jsonSerializer,
          CancellationToken cancellationToken)
        {
            string providerId = ProviderIdsExtensions.GetProviderId(item, MetadataProviders.Tmdb);
            return string.IsNullOrEmpty(providerId) ? null : (await MovieDbSeriesProvider.Current.EnsureSeriesInfo(providerId, language, country, cancellationToken).ConfigureAwait(false))?.images;
        }

        public int Order => 2;

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken) => _httpClient.GetResponse(new HttpRequestOptions()
        {
            CancellationToken = cancellationToken,
            Url = url
        });
    }
}
