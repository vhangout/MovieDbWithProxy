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
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using MovieDbWithProxy.Models;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace MovieDbWithProxy
{
    public class MovieDbSeasonImageProvider :
      IRemoteImageProviderWithOptions,
      IRemoteImageProvider,
      IImageProvider,
      IHasOrder
    {
        public string Name => "TheMovieDb (proxy)";

        private readonly IHttpClient _httpClient;
        private readonly MovieDbSeasonProvider _seasonProvider;

        public MovieDbSeasonImageProvider(
          IJsonSerializer jsonSerializer,
          IServerConfigurationManager configurationManager,
          IHttpClient httpClient,
          IFileSystem fileSystem,
          ILocalizationManager localization,
          ILogManager logManager)
        {
            _httpClient = httpClient;
            _seasonProvider = new MovieDbSeasonProvider(httpClient, configurationManager, fileSystem, localization, jsonSerializer, logManager);
        }

        public bool Supports(BaseItem item) => item is Season;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new List<ImageType>()
        {
            ImageType.Primary
        };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(
          RemoteImageFetchOptions options,
          CancellationToken cancellationToken)
        {
            BaseItem baseItem = options.Item;
            List<RemoteImageInfo> list = new List<RemoteImageInfo>();
            Season season = (Season)baseItem;
            string tmdbId;
            season.Series.ProviderIds.TryGetValue(MetadataProviders.Tmdb.ToString(), out tmdbId);
            if (!string.IsNullOrWhiteSpace(tmdbId) && season.IndexNumber.HasValue)
            {
                try
                {
                    MovieDbSeasonProvider.RootObject seasonInfo = await _seasonProvider.EnsureSeasonInfo(tmdbId, season.IndexNumber.Value, null, null, options.DirectoryService, cancellationToken).ConfigureAwait(false);
                    TmdbSettingsResult tmdbSettingsResult = await MovieDbProvider.Current.GetTmdbSettings(cancellationToken).ConfigureAwait(false);
                    string imageUrl = tmdbSettingsResult.images.GetImageUrl("original");
                    if (seasonInfo?.images?.posters != null)
                    {
                        foreach (TmdbImage poster in seasonInfo.images.posters)
                            list.Add(new RemoteImageInfo()
                            {
                                Url = imageUrl + poster.file_path,
                                ThumbnailUrl = tmdbSettingsResult.images.GetPosterThumbnailImageUrl(poster.file_path),
                                CommunityRating = new double?(poster.vote_average),
                                VoteCount = new int?(poster.vote_count),
                                Width = new int?(poster.width),
                                Height = new int?(poster.height),
                                Language = MovieDbImageProvider.NormalizeImageLanguage(poster.iso_639_1),
                                ProviderName = Name,
                                Type = ImageType.Primary,
                                RatingType = RatingType.Score
                            });
                    }                    
                }
                catch (HttpException ex)
                {
                }
            }            
            return list;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(
          BaseItem item,
          LibraryOptions libraryOptions,
          CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public int Order => 2;

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken) => _httpClient.GetResponse(new HttpRequestOptions()
        {
            CancellationToken = cancellationToken,
            Url = url
        });
    }
}
