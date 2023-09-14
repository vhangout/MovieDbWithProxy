using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
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
using System.Runtime.CompilerServices;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace MovieDbWithProxy
{
    public class MovieDbSeriesProvider :
      IRemoteMetadataProvider<Series, SeriesInfo>,
      IMetadataProvider<Series>,
      IMetadataProvider,
      IRemoteMetadataProvider,
      IRemoteSearchProvider<SeriesInfo>,
      IRemoteSearchProvider,
      IHasOrder,
      IHasMetadataFeatures
    {
        public string Name => Plugin.ProviderName;

        private const string GetTvInfo3 = "https://api.themoviedb.org/3/tv/{0}?api_key={1}&append_to_response=alternative_titles,reviews,credits,images,keywords,external_ids,videos,content_ratings";
        private readonly CultureInfo _usCulture = new CultureInfo("en-US");
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IFileSystem _fileSystem;
        private readonly IServerConfigurationManager _configurationManager;
        private readonly ILocalizationManager _localization;
        private readonly IHttpClient _httpClient;
        private readonly ILibraryManager _libraryManager;

        internal static MovieDbSeriesProvider Current { get; private set; }

        public MovieDbSeriesProvider(
          IJsonSerializer jsonSerializer,
          IFileSystem fileSystem,
          IServerConfigurationManager configurationManager,
          ILocalizationManager localization,
          IHttpClient httpClient,
          ILibraryManager libraryManager)
        {
            _jsonSerializer = jsonSerializer;
            _fileSystem = fileSystem;
            _configurationManager = configurationManager;
            _localization = localization;
            _httpClient = httpClient;
            _libraryManager = libraryManager;
            Current = this;
        }

        public MetadataFeatures[] Features => new MetadataFeatures[1]
        {
      MetadataFeatures.Adult
        };

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
          SeriesInfo searchInfo,
          CancellationToken cancellationToken)
        {
            MovieDbSeriesProvider dbSeriesProvider1 = this;
            string providerId1 = ProviderIdsExtensions.GetProviderId(searchInfo, MetadataProviders.Tmdb);
            if (!string.IsNullOrEmpty(providerId1))
            {
                RootObject obj = await dbSeriesProvider1.EnsureSeriesInfo(providerId1, searchInfo.MetadataLanguage, searchInfo.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
                if (obj != null)
                {
                    string imageUrl = (await MovieDbProvider.Current.GetTmdbSettings(cancellationToken).ConfigureAwait(false)).images.GetImageUrl("original");
                    // ISSUE: explicit non-virtual call
                    RemoteSearchResult remoteSearchResult = new RemoteSearchResult()
                    {
                        Name = obj.name,
                        SearchProviderName = dbSeriesProvider1.Name, //__nonvirtual ??
                        ImageUrl = string.IsNullOrWhiteSpace(obj.poster_path) ? null : imageUrl + obj.poster_path
                    };
                    ProviderIdsExtensions.SetProviderId(remoteSearchResult, MetadataProviders.Tmdb, obj.id.ToString(dbSeriesProvider1._usCulture));
                    ProviderIdsExtensions.SetProviderId(remoteSearchResult, MetadataProviders.Imdb, obj.external_ids.imdb_id);
                    if (obj.external_ids.tvdb_id > 0)
                        ProviderIdsExtensions.SetProviderId(remoteSearchResult, MetadataProviders.Tvdb, obj.external_ids.tvdb_id.ToString(dbSeriesProvider1._usCulture));
                    return new RemoteSearchResult[1]
          {
            remoteSearchResult
          };
                }
                obj = null;
            }
            string providerId2 = ProviderIdsExtensions.GetProviderId(searchInfo, MetadataProviders.Imdb);
            MetadataProviders metadataProviders;
            if (!string.IsNullOrEmpty(providerId2))
            {
                MovieDbSeriesProvider dbSeriesProvider2 = dbSeriesProvider1;
                string id = providerId2;
                metadataProviders = MetadataProviders.Imdb;
                string providerIdKey = metadataProviders.ToString();
                CancellationToken cancellationToken1 = cancellationToken;
                RemoteSearchResult remoteSearchResult = await dbSeriesProvider2.FindByExternalId(id, "imdb_id", providerIdKey, cancellationToken1).ConfigureAwait(false);
                if (remoteSearchResult != null)
                    return new RemoteSearchResult[1]
                    {
            remoteSearchResult
                    };
            }
            string providerId3 = ProviderIdsExtensions.GetProviderId(searchInfo, MetadataProviders.Tvdb);
            if (!string.IsNullOrEmpty(providerId3))
            {
                MovieDbSeriesProvider dbSeriesProvider3 = dbSeriesProvider1;
                string id = providerId3;
                metadataProviders = MetadataProviders.Tvdb;
                string providerIdKey = metadataProviders.ToString();
                CancellationToken cancellationToken2 = cancellationToken;
                RemoteSearchResult remoteSearchResult = await dbSeriesProvider3.FindByExternalId(id, "tvdb_id", providerIdKey, cancellationToken2).ConfigureAwait(false);
                if (remoteSearchResult != null)
                    return new RemoteSearchResult[1]
          {
            remoteSearchResult
          };
            }
            ConfiguredTaskAwaitable<List<RemoteSearchResult>> configuredTaskAwaitable = new MovieDbSearch(dbSeriesProvider1._jsonSerializer, dbSeriesProvider1._libraryManager).GetSearchResults(searchInfo, cancellationToken).ConfigureAwait(false);
            List<RemoteSearchResult> results = await configuredTaskAwaitable;
            configuredTaskAwaitable = dbSeriesProvider1.FilterSearchResults(results, searchInfo, searchInfo.MetadataLanguage, searchInfo.MetadataCountryCode, true, cancellationToken).ConfigureAwait(false);
            return await configuredTaskAwaitable;
        }

        private async Task<List<RemoteSearchResult>> FilterSearchResults(
          List<RemoteSearchResult> results,
          SeriesInfo searchInfo,
          string language,
          string country,
          bool foundByName,
          CancellationToken cancellationToken)
        {
            DateTimeOffset? episodeAirDate = searchInfo.EpisodeAirDate;
            if (episodeAirDate.HasValue & foundByName)
            {
                List<RemoteSearchResult> list = new List<RemoteSearchResult>();
                foreach (RemoteSearchResult item in results)
                {
                    if (await AiredWithin(item, episodeAirDate.Value, language, country, cancellationToken).ConfigureAwait(false))
                        list.Add(item);
                }
                results = list;
            }
            return results;
        }

        private async Task<bool> AiredWithin(
          RemoteSearchResult remoteSearchResult,
          DateTimeOffset episodeAirDate,
          string language,
          string country,
          CancellationToken cancellationToken)
        {
            EntryPoint.Current.Log(this, LogSeverity.Info, "Checking AiredWithin for {0}. episodeAirDate: {1}", new object[2] { remoteSearchResult.Name, episodeAirDate.UtcDateTime.ToShortDateString() });

            if (!remoteSearchResult.PremiereDate.HasValue || episodeAirDate.Year < remoteSearchResult.PremiereDate.GetValueOrDefault().Year)
                return false;

            SeriesInfo seriesInfo = new SeriesInfo()
            {
                ProviderIds = remoteSearchResult.ProviderIds,
                MetadataLanguage = language,
                MetadataCountryCode = country,
                Name = remoteSearchResult.Name,
                Year = remoteSearchResult.ProductionYear,
                PremiereDate = remoteSearchResult.PremiereDate
            };

            MetadataResult<Series> metadataResult = await GetMetadata(seriesInfo, cancellationToken).ConfigureAwait(false);
            if (!metadataResult.HasMetadata)
                return false;

            object[] objArray = new object[3] {
                seriesInfo.Name,
                null,
                null
            };

            if (metadataResult.Item.PremiereDate != null)
            {
                objArray[1] = metadataResult.Item.PremiereDate.Value.UtcDateTime.ToShortDateString();
            }

            if (metadataResult.Item.EndDate != null)
            {
                objArray[2] = metadataResult.Item.EndDate.Value.UtcDateTime.ToShortDateString();
            }
            EntryPoint.Current.Log(this, LogSeverity.Info, "AiredWithin for {0} Item.PremiereDate: {1}, Item.EndDate: {2}", objArray);

            if (metadataResult.Item.PremiereDate == null)
                return false;

            int episodeAirDateYear = episodeAirDate.Year;
            int premiereDateYear = metadataResult.Item.PremiereDate.GetValueOrDefault().Year; //year or 1
            int endDateYear = metadataResult.Item.EndDate.GetValueOrDefault().Year; //year or 1

            return episodeAirDateYear > premiereDateYear && episodeAirDateYear < endDateYear;
        }

        public async Task<MetadataResult<Series>> GetMetadata(
          SeriesInfo info,
          CancellationToken cancellationToken)
        {
            MovieDbSeriesProvider dbSeriesProvider1 = this;
            MetadataResult<Series> result = new MetadataResult<Series>();
            result.QueriedById = true;
            string tmdbId = ProviderIdsExtensions.GetProviderId(info, MetadataProviders.Tmdb);
            MetadataProviders metadataProviders;
            ConfiguredTaskAwaitable<RemoteSearchResult> configuredTaskAwaitable;
            if (string.IsNullOrEmpty(tmdbId))
            {
                string providerId = ProviderIdsExtensions.GetProviderId(info, MetadataProviders.Imdb);
                if (!string.IsNullOrEmpty(providerId))
                {
                    MovieDbSeriesProvider dbSeriesProvider2 = dbSeriesProvider1;
                    string id = providerId;
                    metadataProviders = MetadataProviders.Imdb;
                    string providerIdKey = metadataProviders.ToString();
                    CancellationToken cancellationToken1 = cancellationToken;
                    configuredTaskAwaitable = dbSeriesProvider2.FindByExternalId(id, "imdb_id", providerIdKey, cancellationToken1).ConfigureAwait(false);
                    RemoteSearchResult remoteSearchResult = await configuredTaskAwaitable;
                    if (remoteSearchResult != null)
                        tmdbId = ProviderIdsExtensions.GetProviderId(remoteSearchResult, MetadataProviders.Tmdb);
                }
            }
            if (string.IsNullOrEmpty(tmdbId))
            {
                string providerId = ProviderIdsExtensions.GetProviderId(info, MetadataProviders.Tvdb);
                if (!string.IsNullOrEmpty(providerId))
                {
                    MovieDbSeriesProvider dbSeriesProvider3 = dbSeriesProvider1;
                    string id = providerId;
                    metadataProviders = MetadataProviders.Tvdb;
                    string providerIdKey = metadataProviders.ToString();
                    CancellationToken cancellationToken2 = cancellationToken;
                    configuredTaskAwaitable = dbSeriesProvider3.FindByExternalId(id, "tvdb_id", providerIdKey, cancellationToken2).ConfigureAwait(false);
                    RemoteSearchResult remoteSearchResult = await configuredTaskAwaitable;
                    if (remoteSearchResult != null)
                        tmdbId = ProviderIdsExtensions.GetProviderId(remoteSearchResult, MetadataProviders.Tmdb);
                }
            }
            if (string.IsNullOrEmpty(tmdbId))
            {
                result.QueriedById = false;
                RemoteSearchResult remoteSearchResult = (await new MovieDbSearch(dbSeriesProvider1._jsonSerializer, dbSeriesProvider1._libraryManager).GetSearchResults(info, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
                if (remoteSearchResult != null)
                    tmdbId = ProviderIdsExtensions.GetProviderId(remoteSearchResult, MetadataProviders.Tmdb);
            }
            if (!string.IsNullOrEmpty(tmdbId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = await dbSeriesProvider1.FetchMovieData(tmdbId, info.MetadataLanguage, info.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
                result.HasMetadata = result.Item != null;
            }
            MetadataResult<Series> metadata = result;
            return metadata;
        }

        private async Task<MetadataResult<Series>> FetchMovieData(
          string tmdbId,
          string language,
          string preferredCountryCode,
          CancellationToken cancellationToken)
        {
            MetadataResult<Series> result = new MetadataResult<Series>();
            RootObject seriesInfo = await EnsureSeriesInfo(tmdbId, language, preferredCountryCode, cancellationToken).ConfigureAwait(false);
            if (seriesInfo == null)
                return result;
            result.Item = new Series();
            result.ResultLanguage = seriesInfo.ResultLanguage;
            TmdbSettingsResult settings = await MovieDbProvider.Current.GetTmdbSettings(cancellationToken).ConfigureAwait(false);
            ProcessMainInfo(result, seriesInfo, preferredCountryCode, settings);
            return result;
        }

        private void ProcessMainInfo(
          MetadataResult<Series> seriesResult,
          RootObject seriesInfo,
          string preferredCountryCode,
          TmdbSettingsResult settings)
        {
            Series series = seriesResult.Item;
            series.Name = seriesInfo.name;
            series.OriginalTitle = seriesInfo.GetOriginalTitle();
            ProviderIdsExtensions.SetProviderId(series, MetadataProviders.Tmdb, seriesInfo.id.ToString(_usCulture));
            float result;
            if (float.TryParse(seriesInfo.vote_average.ToString(CultureInfo.InvariantCulture), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result))
                series.CommunityRating = new float?(result);
            series.Overview = seriesInfo.overview;
            if (seriesInfo.networks != null)
                series.SetStudios(seriesInfo.networks.Select(i => i.name));
            if (seriesInfo.genres != null)
                series.SetGenres(seriesInfo.genres.Select(i => i.name));
            series.RunTimeTicks = new long?(seriesInfo.episode_run_time.Select(i => TimeSpan.FromMinutes(i).Ticks).FirstOrDefault());
            if (string.Equals(seriesInfo.status, "Ended", StringComparison.OrdinalIgnoreCase) || string.Equals(seriesInfo.status, "Cancelled", StringComparison.OrdinalIgnoreCase) || string.Equals(seriesInfo.status, "Canceled", StringComparison.OrdinalIgnoreCase))
            {
                series.Status = new SeriesStatus?((SeriesStatus)2);
                series.EndDate = new DateTimeOffset?(seriesInfo.last_air_date);
            }
            else
                series.Status = new SeriesStatus?((SeriesStatus)1);
            series.PremiereDate = new DateTimeOffset?(seriesInfo.first_air_date);
            TmdbExternalIds externalIds = seriesInfo.external_ids;
            if (externalIds != null)
            {
                if (!string.IsNullOrWhiteSpace(externalIds.imdb_id))
                    ProviderIdsExtensions.SetProviderId(series, MetadataProviders.Imdb, externalIds.imdb_id);
                if (externalIds.tvrage_id > 0)
                    ProviderIdsExtensions.SetProviderId(series, MetadataProviders.TvRage, externalIds.tvrage_id.ToString(_usCulture));
                if (externalIds.tvdb_id > 0)
                    ProviderIdsExtensions.SetProviderId(series, MetadataProviders.Tvdb, externalIds.tvdb_id.ToString(_usCulture));
            }
            List<ContentRating> source = (seriesInfo.content_ratings ?? new ContentRatings()).results ?? new List<ContentRating>();
            ContentRating contentRating1 = source.FirstOrDefault(c => string.Equals(c.iso_3166_1, preferredCountryCode, StringComparison.OrdinalIgnoreCase));
            ContentRating contentRating2 = source.FirstOrDefault(c => string.Equals(c.iso_3166_1, "US", StringComparison.OrdinalIgnoreCase));
            ContentRating contentRating3 = source.FirstOrDefault();
            if (contentRating1 != null)
                series.OfficialRating = contentRating1.GetRating();
            else if (contentRating2 != null)
                series.OfficialRating = contentRating2.GetRating();
            else if (contentRating3 != null)
                series.OfficialRating = contentRating3.GetRating();
            foreach (Video trailer in GetTrailers(seriesInfo))
            {
                string str = string.Format("http://www.youtube.com/watch?v={0}", trailer.key);
                Extensions.AddTrailerUrl(series, str);
            }
            seriesResult.ResetPeople();
            string imageUrl = settings.images.GetImageUrl("original");
            if (seriesInfo.credits == null || seriesInfo.credits.cast == null)
                return;
            foreach (TmdbCast tmdbCast in (IEnumerable<TmdbCast>)seriesInfo.credits.cast.OrderBy(a => a.order))
            {
                PersonInfo personInfo = new PersonInfo()
                {
                    Name = tmdbCast.name.Trim(),
                    Role = tmdbCast.character,
                    Type = 0
                };
                if (!string.IsNullOrWhiteSpace(tmdbCast.profile_path))
                    personInfo.ImageUrl = imageUrl + tmdbCast.profile_path;
                if (tmdbCast.id > 0)
                    ProviderIdsExtensions.SetProviderId(personInfo, MetadataProviders.Tmdb, tmdbCast.id.ToString(CultureInfo.InvariantCulture));
                seriesResult.AddPerson(personInfo);
            }
        }

        private List<Video> GetTrailers(
          RootObject seriesInfo)
        {
            List<Video> trailers = new List<Video>();
            if (seriesInfo.videos != null && seriesInfo.videos.results != null)
            {
                foreach (Video result in seriesInfo.videos.results)
                {
                    if (string.Equals(result.type, "trailer", StringComparison.OrdinalIgnoreCase) && string.Equals(result.site, "youtube", StringComparison.OrdinalIgnoreCase))
                        trailers.Add(result);
                }
            }
            return trailers;
        }

        internal static string GetSeriesDataPath(IApplicationPaths appPaths, string tmdbId) => Path.Combine(GetSeriesDataPath(appPaths), tmdbId);

        internal static string GetSeriesDataPath(IApplicationPaths appPaths) => Path.Combine(appPaths.CachePath, "tmdb-tv");

        internal async Task<RootObject> DownloadSeriesInfo(
          string id,
          string preferredMetadataLanguage,
          string preferredMetadataCountry,
          CancellationToken cancellationToken)
        {
            RootObject rootObject = await FetchMainResult(id, preferredMetadataLanguage, preferredMetadataCountry, cancellationToken).ConfigureAwait(false);
            if (rootObject == null)
                return null;
            string dataFilePath = GetDataFilePath(id, preferredMetadataLanguage);
            _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(dataFilePath));
            _jsonSerializer.SerializeToFile(rootObject, dataFilePath);
            return rootObject;
        }

        internal async Task<RootObject> FetchMainResult(
          string id,
          string language,
          string country,
          CancellationToken cancellationToken)
        {
            string url = string.Format("https://api.themoviedb.org/3/tv/{0}?api_key={1}&append_to_response=alternative_titles,reviews,credits,images,keywords,external_ids,videos,content_ratings", id, MovieDbProvider.ApiKey);
            if (!string.IsNullOrEmpty(language))
                url += string.Format("&language={0}", MovieDbProvider.NormalizeLanguage(language, country));
            string str1 = MovieDbProvider.AddImageLanguageParam(url, language, country);
            cancellationToken.ThrowIfCancellationRequested();
            RootObject mainResult;
            HttpResponseInfo response;
            Stream json;
            try
            {
                response = await MovieDbProvider.Current.GetMovieDbResponse(new HttpRequestOptions()
                {
                    Url = str1,
                    CancellationToken = cancellationToken,
                    AcceptHeader = MovieDbProvider.AcceptHeader
                }).ConfigureAwait(false);
                try
                {
                    json = response.Content;
                    try
                    {
                        mainResult = await _jsonSerializer.DeserializeFromStreamAsync<RootObject>(json).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(language))
                            mainResult.ResultLanguage = language;
                    }
                    finally
                    {
                        json?.Dispose();
                    }
                    json = null;
                }
                finally
                {
                    ((IDisposable)response)?.Dispose();
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
            if (mainResult != null && !string.IsNullOrEmpty(language) && !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) && (string.IsNullOrEmpty(mainResult.overview) || GetTrailers(mainResult).Count == 0))
            {
                EntryPoint.Current.Log(this, LogSeverity.Info, "MovieDbSeriesProvider is incomplete for language " + language + ". Trying English...", Array.Empty<object>());
                string str2 = MovieDbProvider.AddImageLanguageParam(string.Format("https://api.themoviedb.org/3/tv/{0}?api_key={1}&append_to_response=alternative_titles,reviews,credits,images,keywords,external_ids,videos,content_ratings", (object)id, (object)MovieDbProvider.ApiKey) + "&language=en", language, country);
                response = await MovieDbProvider.Current.GetMovieDbResponse(new HttpRequestOptions()
                {
                    Url = str2,
                    CancellationToken = cancellationToken,
                    AcceptHeader = MovieDbProvider.AcceptHeader
                }).ConfigureAwait(false);
                try
                {
                    json = response.Content;
                    try
                    {
                        RootObject rootObject = await _jsonSerializer.DeserializeFromStreamAsync<RootObject>(json).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(mainResult.overview))
                            mainResult.overview = rootObject.overview;
                        if (GetTrailers(mainResult).Count == 0)
                            mainResult.videos = rootObject.videos;
                        mainResult.ResultLanguage = "en";
                    }
                    finally
                    {
                        json?.Dispose();
                    }
                    json = null;
                }
                finally
                {
                    ((IDisposable)response)?.Dispose();
                }
                response = null;
            }
            return mainResult;
        }

        internal Task<RootObject> EnsureSeriesInfo(
          string tmdbId,
          string language,
          string country,
          CancellationToken cancellationToken)
        {
            string str = !string.IsNullOrEmpty(tmdbId) ? GetDataFilePath(tmdbId, language) : throw new ArgumentNullException(nameof(tmdbId));
            FileSystemMetadata fileSystemInfo = _fileSystem.GetFileSystemInfo(str);
            return fileSystemInfo.Exists && DateTimeOffset.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileSystemInfo) <= MovieDbProviderBase.CacheTime ? _jsonSerializer.DeserializeFromFileAsync<RootObject>(str) : DownloadSeriesInfo(tmdbId, language, country, cancellationToken);
        }

        internal string GetDataFilePath(string tmdbId, string preferredLanguage)
        {
            if (string.IsNullOrEmpty(tmdbId))
                throw new ArgumentNullException(nameof(tmdbId));
            if (string.IsNullOrEmpty(preferredLanguage))
                preferredLanguage = "alllang";
            return Path.Combine(GetSeriesDataPath(_configurationManager.ApplicationPaths, tmdbId), string.Format("series-{0}.json", preferredLanguage));
        }

        private async Task<RemoteSearchResult> FindByExternalId(
          string id,
          string externalSource,
          string providerIdKey,
          CancellationToken cancellationToken)
        {
            string str = string.Format("https://api.themoviedb.org/3/find/{0}?api_key={1}&external_source={2}", id, MovieDbProvider.ApiKey, externalSource);
            MovieDbProvider current = MovieDbProvider.Current;
            using (HttpResponseInfo response = await current.GetMovieDbResponse(new HttpRequestOptions()
            {
                Url = str,
                CancellationToken = cancellationToken,
                AcceptHeader = MovieDbProvider.AcceptHeader
            }).ConfigureAwait(false))
            {
                using (Stream json = response.Content)
                {
                    MovieDbSearch.ExternalIdLookupResult externalIdLookupResult = await _jsonSerializer.DeserializeFromStreamAsync<MovieDbSearch.ExternalIdLookupResult>(json).ConfigureAwait(false);
                    if (externalIdLookupResult != null)
                    {
                        if (externalIdLookupResult.tv_results != null)
                        {
                            MovieDbSearch.TvResult tv = externalIdLookupResult.tv_results.FirstOrDefault();
                            if (tv != null)
                            {
                                string imageUrl = (await MovieDbProvider.Current.GetTmdbSettings(cancellationToken).ConfigureAwait(false)).images.GetImageUrl("original");
                                RemoteSearchResult byExternalId = new RemoteSearchResult();
                                byExternalId.Name = tv.name;
                                byExternalId.SearchProviderName = Name;
                                byExternalId.ImageUrl = string.IsNullOrWhiteSpace(tv.poster_path) ? null : imageUrl + tv.poster_path;
                                ProviderIdsExtensions.SetProviderId(byExternalId, MetadataProviders.Tmdb, tv.id.ToString(_usCulture));
                                ProviderIdsExtensions.SetProviderId(byExternalId, providerIdKey, id);
                                return byExternalId;
                            }
                        }
                    }
                }
            }
            return null;
        }

        public int Order => 1;

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken) => _httpClient.GetResponse(new HttpRequestOptions()
        {
            CancellationToken = cancellationToken,
            Url = url
        });

        public class CreatedBy
        {
            public int id { get; set; }

            public string name { get; set; }

            public string profile_path { get; set; }
        }

        public class Network
        {
            public int id { get; set; }

            public string name { get; set; }
        }

        public class Season
        {
            public string air_date { get; set; }

            public int episode_count { get; set; }

            public int id { get; set; }

            public string poster_path { get; set; }

            public int season_number { get; set; }
        }

        public class Credits
        {
            public List<TmdbCast> cast { get; set; }

            public List<TmdbCrew> crew { get; set; }
        }

        public class Images
        {
            public List<TmdbImage> backdrops { get; set; }

            public List<TmdbImage> posters { get; set; }

            public List<TmdbImage> logos { get; set; }
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

        public class ContentRating
        {
            public string iso_3166_1 { get; set; }

            public string rating { get; set; }

            public string GetRating() => Country.GetRating(rating, iso_3166_1);
        }

        public class ContentRatings
        {
            public List<ContentRating> results { get; set; }
        }

        public class RootObject
        {
            public string backdrop_path { get; set; }

            public List<CreatedBy> created_by { get; set; }

            public List<int> episode_run_time { get; set; }

            public DateTimeOffset first_air_date { get; set; }

            public List<TmdbGenre> genres { get; set; }

            public string homepage { get; set; }

            public int id { get; set; }

            public bool in_production { get; set; }

            public List<string> languages { get; set; }

            public DateTimeOffset last_air_date { get; set; }

            public string name { get; set; }

            public List<Network> networks { get; set; }

            public int number_of_episodes { get; set; }

            public int number_of_seasons { get; set; }

            public string original_title { get; set; }

            public string original_name { get; set; }

            public List<string> origin_country { get; set; }

            public string overview { get; set; }

            public string popularity { get; set; }

            public string poster_path { get; set; }

            public List<Season> seasons { get; set; }

            public string status { get; set; }

            public double vote_average { get; set; }

            public int vote_count { get; set; }

            public Credits credits { get; set; }

            public Images images { get; set; }

            public TmdbKeywords keywords { get; set; }

            public TmdbExternalIds external_ids { get; set; }

            public Videos videos { get; set; }

            public ContentRatings content_ratings { get; set; }

            public string ResultLanguage { get; set; }

            public string GetOriginalTitle() => original_name ?? original_title;
        }
    }
}
