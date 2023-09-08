using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MovieDbWithProxy.Models;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace MovieDbWithProxy
{
    internal class MovieDbBoxSetImageProvider :
      IRemoteImageProviderWithOptions,
      IRemoteImageProvider,
      IImageProvider,
      IHasOrder
    {
        public string Name => Plugin.ProviderName;

        private readonly IHttpClient _httpClient;

        public MovieDbBoxSetImageProvider(IHttpClient httpClient) => _httpClient = httpClient;
        

        public bool Supports(BaseItem item) => item is BoxSet;

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
            string providerId = ProviderIdsExtensions.GetProviderId(options.Item, MetadataProviders.Tmdb);
            if (!string.IsNullOrEmpty(providerId))
            {
                MovieDbBoxSetProvider.RootObject mainResult = await MovieDbBoxSetProvider.Current.GetMovieDbResult(providerId, null, null, cancellationToken).ConfigureAwait(false);
                if (mainResult != null)
                {
                    TmdbSettingsResult tmdbSettings = await MovieDbProvider.Current.GetTmdbSettings(cancellationToken).ConfigureAwait(false);
                    string imageUrl = tmdbSettings.images.GetImageUrl("original");
                    return GetImages(mainResult, tmdbSettings, imageUrl);
                }                
            }
            return new List<RemoteImageInfo>();
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(
          BaseItem item,
          LibraryOptions libraryOptions,
          CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private IEnumerable<RemoteImageInfo> GetImages(
          MovieDbBoxSetProvider.RootObject obj,
          TmdbSettingsResult tmdbSettings,
          string baseUrl)
        {
            List<RemoteImageInfo> images1 = new List<RemoteImageInfo>();
            MovieDbBoxSetProvider.Images images2 = obj.images ?? new MovieDbBoxSetProvider.Images();
            images1.AddRange(GetPosters(images2).Select(i => new RemoteImageInfo()
            {
                Url = baseUrl + i.file_path,
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
            images1.AddRange(GetLogos(images2).Select(i => new RemoteImageInfo()
            {
                Url = baseUrl + i.file_path,
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
            images1.AddRange(GetBackdrops(images2).Where(i => string.IsNullOrEmpty(i.iso_639_1)).Select(i => new RemoteImageInfo()
            {
                Url = baseUrl + i.file_path,
                ThumbnailUrl = tmdbSettings.images.GetBackdropThumbnailImageUrl(i.file_path),
                CommunityRating = new double?(i.vote_average),
                VoteCount = new int?(i.vote_count),
                Width = new int?(i.width),
                Height = new int?(i.height),
                ProviderName = Name,
                Type = ImageType.Backdrop,
                RatingType = RatingType.Score
            }));
            return images1;
        }

        private IEnumerable<TmdbImage> GetLogos(MovieDbBoxSetProvider.Images images) => (IEnumerable<TmdbImage>)images.logos ?? new List<TmdbImage>();

        private IEnumerable<TmdbImage> GetPosters(MovieDbBoxSetProvider.Images images) => (IEnumerable<TmdbImage>)images.posters ?? new List<TmdbImage>();

        private IEnumerable<TmdbImage> GetBackdrops(MovieDbBoxSetProvider.Images images) => (images.backdrops == null ? new List<TmdbImage>() : (IEnumerable<TmdbImage>)images.backdrops).OrderByDescending(i => i.vote_average).ThenByDescending(i => i.vote_count);

        public int Order => 0;

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken) => _httpClient.GetResponse(new HttpRequestOptions()
        {
            CancellationToken = cancellationToken,
            Url = url
        });
    }
}
