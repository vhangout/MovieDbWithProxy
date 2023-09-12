using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using MovieDbWithProxy.Commons;
using MovieDbWithProxy.Models;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace MovieDbWithProxy
{
    internal class MovieDbImageProvider :
      IRemoteImageProviderWithOptions,
      IRemoteImageProvider,
      IImageProvider,
      IHasOrder
    {
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IFileSystem _fileSystem;

        public MovieDbImageProvider(
          IJsonSerializer jsonSerializer,
          IFileSystem fileSystem)
        {
            _jsonSerializer = jsonSerializer;
            _fileSystem = fileSystem;
        }

        public string Name => Plugin.ProviderName;

        public bool Supports(BaseItem item)
        {
            switch (item)
            {
                case Movie _:
                case MusicVideo _:
                    return true;
                default:
                    return item is Trailer;
            }
        }

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
            BaseItem item = options.Item;
            List<RemoteImageInfo> list = new List<RemoteImageInfo>();
            CompleteMovieData movieInfo = await GetMovieInfo(item, null, null, _jsonSerializer, cancellationToken).ConfigureAwait(false);
            Images results = movieInfo?.images;
            TmdbSettingsResult tmdbSettings = await MovieDbProvider.Current.GetTmdbSettings(cancellationToken).ConfigureAwait(false);
            string tmdbImageUrl = tmdbSettings.images.GetImageUrl("original");
            List<ImageType> list1 = GetSupportedImages(item).ToList();
            if (results != null)
            {
                if (list1.Contains(ImageType.Primary))
                    list.AddRange(GetPosters(results).Select(i => new RemoteImageInfo()
                    {
                        Url = tmdbImageUrl + i.file_path,
                        ThumbnailUrl = tmdbSettings.images.GetPosterThumbnailImageUrl(i.file_path),
                        CommunityRating = new double?(i.vote_average),
                        VoteCount = new int?(i.vote_count),
                        Width = new int?(i.width),
                        Height = new int?(i.height),
                        Language = NormalizeImageLanguage(i.iso_639_1),
                        ProviderName = Name,
                        Type = ImageType.Primary,
                        RatingType = RatingType.Score
                    }));
                if (list1.Contains(ImageType.Logo))
                    list.AddRange(GetLogos(results).Select(i => new RemoteImageInfo()
                    {
                        Url = tmdbImageUrl + i.file_path,
                        ThumbnailUrl = tmdbSettings.images.GetLogoThumbnailImageUrl(i.file_path),
                        CommunityRating = new double?(i.vote_average),
                        VoteCount = new int?(i.vote_count),
                        Width = new int?(i.width),
                        Height = new int?(i.height),
                        Language = NormalizeImageLanguage(i.iso_639_1),
                        ProviderName = Name,
                        Type = ImageType.Logo,
                        RatingType = RatingType.Score
                    }));
                if (list1.Contains(ImageType.Backdrop))
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
            }
            if (list1.Contains(ImageType.Primary))
            {
                string posterPath = movieInfo?.poster_path;
                if (!string.IsNullOrWhiteSpace(posterPath))
                    list.Add(new RemoteImageInfo()
                    {
                        ProviderName = Name,
                        Type = ImageType.Primary,
                        Url = tmdbImageUrl + posterPath
                    });
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

        public static string NormalizeImageLanguage(string lang) => string.Equals(lang, "xx", StringComparison.OrdinalIgnoreCase) ? (string)null : lang;

        private IEnumerable<TmdbImage> GetLogos(Images images) => (IEnumerable<TmdbImage>)images.logos ?? new List<TmdbImage>();

        private IEnumerable<TmdbImage> GetPosters(Images images) => (IEnumerable<TmdbImage>)images.posters ?? new List<TmdbImage>();

        private IEnumerable<TmdbImage> GetBackdrops(Images images) => (images.backdrops == null ? new List<TmdbImage>() : (IEnumerable<TmdbImage>)images.backdrops).OrderByDescending(i => i.vote_average).ThenByDescending(i => i.vote_count);

        private async Task<CompleteMovieData> GetMovieInfo(
          BaseItem item,
          string language,
          string preferredMetadataCountry,
          IJsonSerializer jsonSerializer,
          CancellationToken cancellationToken)
        {
            string providerId1 = ProviderIdsExtensions.GetProviderId(item, MetadataProviders.Tmdb);
            if (string.IsNullOrWhiteSpace(providerId1))
            {
                string providerId2 = ProviderIdsExtensions.GetProviderId(item, MetadataProviders.Imdb);
                if (!string.IsNullOrWhiteSpace(providerId2))
                {
                    CompleteMovieData movieInfo = await MovieDbProvider.Current.FetchMainResult(providerId2, false, language, preferredMetadataCountry, cancellationToken).ConfigureAwait(false);
                    if (movieInfo != null)
                        return movieInfo;
                }
                return null;
            }
            CompleteMovieData completeMovieData = await MovieDbProvider.Current.EnsureMovieInfo(providerId1, language, preferredMetadataCountry, cancellationToken).ConfigureAwait(false);
            return completeMovieData == null ? null : completeMovieData;
        }

        public int Order => 0;

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken) => EntryPoint.Current.HttpClient.GetResponse(new HttpRequestOptions()
        {
            CancellationToken = cancellationToken,
            Url = url
        });
    }
}
