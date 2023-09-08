using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MovieDbWithProxy.Models;
using System.Globalization;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace MovieDbWithProxy
{
    public abstract class MovieDbProviderBase
    {
        private const string EpisodeUrlPattern = "https://api.themoviedb.org/3/tv/{0}/season/{1}/episode/{2}?api_key={3}&append_to_response=images,external_ids,credits,videos";
        private readonly IHttpClient _httpClient;
        private readonly IServerConfigurationManager _configurationManager;
        private readonly IJsonSerializer _jsonSerializer;
        protected readonly IFileSystem FileSystem;
        private readonly ILocalizationManager _localization;
        private readonly ILogger _logger;
        public static TimeSpan CacheTime = TimeSpan.FromHours(6.0);

        public MovieDbProviderBase(
          IHttpClient httpClient,
          IServerConfigurationManager configurationManager,
          IJsonSerializer jsonSerializer,
          IFileSystem fileSystem,
          ILocalizationManager localization,
          ILogManager logManager)
        {
            _httpClient = httpClient;
            _configurationManager = configurationManager;
            _jsonSerializer = jsonSerializer;
            FileSystem = fileSystem;
            _localization = localization;
            _logger = logManager.GetLogger(GetType().Name);
        }

        protected ILogger Logger => _logger;

        protected async Task<RootObject> GetEpisodeInfo(
          string tmdbId,
          int seasonNumber,
          int episodeNumber,
          string language,
          string preferredMetadataCountry,
          IDirectoryService directoryService,
          CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(tmdbId))
                throw new ArgumentNullException(nameof(tmdbId));
            if (string.IsNullOrEmpty(language))
                throw new ArgumentNullException(nameof(language));
            string cacheKey = "tmdb_episode_" + tmdbId + "_" + language + "_" + preferredMetadataCountry + "_" + seasonNumber.ToString() + "_" + episodeNumber.ToString();
            RootObject rootObject;
            if (!directoryService.TryGetFromCache(cacheKey, out rootObject))
            {
                string dataFilePath = GetDataFilePath(tmdbId, seasonNumber, episodeNumber, language);
                FileSystemMetadata fileSystemInfo = FileSystem.GetFileSystemInfo(dataFilePath);
                if (fileSystemInfo.Exists && DateTimeOffset.UtcNow - FileSystem.GetLastWriteTimeUtc(fileSystemInfo) <= CacheTime)
                    rootObject = await _jsonSerializer.DeserializeFromFileAsync<RootObject>(dataFilePath).ConfigureAwait(false);
                if (rootObject == null)
                {
                    FileSystem.CreateDirectory(FileSystem.GetDirectoryName(dataFilePath));
                    rootObject = await DownloadEpisodeInfo(tmdbId, seasonNumber, episodeNumber, language, preferredMetadataCountry, dataFilePath, cancellationToken).ConfigureAwait(false);
                    using (Stream fileStream = FileSystem.GetFileStream(dataFilePath, FileOpenMode.Create, FileAccessMode.Write, FileShareMode.Read, false))
                        _jsonSerializer.SerializeToStream(rootObject, fileStream);
                }
                directoryService.AddOrUpdateCache(cacheKey, rootObject);
            }
            return rootObject;
        }

        internal string GetDataFilePath(
          string tmdbId,
          int seasonNumber,
          int episodeNumber,
          string preferredLanguage)
        {
            if (string.IsNullOrEmpty(tmdbId))
                throw new ArgumentNullException(nameof(tmdbId));
            if (string.IsNullOrEmpty(preferredLanguage))
                throw new ArgumentNullException(nameof(preferredLanguage));
            return Path.Combine(MovieDbSeriesProvider.GetSeriesDataPath(_configurationManager.ApplicationPaths, tmdbId), string.Format("season-{0}-episode-{1}-{2}.json", seasonNumber.ToString(CultureInfo.InvariantCulture), episodeNumber.ToString(CultureInfo.InvariantCulture), preferredLanguage));
        }

        internal async Task<RootObject> DownloadEpisodeInfo(
          string id,
          int seasonNumber,
          int episodeNumber,
          string preferredMetadataLanguage,
          string preferredMetadataCountry,
          string dataFilePath,
          CancellationToken cancellationToken)
        {
            RootObject rootObject = await FetchMainResult("https://api.themoviedb.org/3/tv/{0}/season/{1}/episode/{2}?api_key={3}&append_to_response=images,external_ids,credits,videos", id, seasonNumber, episodeNumber, preferredMetadataLanguage, preferredMetadataCountry, cancellationToken).ConfigureAwait(false);
            FileSystem.CreateDirectory(FileSystem.GetDirectoryName(dataFilePath));
            _jsonSerializer.SerializeToFile(rootObject, dataFilePath);
            return rootObject;
        }

        internal async Task<RootObject> FetchMainResult(
          string urlPattern,
          string id,
          int seasonNumber,
          int episodeNumber,
          string language,
          string country,
          CancellationToken cancellationToken)
        {
            string url = string.Format(urlPattern, id, seasonNumber.ToString(CultureInfo.InvariantCulture), episodeNumber, MovieDbProvider.ApiKey);
            if (!string.IsNullOrEmpty(language))
                url += string.Format("&language={0}", MovieDbProvider.NormalizeLanguage(language, country));
            string str = MovieDbProvider.AddImageLanguageParam(url, language, country);
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

        protected Task<HttpResponseInfo> GetResponse(string url, CancellationToken cancellationToken) => _httpClient.GetResponse(new HttpRequestOptions()
        {
            CancellationToken = cancellationToken,
            Url = url
        });

        public class Images
        {
            public List<TmdbImage> stills { get; set; }
        }

        public class GuestStar
        {
            public int id { get; set; }

            public string name { get; set; }

            public string credit_id { get; set; }

            public string character { get; set; }

            public int order { get; set; }

            public string profile_path { get; set; }
        }

        public class Credits
        {
            public List<TmdbCast> cast { get; set; }

            public List<TmdbCrew> crew { get; set; }

            public List<GuestStar> guest_stars { get; set; }
        }

        public class Videos
        {
            public List<Video> results { get; set; }
        }

        public class Video
        {
            public string id { get; set; }

            public string iso_639_1 { get; set; }

            public string iso_3166_1 { get; set; }

            public string key { get; set; }

            public string name { get; set; }

            public string site { get; set; }

            public string size { get; set; }

            public string type { get; set; }
        }

        public class RootObject
        {
            public DateTimeOffset air_date { get; set; }

            public int episode_number { get; set; }

            public string name { get; set; }

            public string overview { get; set; }

            public int id { get; set; }

            public object production_code { get; set; }

            public int season_number { get; set; }

            public string still_path { get; set; }

            public double vote_average { get; set; }

            public int vote_count { get; set; }

            public Images images { get; set; }

            public TmdbExternalIds external_ids { get; set; }

            public Credits credits { get; set; }

            public Videos videos { get; set; }
        }
    }
}
