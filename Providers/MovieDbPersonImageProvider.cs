using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using MovieDbWithProxy.Commons;
using MovieDbWithProxy.Models;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace MovieDbWithProxy
{
    public class MovieDbPersonImageProvider :
      IRemoteImageProviderWithOptions,
      IRemoteImageProvider,
      IImageProvider,
      IHasOrder
    {
        public string Name => Plugin.ProviderName;

        private readonly IServerConfigurationManager _config;
        private readonly IJsonSerializer _jsonSerializer;

        public MovieDbPersonImageProvider(
          IServerConfigurationManager config,
          IJsonSerializer jsonSerializer)
        {
            _config = config;
            _jsonSerializer = jsonSerializer;
        }

        public bool Supports(BaseItem item) => item is Person;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new List<ImageType>()
        {
            ImageType.Primary
        };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(
          RemoteImageFetchOptions options,
          CancellationToken cancellationToken)
        {
            BaseItem baseItem = options.Item;
            LibraryOptions libraryOptions = options.LibraryOptions;
            IDirectoryService directoryService = options.DirectoryService;
            string providerId = ProviderIdsExtensions.GetProviderId(baseItem, (MetadataProviders)3);
            if (string.IsNullOrEmpty(providerId))
                return new List<RemoteImageInfo>();
            string metadataLanguage = baseItem.GetPreferredMetadataLanguage(libraryOptions);
            MovieDbPersonProvider.Images images = (await MovieDbPersonProvider.Current.EnsurePersonInfo(providerId, metadataLanguage, directoryService, cancellationToken).ConfigureAwait(false)).images ?? new MovieDbPersonProvider.Images();
            TmdbSettingsResult tmdbSettings = await MovieDbProvider.Current.GetTmdbSettings(cancellationToken).ConfigureAwait(false);
            string imageUrl = tmdbSettings.images.GetImageUrl("original");
            return GetImages(images, tmdbSettings, imageUrl);
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(
          BaseItem item,
          LibraryOptions libraryOptions,
          CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private IEnumerable<RemoteImageInfo> GetImages(
          MovieDbPersonProvider.Images images,
          TmdbSettingsResult tmdbSettings,
          string baseImageUrl)
        {
            List<RemoteImageInfo> images1 = new List<RemoteImageInfo>();
            if (images.profiles != null)
                images1.AddRange(images.profiles.Select(i => new RemoteImageInfo()
                {
                    Url = baseImageUrl + i.file_path,
                    ThumbnailUrl = tmdbSettings.images.GetProfileThumbnailImageUrl(i.file_path),
                    CommunityRating = new double?(i.vote_average),
                    VoteCount = new int?(i.vote_count),
                    Width = new int?(i.width),
                    Height = new int?(i.height),
                    ProviderName = Name,
                    Type = ImageType.Primary,
                    RatingType = RatingType.Score
                }));
            return images1;
        }

        public int Order => 0;

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken) => EntryPoint.Current.HttpClient.GetResponse(new HttpRequestOptions()
        {
            CancellationToken = cancellationToken,
            Url = url
        });
    }
}
