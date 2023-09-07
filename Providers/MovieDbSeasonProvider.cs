using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using MovieDbWithProxy.Models;
using System.Globalization;
using System.Net;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace MovieDbWithProxy
{
    public class MovieDbSeasonProvider :
      IRemoteMetadataProviderWithOptions<Season, SeasonInfo>,
      IRemoteMetadataProvider<Season, SeasonInfo>,
      IMetadataProvider<Season>,
      IMetadataProvider,
      IRemoteMetadataProvider,
      IRemoteSearchProvider<SeasonInfo>,
      IRemoteSearchProvider,
      IHasMetadataFeatures
    {
        private const string GetTvInfo3 = "https://api.themoviedb.org/3/tv/{0}/season/{1}?api_key={2}&append_to_response=images,keywords,external_ids,credits,videos";
        private readonly IHttpClient _httpClient;
        private readonly IServerConfigurationManager _configurationManager;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localization;
        private readonly ILogger _logger;
        public string Name => "TheMovieDb";

        public MovieDbSeasonProvider(
      IHttpClient httpClient,
      IServerConfigurationManager configurationManager,
      IFileSystem fileSystem,
      ILocalizationManager localization,
      IJsonSerializer jsonSerializer,
      ILogManager logManager)
        {
            _httpClient = httpClient;
            _configurationManager = configurationManager;
            _fileSystem = fileSystem;
            _localization = localization;
            _jsonSerializer = jsonSerializer;
            _logger = logManager.GetLogger(GetType().Name);
        }

        public async Task<MetadataResult<Season>> GetMetadata(
          RemoteMetadataFetchOptions<SeasonInfo> options,
          CancellationToken cancellationToken)
        {
            SeasonInfo info = options.SearchInfo;
            MetadataResult<Season> result = new MetadataResult<Season>();
            string tmdbId;
            info.SeriesProviderIds.TryGetValue(MetadataProviders.Tmdb.ToString(), out tmdbId);
            int? seasonNumber = info.IndexNumber;
            if (!string.IsNullOrWhiteSpace(tmdbId) && seasonNumber.HasValue)
            {
                try
                {
                    RootObject rootObject = await EnsureSeasonInfo(tmdbId, seasonNumber.Value, info.MetadataLanguage, info.MetadataCountryCode, options.DirectoryService, cancellationToken).ConfigureAwait(false);
                    result.HasMetadata = true;
                    result.Item = new Season();
                    result.Item.Name = info.Name;
                    result.Item.IndexNumber = seasonNumber;
                    result.Item.Overview = rootObject.overview;
                    if (rootObject.external_ids.tvdb_id > 0)
                        ProviderIdsExtensions.SetProviderId(result.Item, MetadataProviders.Tvdb, rootObject.external_ids.tvdb_id.ToString(CultureInfo.InvariantCulture));
                    Credits credits = rootObject.credits;
                    if (credits != null)
                    {
                        List<TmdbCast> cast = credits.cast;
                        List<TmdbCrew> crew = credits.crew;
                    }
                    result.Item.PremiereDate = new DateTimeOffset?(rootObject.air_date);
                    result.Item.ProductionYear = new int?(result.Item.PremiereDate.Value.Year);
                }
                catch (HttpException ex)
                {
                    if (ex.StatusCode.HasValue && ex.StatusCode.Value == HttpStatusCode.NotFound)
                        return result;
                    throw;
                }
            }
            return result;
        }

        public Task<MetadataResult<Season>> GetMetadata(
          SeasonInfo info,
          CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public MetadataFeatures[] Features => new MetadataFeatures[1]
        {
            MetadataFeatures.Adult
        };

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
          SeasonInfo searchInfo,
          CancellationToken cancellationToken)
        {
            return Task.FromResult((IEnumerable<RemoteSearchResult>)new List<RemoteSearchResult>());
        }

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken) => _httpClient.GetResponse(new HttpRequestOptions()
        {
            CancellationToken = cancellationToken,
            Url = url
        });

        internal async Task<RootObject> EnsureSeasonInfo(
          string tmdbId,
          int seasonNumber,
          string language,
          string preferredMetadataCountry,
          IDirectoryService directoryService,
          CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(tmdbId))
                throw new ArgumentNullException(nameof(tmdbId));
            string path = GetDataFilePath(tmdbId, seasonNumber, language);
            FileSystemMetadata fileSystemInfo = _fileSystem.GetFileSystemInfo(path);
            RootObject rootObject = null;
            if (fileSystemInfo.Exists && DateTimeOffset.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileSystemInfo) <= MovieDbProviderBase.CacheTime)
                rootObject = await _jsonSerializer.DeserializeFromFileAsync<RootObject>(fileSystemInfo.FullName).ConfigureAwait(false);
            if (rootObject == null)
            {
                rootObject = await FetchMainResult(tmdbId, seasonNumber, language, preferredMetadataCountry, cancellationToken).ConfigureAwait(false);
                _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(path));
                _jsonSerializer.SerializeToFile(rootObject, path);
            }            
            return rootObject;
        }

        internal string GetDataFilePath(string tmdbId, int seasonNumber, string preferredLanguage)
        {
            if (string.IsNullOrEmpty(tmdbId))
                throw new ArgumentNullException(nameof(tmdbId));
            if (string.IsNullOrEmpty(preferredLanguage))
                preferredLanguage = "alllang";
            return Path.Combine(MovieDbSeriesProvider.GetSeriesDataPath(_configurationManager.ApplicationPaths, tmdbId), string.Format("season-{0}-{1}.json", seasonNumber.ToString(CultureInfo.InvariantCulture), preferredLanguage));
        }

        internal async Task<RootObject> FetchMainResult(
          string id,
          int seasonNumber,
          string language,
          string preferredMetadataCountry,
          CancellationToken cancellationToken)
        {
            string url = string.Format("https://api.themoviedb.org/3/tv/{0}/season/{1}?api_key={2}&append_to_response=images,keywords,external_ids,credits,videos", id, seasonNumber.ToString(CultureInfo.InvariantCulture), MovieDbProvider.ApiKey);
            if (!string.IsNullOrEmpty(language))
                url += string.Format("&language={0}", MovieDbProvider.NormalizeLanguage(language, preferredMetadataCountry));
            string str = MovieDbProvider.AddImageLanguageParam(url, language, preferredMetadataCountry);
            cancellationToken.ThrowIfCancellationRequested();
            MovieDbProvider current = MovieDbProvider.Current;
            RootObject rootObject;
            using (HttpResponseInfo response = await current.GetMovieDbResponse(new HttpRequestOptions()
            {
                Url = str,
                CancellationToken = cancellationToken,
                AcceptHeader = MovieDbProvider.AcceptHeader
            }).ConfigureAwait(false))
            {
                using (Stream json = response.Content)
                    rootObject = await _jsonSerializer.DeserializeFromStreamAsync<RootObject>(json).ConfigureAwait(false);
            }
            return rootObject;
        }

        public class Episode
        {
            public string air_date { get; set; }

            public int episode_number { get; set; }

            public int id { get; set; }

            public string name { get; set; }

            public string overview { get; set; }

            public string still_path { get; set; }

            public double vote_average { get; set; }

            public int vote_count { get; set; }
        }

        public class Credits
        {
            public List<TmdbCast> cast { get; set; }

            public List<TmdbCrew> crew { get; set; }
        }

        public class Images
        {
            public List<TmdbImage> posters { get; set; }
        }

        public class Videos
        {
            public List<object> results { get; set; }
        }

        public class RootObject
        {
            public DateTimeOffset air_date { get; set; }

            public List<Episode> episodes { get; set; }

            public string name { get; set; }

            public string overview { get; set; }

            public int id { get; set; }

            public string poster_path { get; set; }

            public int season_number { get; set; }

            public Credits credits { get; set; }

            public Images images { get; set; }

            public TmdbExternalIds external_ids { get; set; }

            public Videos videos { get; set; }
        }
    }
}
