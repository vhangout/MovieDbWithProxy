using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using System.Globalization;
using System.Net;

using MediaBrowser.Model.IO;
using MediaBrowser.Common;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Configuration;
using MovieDbWithProxy.Models;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace MovieDbWithProxy
{
    /// <summary>
    /// Class MovieDbProvider
    /// </summary>
    public class MovieDbProvider : IRemoteMetadataProvider<Movie, MovieInfo>,
        IMetadataProvider<Movie>,
        IRemoteMetadataProvider,
        IRemoteSearchProvider<MovieInfo>,
        IHasOrder,
        IHasMetadataFeatures
    {
        public string Name => Plugin.ProviderName;

        private readonly IJsonSerializer _jsonSerializer;
        private readonly IHttpClient _httpClient;
        private readonly IFileSystem _fileSystem;
        private readonly IServerConfigurationManager _configurationManager;
        private readonly ILogger _logger;
        private readonly ILocalizationManager _localization;
        private readonly ILibraryManager _libraryManager;
        private readonly IApplicationHost _appHost;

        private readonly CultureInfo _usCulture = new CultureInfo("en-US");
        private TmdbSettingsResult? _tmdbSettings;
        public const string BaseMovieDbUrl = "https://api.themoviedb.org/";

        private const string TmdbConfigUrl = BaseMovieDbUrl + "3/configuration?api_key={0}";
        private const string GetMovieInfo3 = BaseMovieDbUrl + @"3/movie/{0}?api_key={1}&append_to_response=alternative_titles,reviews,casts,releases,images,keywords,trailers";

        internal static string ApiKey = "f6bd687ffa63cd282b6ff2c6877f2669";
        internal static string AcceptHeader = "application/json,image/*";

        private static long _lastRequestTicks;
        // The limit is 40 requests per 10 seconds
        private static int requestIntervalMs = 300;

        internal static MovieDbProvider Current { get; private set; }

        public MetadataFeatures[] Features => new MetadataFeatures[2]
        {
            MetadataFeatures.Adult,
            MetadataFeatures.Collections
        };

        public MovieDbProvider(
            IJsonSerializer jsonSerializer,
            IHttpClient httpClient,
            IFileSystem fileSystem,
            IServerConfigurationManager configurationManager,
            ILogger logger,
            ILocalizationManager localization,
            ILibraryManager libraryManager,
            IApplicationHost appHost)
        {
            _jsonSerializer = jsonSerializer;
            _httpClient = httpClient;
            _fileSystem = fileSystem;
            _configurationManager = configurationManager;
            _logger = logger;
            _localization = localization;
            _libraryManager = libraryManager;
            _appHost = appHost;
            Current = this;
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
        {
            return GetMovieSearchResults(searchInfo, cancellationToken);
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetMovieSearchResults(ItemLookupInfo searchInfo, CancellationToken cancellationToken)
        {
            var providerId1 = searchInfo.GetProviderId(MetadataProviders.Tmdb);

            if (!string.IsNullOrEmpty(providerId1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                CompleteMovieData obj = await EnsureMovieInfo(providerId1, searchInfo.MetadataLanguage, searchInfo.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
                if (obj == null)
                    return new List<RemoteSearchResult>();

                string imageUrl = (await GetTmdbSettings(cancellationToken).ConfigureAwait(false)).images.GetImageUrl("original");

                RemoteSearchResult instance = new RemoteSearchResult()
                {
                    Name = obj.GetTitle(),
                    SearchProviderName = Name,
                    ImageUrl = string.IsNullOrWhiteSpace(obj.poster_path) ? null : imageUrl + obj.poster_path
                };

                DateTimeOffset result;
                if (!string.IsNullOrEmpty(obj.release_date) && DateTimeOffset.TryParse(obj.release_date, _usCulture, DateTimeStyles.None, out result))
                {
                    instance.PremiereDate = new DateTimeOffset?(result.ToUniversalTime());
                    instance.ProductionYear = new int?(instance.PremiereDate.Value.Year);
                }
                instance.SetProviderId(MetadataProviders.Tmdb, obj.id.ToString(_usCulture));
                if (!string.IsNullOrWhiteSpace(obj.imdb_id))
                    instance.SetProviderId(MetadataProviders.Imdb, obj.imdb_id);
                return new RemoteSearchResult[1] { instance };
            }
            string providerId2 = searchInfo.GetProviderId(MetadataProviders.Imdb);
            if (!string.IsNullOrEmpty(providerId2))
            {
                RemoteSearchResult remoteSearchResult = await new MovieDbSearch(_logger, _jsonSerializer, _libraryManager).FindMovieByExternalId(providerId2, "imdb_id", MetadataProviders.Imdb.ToString(), cancellationToken).ConfigureAwait(false);
                if (remoteSearchResult != null)
                    return new RemoteSearchResult[1]
                    {
                        remoteSearchResult
                    };
            }
            return await new MovieDbSearch(_logger, _jsonSerializer, _libraryManager).GetMovieSearchResults(searchInfo, cancellationToken).ConfigureAwait(false);
        }
        public Task<MetadataResult<Movie>> GetMetadata(
            MovieInfo info,
            CancellationToken cancellationToken)
        {
            return GetItemMetadata<Movie>(info, cancellationToken);
        }

        public Task<MetadataResult<T>> GetItemMetadata<T>(
          ItemLookupInfo id,
          CancellationToken cancellationToken)
          where T : BaseItem, new()
        {
            return new GenericMovieDbInfo<T>(_logger, _jsonSerializer, _libraryManager, _fileSystem).GetMetadata(id, cancellationToken);
        }

        internal async Task<TmdbSettingsResult> GetTmdbSettings(CancellationToken cancellationToken)
        {
            MovieDbProvider movieDbProvider1 = this;
            if (movieDbProvider1._tmdbSettings != null)
                return movieDbProvider1._tmdbSettings;
            MovieDbProvider movieDbProvider2 = movieDbProvider1;
            using (HttpResponseInfo response = await movieDbProvider2.GetMovieDbResponse(new HttpRequestOptions()
            {
                Url = string.Format("https://api.themoviedb.org/3/configuration?api_key={0}", ApiKey),
                CancellationToken = cancellationToken,
                AcceptHeader = AcceptHeader
            }).ConfigureAwait(false))
            {
                using (Stream json = response.Content)
                {
                    using (StreamReader reader = new StreamReader(json))
                    {
                        string text = await reader.ReadToEndAsync().ConfigureAwait(false);
                        movieDbProvider1._logger.Info("MovieDb settings: {0}", text);
                        movieDbProvider1._tmdbSettings = movieDbProvider1._jsonSerializer.DeserializeFromString<TmdbSettingsResult>(text);
                        return movieDbProvider1._tmdbSettings;
                    }
                }
            }
        }

        internal static string GetMovieDataPath(IApplicationPaths appPaths, string tmdbId) => Path.Combine(MovieDbProvider.GetMoviesDataPath(appPaths), tmdbId);

        internal static string GetMoviesDataPath(IApplicationPaths appPaths) => Path.Combine(appPaths.CachePath, "tmdb-movies2");

        internal async Task<CompleteMovieData> DownloadMovieInfo(
          string id,
          string preferredMetadataLanguage,
          string preferredMetadataCountry,
          CancellationToken cancellationToken)
        {
            CompleteMovieData completeMovieData = await FetchMainResult(id, true, preferredMetadataLanguage, preferredMetadataCountry, cancellationToken).ConfigureAwait(false);
            if (completeMovieData == null)
                return null;
            string dataFilePath = GetDataFilePath(id, preferredMetadataLanguage);
            _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(dataFilePath));
            _jsonSerializer.SerializeToFile((object)completeMovieData, dataFilePath);
            return completeMovieData;
        }

        internal Task<CompleteMovieData> EnsureMovieInfo(
          string tmdbId,
          string language,
          string country,
          CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(tmdbId))
                throw new ArgumentNullException(nameof(tmdbId));
            FileSystemMetadata fileSystemInfo = _fileSystem.GetFileSystemInfo(GetDataFilePath(tmdbId, language));
            return fileSystemInfo.Exists && DateTimeOffset.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileSystemInfo) <= MovieDbProviderBase.CacheTime ? _jsonSerializer.DeserializeFromFileAsync<CompleteMovieData>(fileSystemInfo.FullName) : DownloadMovieInfo(tmdbId, language, country, cancellationToken);
        }

        internal string GetDataFilePath(string tmdbId, string preferredLanguage)
        {
            if (string.IsNullOrEmpty(tmdbId))
                throw new ArgumentNullException(nameof(tmdbId));
            string movieDataPath = GetMovieDataPath(_configurationManager.ApplicationPaths, tmdbId);
            if (string.IsNullOrEmpty(preferredLanguage))
                preferredLanguage = "alllang";
            string path2 = string.Format("all-{0}.json", preferredLanguage);
            return Path.Combine(movieDataPath, path2);
        }

        public static string AddImageLanguageParam(
          string url,
          string preferredLanguage,
          string preferredCountry)
        {
            string imageLanguagesParam = GetImageLanguagesParam(preferredLanguage, preferredCountry);
            if (!string.IsNullOrEmpty(imageLanguagesParam))
                url = url + "&include_image_language=" + imageLanguagesParam;
            return url;
        }

        public static string GetImageLanguagesParam(string preferredLanguage, string preferredCountry)
        {
            List<string> source = new List<string>();
            if (!string.IsNullOrEmpty(preferredLanguage))
            {
                preferredLanguage = NormalizeLanguage(preferredLanguage, preferredCountry);
                source.Add(preferredLanguage);
                if (preferredLanguage.Length == 5)
                    source.Add(preferredLanguage.Substring(0, 2));
                source.Add("null");
                if (!string.Equals(preferredLanguage, "en", StringComparison.OrdinalIgnoreCase))
                    source.Add("en");
            }
            return string.Join(",", source.ToArray(source.Count));
        }

        public static string NormalizeLanguage(string language, string country)
        {
            if (!string.IsNullOrEmpty(language))
            {
                if (string.Equals(language, "es", StringComparison.OrdinalIgnoreCase) && string.Equals(country, "mx", StringComparison.OrdinalIgnoreCase))
                    return "es-MX";
                string[] strArray = language.Split('-');
                if (strArray.Length == 2)
                    language = strArray[0] + "-" + strArray[1].ToUpper();
            }
            return language;
        }

        internal async Task<CompleteMovieData> FetchMainResult(
          string id,
          bool isTmdbId,
          string language,
          string country,
          CancellationToken cancellationToken)
        {
            MovieDbProvider movieDbProvider = this;
            string url = string.Format("https://api.themoviedb.org/3/movie/{0}?api_key={1}&append_to_response=alternative_titles,reviews,casts,releases,images,keywords,trailers", (object)id, (object)MovieDbProvider.ApiKey);
            if (!string.IsNullOrEmpty(language))
                url += string.Format("&language={0}", (object)MovieDbProvider.NormalizeLanguage(language, country));
            string str1 = MovieDbProvider.AddImageLanguageParam(url, language, country);
            cancellationToken.ThrowIfCancellationRequested();
            CacheMode cacheMode = isTmdbId ? CacheMode.None : CacheMode.Unconditional;
            TimeSpan cacheLength = MovieDbProviderBase.CacheTime;
            CompleteMovieData mainResult;
            HttpResponseInfo? response;
            Stream? json;
            try
            {
                response = await movieDbProvider.GetMovieDbResponse(new HttpRequestOptions()
                {
                    Url = str1,
                    CancellationToken = cancellationToken,
                    AcceptHeader = MovieDbProvider.AcceptHeader,
                    CacheMode = cacheMode,
                    CacheLength = cacheLength
                }).ConfigureAwait(false);
                try
                {
                    json = response.Content;
                    try
                    {
                        mainResult = await movieDbProvider._jsonSerializer.DeserializeFromStreamAsync<CompleteMovieData>(json).ConfigureAwait(false);
                    }
                    finally
                    {
                        json?.Dispose();
                    }
                    json = null;
                }
                finally
                {
                    response?.Dispose();
                }
                response = null;
            }
            catch (HttpException ex)
            {
                if (ex.StatusCode.HasValue && ex.StatusCode.Value == HttpStatusCode.NotFound)
                    return null;
                throw;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (mainResult != null && !string.IsNullOrEmpty(language) && !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
            {
                string str2 = AddImageLanguageParam(string.Format("https://api.themoviedb.org/3/movie/{0}?api_key={1}&append_to_response=alternative_titles,reviews,casts,releases,images,keywords,trailers", (object)id, (object)MovieDbProvider.ApiKey) + "&language=en", language, country);
                response = await movieDbProvider.GetMovieDbResponse(new HttpRequestOptions()
                {
                    Url = str2,
                    CancellationToken = cancellationToken,
                    AcceptHeader = AcceptHeader,
                    CacheMode = cacheMode,
                    CacheLength = cacheLength
                }).ConfigureAwait(false);
                try
                {
                    json = response.Content;
                    try
                    {
                        CompleteMovieData completeMovieData = await movieDbProvider._jsonSerializer.DeserializeFromStreamAsync<CompleteMovieData>(json).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(mainResult.overview))
                            mainResult.overview = completeMovieData.overview;
                        if (mainResult.trailers != null && mainResult.trailers.youtube != null)
                        {
                            if (mainResult.trailers.youtube.Count != 0)
                                goto label_32;
                        }
                        mainResult.trailers = completeMovieData.trailers;
                    }
                    finally
                    {
                        json?.Dispose();
                    }
                label_32:
                    json = null;
                }
                finally
                {
                    response?.Dispose();
                }
            }
            return mainResult;
        }

        internal async Task<HttpResponseInfo> GetMovieDbResponse(HttpRequestOptions options)
        {
            long num = Math.Min(((requestIntervalMs * 10000) - (DateTimeOffset.UtcNow.Ticks - _lastRequestTicks)) / 10000L, requestIntervalMs);
            if (num > 0L)
            {
                _logger.Debug("Throttling Tmdb by {0} ms", num);
                await Task.Delay(Convert.ToInt32(num)).ConfigureAwait(false);
            }
            _lastRequestTicks = DateTimeOffset.UtcNow.Ticks;
            options.BufferContent = true;
            options.UserAgent = "Emby/" + _appHost.ApplicationVersion?.ToString();
            return await _httpClient.SendAsync(options, "GET").ConfigureAwait(false);
        }

        public int Order => 1;

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken) => _httpClient.GetResponse(new HttpRequestOptions()
        {
            CancellationToken = cancellationToken,
            Url = url
        });
    }
}
