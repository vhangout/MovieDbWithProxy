using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using MovieDbWithProxy.Models;
using System.Net;

namespace MovieDbWithProxy
{
    public class MovieDbEpisodeImageProvider :
      MovieDbProviderBase,
      IRemoteImageProviderWithOptions,
      IRemoteImageProvider,
      IImageProvider,
      IHasOrder
    {
        public string Name => Plugin.ProviderName;

        public MovieDbEpisodeImageProvider(
          IHttpClient httpClient,
          IServerConfigurationManager configurationManager,
          IJsonSerializer jsonSerializer,
          IFileSystem fileSystem,
          ILocalizationManager localization)
          : base(httpClient, configurationManager, jsonSerializer, fileSystem, localization)
        {
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new List<ImageType>()
        {
            ImageType.Primary
        };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(
          RemoteImageFetchOptions options,
          CancellationToken cancellationToken)
        {
            EntryPoint.Current.LogCall();
            MovieDbEpisodeImageProvider episodeImageProvider = this;
            BaseItem baseItem = options.Item;
            LibraryOptions libraryOptions = options.LibraryOptions;
            Episode episode = (Episode)baseItem;
            Series series = episode.Series;
            string providerId = series != null ? ProviderIdsExtensions.GetProviderId(series, MetadataProviders.Tmdb) : null;
            List<RemoteImageInfo> list = new List<RemoteImageInfo>();
            if (string.IsNullOrEmpty(providerId))
                return list;
            int? parentIndexNumber = episode.ParentIndexNumber;
            int? indexNumber = episode.IndexNumber;
            if (!parentIndexNumber.HasValue || !indexNumber.HasValue)
                return list;
            string metadataLanguage = baseItem.GetPreferredMetadataLanguage(libraryOptions);
            try
            {
                RootObject response = await episodeImageProvider.GetEpisodeInfo(providerId, parentIndexNumber.Value, indexNumber.Value, metadataLanguage, baseItem.GetPreferredMetadataCountryCode(libraryOptions), options.DirectoryService, cancellationToken).ConfigureAwait(false);
                TmdbSettingsResult tmdbSettings = await MovieDbProvider.Current.GetTmdbSettings(cancellationToken).ConfigureAwait(false);
                string tmdbImageUrl = tmdbSettings.images.GetImageUrl("original");
                list.AddRange(episodeImageProvider.GetPosters(response.images).Select(i => new RemoteImageInfo()
                {
                    Url = tmdbImageUrl + i.file_path,
                    ThumbnailUrl = tmdbSettings.images.GetBackdropThumbnailImageUrl(i.file_path),
                    CommunityRating = new double?(i.vote_average),
                    VoteCount = new int?(i.vote_count),
                    Width = new int?(i.width),
                    Height = new int?(i.height),
                    Language = MovieDbImageProvider.NormalizeImageLanguage(i.iso_639_1),
                    ProviderName = Name,
                    Type = ImageType.Primary,
                    RatingType = RatingType.Score
                }));
                return list;
            }
            catch (HttpException ex)
            {
                if (ex.StatusCode.HasValue && ex.StatusCode.Value == HttpStatusCode.NotFound)
                    return list;
                throw;
            }
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(
          BaseItem item,
          LibraryOptions libraryOptions,
          CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private IEnumerable<TmdbImage> GetPosters(Images images) => (IEnumerable<TmdbImage>)images.stills ?? new List<TmdbImage>();        

        public bool Supports(BaseItem item) => item is Episode;

        public int Order => 1;

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            EntryPoint.Current.LogCall();
            return MovieDbProvider.Current.GetImageResponse(url, cancellationToken);
        }
    }
}
