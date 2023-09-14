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
using System.Text.RegularExpressions;

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

        protected readonly Regex _rgx;

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
            ILocalizationManager localization,
            ILibraryManager libraryManager,
            IApplicationHost appHost)
        {
            _jsonSerializer = jsonSerializer;
            _httpClient = httpClient;
            _fileSystem = fileSystem;
            _configurationManager = configurationManager;
            _localization = localization;
            _libraryManager = libraryManager;
            _appHost = appHost;
            _rgx = new Regex(@"[\?&](([^&=]+)=([^&=#]*))");
            Current = this;
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
        {
            EntryPoint.Current.LogCall();
            return GetMovieSearchResults(searchInfo, cancellationToken);
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetMovieSearchResults(ItemLookupInfo searchInfo, CancellationToken cancellationToken)
        {
            EntryPoint.Current.LogCall();
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
                RemoteSearchResult remoteSearchResult = await new MovieDbSearch(_jsonSerializer, _libraryManager).FindMovieByExternalId(providerId2, "imdb_id", MetadataProviders.Imdb.ToString(), cancellationToken).ConfigureAwait(false);
                if (remoteSearchResult != null)
                    return new RemoteSearchResult[1]
                    {
                        remoteSearchResult
                    };
            }
            return await new MovieDbSearch(_jsonSerializer, _libraryManager).GetMovieSearchResults(searchInfo, cancellationToken).ConfigureAwait(false);
        }
        public Task<MetadataResult<Movie>> GetMetadata(
            MovieInfo info,
            CancellationToken cancellationToken)
        {
            EntryPoint.Current.LogCall();
            return GetItemMetadata<Movie>(info, cancellationToken);
        }

        public Task<MetadataResult<T>> GetItemMetadata<T>(
          ItemLookupInfo id,
          CancellationToken cancellationToken)
          where T : BaseItem, new()
        {
            EntryPoint.Current.LogCall();
            return new GenericMovieDbInfo<T>(_jsonSerializer, _libraryManager, _fileSystem).GetMetadata(id, cancellationToken);
        }

        internal async Task<TmdbSettingsResult> GetTmdbSettings(CancellationToken cancellationToken)
        {
            EntryPoint.Current.LogCall();
            if (_tmdbSettings != null)
                return _tmdbSettings;            
            using (HttpResponseInfo response = await GetMovieDbResponse(new HttpRequestOptions()
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
                        EntryPoint.Current.Log(this, LogSeverity.Info, "MovieDb settings: {0}", text);
                        _tmdbSettings = _jsonSerializer.DeserializeFromString<TmdbSettingsResult>(text);                        
                        return _tmdbSettings;
                    }
                }
            }
        }

        internal static string GetMovieDataPath(IApplicationPaths appPaths, string tmdbId) => Path.Combine(GetMoviesDataPath(appPaths), tmdbId);

        internal static string GetMoviesDataPath(IApplicationPaths appPaths) => Path.Combine(appPaths.CachePath, "tmdb-movies2");

        internal async Task<CompleteMovieData> DownloadMovieInfo(
          string id,
          string preferredMetadataLanguage,
          string preferredMetadataCountry,
          CancellationToken cancellationToken)
        {
            EntryPoint.Current.LogCall();
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
            EntryPoint.Current.LogCall();
            if (string.IsNullOrEmpty(tmdbId))
                throw new ArgumentNullException(nameof(tmdbId));
            FileSystemMetadata fileSystemInfo = _fileSystem.GetFileSystemInfo(GetDataFilePath(tmdbId, language));
            return fileSystemInfo.Exists && DateTimeOffset.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileSystemInfo) <= MovieDbProviderBase.CacheTime ? _jsonSerializer.DeserializeFromFileAsync<CompleteMovieData>(fileSystemInfo.FullName) : DownloadMovieInfo(tmdbId, language, country, cancellationToken);
        }

        internal string GetDataFilePath(string tmdbId, string preferredLanguage)
        {
            EntryPoint.Current.LogCall();
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
            EntryPoint.Current.LogCall();
            MovieDbProvider movieDbProvider = this;
            string url = string.Format("https://api.themoviedb.org/3/movie/{0}?api_key={1}&append_to_response=alternative_titles,reviews,casts,releases,images,keywords,trailers", (object)id, (object)ApiKey);
            if (!string.IsNullOrEmpty(language))
                url += string.Format("&language={0}", (object)NormalizeLanguage(language, country));
            string str1 = AddImageLanguageParam(url, language, country);
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
                    AcceptHeader = AcceptHeader,
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
                string str2 = AddImageLanguageParam(string.Format("https://api.themoviedb.org/3/movie/{0}?api_key={1}&append_to_response=alternative_titles,reviews,casts,releases,images,keywords,trailers", (object)id, (object)ApiKey) + "&language=en", language, country);
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
            EntryPoint.Current.LogCall();
            long num = Math.Min(((requestIntervalMs * 10000) - (DateTimeOffset.UtcNow.Ticks - _lastRequestTicks)) / 10000L, requestIntervalMs);
            if (num > 0L)
            {
                EntryPoint.Current.Log(this, LogSeverity.Info, "Throttling Tmdb by {0} ms", num);
                await Task.Delay(Convert.ToInt32(num)).ConfigureAwait(false);
            }
            _lastRequestTicks = DateTimeOffset.UtcNow.Ticks;
            options.BufferContent = true;
            options.UserAgent = "Emby/" + _appHost.ApplicationVersion?.ToString();
            return await _httpClient.SendAsync(options, "GET").ConfigureAwait(false);
        }

        public int Order => 1;

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            EntryPoint.Current.LogCall();
            EntryPoint.Current.Log(url);
            if (url.StartsWith("/emby"))
            {
                url = _tmdbSettings.images.GetImageUrl("original") + _rgx.Match(url).Groups[3].Value;
            }
            else if (url.StartsWith("/"))
            {
                url = _tmdbSettings.images.GetImageUrl("original") + url;
            }
            EntryPoint.Current.Log(url);
            return _httpClient.GetResponse(new HttpRequestOptions()
            {
                CancellationToken = cancellationToken,
                Url = url
            });
        }

        internal class TmdbTitle
        {
            public string iso_3166_1 { get; set; }

            public string title { get; set; }
        }

        internal class TmdbAltTitleResults
        {
            public int id { get; set; }

            public List<TmdbTitle> titles { get; set; }
        }

        public class BelongsToCollection
        {
            public int id { get; set; }

            public string name { get; set; }

            public string poster_path { get; set; }

            public string backdrop_path { get; set; }
        }

        public class ProductionCompany
        {
            public int id { get; set; }

            public string logo_path { get; set; }

            public string name { get; set; }

            public string origin_country { get; set; }
        }

        public class ProductionCountry
        {
            public string iso_3166_1 { get; set; }

            public string name { get; set; }
        }

        public class SpokenLanguage
        {
            public string english_name { get; set; }

            public string iso_639_1 { get; set; }

            public string name { get; set; }
        }

        public class Casts
        {
            public List<TmdbCast> cast { get; set; }

            public List<TmdbCrew> crew { get; set; }
        }

        public class Country
        {
            public string certification { get; set; }

            public string iso_3166_1 { get; set; }

            public bool primary { get; set; }

            public DateTimeOffset release_date { get; set; }

            public string GetRating() => Country.GetRating(this.certification, this.iso_3166_1);

            public static string GetRating(string rating, string iso_3166_1)
            {
                if (string.IsNullOrEmpty(rating))
                    return (string)null;
                if (string.Equals(iso_3166_1, "us", StringComparison.OrdinalIgnoreCase))
                    return rating;
                if (string.Equals(iso_3166_1, "de", StringComparison.OrdinalIgnoreCase))
                    iso_3166_1 = "FSK";
                return iso_3166_1 + "-" + rating;
            }
        }

        public class Releases
        {
            public List<Country> countries { get; set; }
        }

        public class Images
        {
            public List<TmdbImage> backdrops { get; set; }

            public List<TmdbImage> posters { get; set; }

            public List<TmdbImage> logos { get; set; }
        }

        public class Youtube
        {
            public string name { get; set; }

            public string size { get; set; }

            public string source { get; set; }

            public string type { get; set; }
        }

        public class Trailers
        {
            public List<object> quicktime { get; set; }

            public List<Youtube> youtube { get; set; }
        }

        internal class CompleteMovieData
        {
            public bool adult { get; set; }

            public string backdrop_path { get; set; }

            public BelongsToCollection belongs_to_collection { get; set; }

            public int budget { get; set; }

            public List<TmdbGenre> genres { get; set; }

            public string homepage { get; set; }

            public int id { get; set; }

            public string imdb_id { get; set; }

            public string original_language { get; set; }

            public string original_title { get; set; }

            public string overview { get; set; }

            public double popularity { get; set; }

            public string poster_path { get; set; }

            public List<ProductionCompany> production_companies { get; set; }

            public List<ProductionCountry> production_countries { get; set; }

            public string release_date { get; set; }

            public int revenue { get; set; }

            public int runtime { get; set; }

            public List<SpokenLanguage> spoken_languages { get; set; }

            public string status { get; set; }

            public string tagline { get; set; }

            public string title { get; set; }

            public bool video { get; set; }

            public double vote_average { get; set; }

            public int vote_count { get; set; }

            public Casts casts { get; set; }

            public Releases releases { get; set; }

            public Images images { get; set; }

            public TmdbKeywords keywords { get; set; }

            public Trailers trailers { get; set; }

            public string name { get; set; }

            public string original_name { get; set; }

            public string GetOriginalTitle() => this.original_name ?? this.original_title;

            public string GetTitle() => this.name ?? this.title ?? this.GetOriginalTitle();
        }
    }
}
