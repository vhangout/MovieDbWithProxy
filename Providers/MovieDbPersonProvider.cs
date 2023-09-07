using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
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
    public class MovieDbPersonProvider :
      IRemoteMetadataProviderWithOptions<Person, PersonLookupInfo>,
      IRemoteMetadataProvider<Person, PersonLookupInfo>,
      IMetadataProvider<Person>,
      IMetadataProvider,
      IRemoteMetadataProvider,
      IRemoteSearchProvider<PersonLookupInfo>,
      IRemoteSearchProvider
    {
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IFileSystem _fileSystem;
        private readonly IServerConfigurationManager _configurationManager;
        private readonly IHttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly CultureInfo _usCulture = new CultureInfo("en-US");

        internal static MovieDbPersonProvider Current { get; private set; }

        public MovieDbPersonProvider(
          IFileSystem fileSystem,
          IServerConfigurationManager configurationManager,
          IJsonSerializer jsonSerializer,
          IHttpClient httpClient,
          ILogger logger)
        {
            _fileSystem = fileSystem;
            _configurationManager = configurationManager;
            _jsonSerializer = jsonSerializer;
            _httpClient = httpClient;
            _logger = logger;
            Current = this;
        }

        public string Name => "TheMovieDb";

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
          PersonLookupInfo searchInfo,
          CancellationToken cancellationToken)
        {
            string tmdbId = ProviderIdsExtensions.GetProviderId(searchInfo, MetadataProviders.Tmdb);
            string tmdbImageUrl = (await MovieDbProvider.Current.GetTmdbSettings(cancellationToken).ConfigureAwait(false)).images.GetImageUrl("original");
            string metadataLanguage = searchInfo.MetadataLanguage;
            if (!string.IsNullOrEmpty(tmdbId))
            {
                PersonResult personResult = await EnsurePersonInfo(tmdbId, metadataLanguage, new DirectoryService(_fileSystem), cancellationToken).ConfigureAwait(false);
                List<TmdbImage> tmdbImageList = (personResult.images ?? new Images()).profiles ?? new List<TmdbImage>();
                RemoteSearchResult remoteSearchResult = new RemoteSearchResult()
                {
                    Name = personResult.name,
                    SearchProviderName = Name,
                    ImageUrl = tmdbImageList.Count == 0 ? null : tmdbImageUrl + tmdbImageList[0].file_path
                };
                ProviderIdsExtensions.SetProviderId(remoteSearchResult, MetadataProviders.Tmdb, personResult.id.ToString(_usCulture));
                ProviderIdsExtensions.SetProviderId(remoteSearchResult, MetadataProviders.Imdb, personResult.imdb_id);
                return new RemoteSearchResult[1]
                {
                    remoteSearchResult
                };
            }
            string providerId = ProviderIdsExtensions.GetProviderId(searchInfo, MetadataProviders.Imdb);
            HttpResponseInfo response;
            Stream json;
            if (!string.IsNullOrEmpty(providerId))
            {
                string str = string.Format("https://api.themoviedb.org/3/find/{0}?api_key={1}&external_source=imdb_id", providerId, MovieDbProvider.ApiKey);
                response = await MovieDbProvider.Current.GetMovieDbResponse(new HttpRequestOptions()
                {
                    Url = str,
                    CancellationToken = cancellationToken,
                    AcceptHeader = MovieDbProvider.AcceptHeader
                }).ConfigureAwait(false);
                try
                {
                    json = response.Content;
                    try
                    {
                        return (await _jsonSerializer.DeserializeFromStreamAsync<GeneralSearchResults>(json).ConfigureAwait(false) ?? new GeneralSearchResults()).person_results.Select<PersonSearchResult, RemoteSearchResult>((Func<PersonSearchResult, RemoteSearchResult>)(i => GetSearchResult(i, tmdbImageUrl)));
                    }
                    finally
                    {
                        json?.Dispose();
                    }
                }
                finally
                {
                    ((IDisposable)response)?.Dispose();
                }
            }
            else
            {
                if (searchInfo.IsAutomated)
                    return new List<RemoteSearchResult>();
                string str = string.Format("https://api.themoviedb.org/3/search/person?api_key={1}&query={0}", WebUtility.UrlEncode(searchInfo.Name), MovieDbProvider.ApiKey);
                response = await MovieDbProvider.Current.GetMovieDbResponse(new HttpRequestOptions()
                {
                    Url = str,
                    CancellationToken = cancellationToken,
                    AcceptHeader = MovieDbProvider.AcceptHeader
                }).ConfigureAwait(false);
                try
                {
                    json = response.Content;
                    try
                    {
                        return (await _jsonSerializer.DeserializeFromStreamAsync<PersonSearchResults>(json).ConfigureAwait(false) ?? new PersonSearchResults()).Results.Select<PersonSearchResult, RemoteSearchResult>((Func<PersonSearchResult, RemoteSearchResult>)(i => GetSearchResult(i, tmdbImageUrl)));
                    }
                    finally
                    {
                        json?.Dispose();
                    }
                }
                finally
                {
                    ((IDisposable)response)?.Dispose();
                }
            }
        }

        private RemoteSearchResult GetSearchResult(
          PersonSearchResult i,
          string baseImageUrl)
        {
            RemoteSearchResult searchResult = new RemoteSearchResult();
            searchResult.SearchProviderName = Name;
            searchResult.Name = i.Name;
            searchResult.ImageUrl = string.IsNullOrEmpty(i.Profile_Path) ? null : baseImageUrl + i.Profile_Path;
            ProviderIdsExtensions.SetProviderId(searchResult, MetadataProviders.Tmdb, i.Id.ToString(_usCulture));
            return searchResult;
        }

        public async Task<MetadataResult<Person>> GetMetadata(
          RemoteMetadataFetchOptions<PersonLookupInfo> options,
          CancellationToken cancellationToken)
        {
            PersonLookupInfo id = options.SearchInfo;
            IDirectoryService directoryService = options.DirectoryService;
            string id1 = ProviderIdsExtensions.GetProviderId(id, MetadataProviders.Tmdb);
            if (string.IsNullOrEmpty(id1))
                id1 = await GetTmdbId(id, cancellationToken).ConfigureAwait(false);
            MetadataResult<Person> result = new MetadataResult<Person>();
            if (!string.IsNullOrEmpty(id1))
            {
                string metadataLanguage = id.MetadataLanguage;
                PersonResult personResult;
                try
                {
                    personResult = await EnsurePersonInfo(id1, metadataLanguage, directoryService, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpException ex)
                {
                    if (ex.StatusCode.HasValue && ex.StatusCode.Value == HttpStatusCode.NotFound)
                        return result;
                    throw;
                }
                Person person = new Person();
                result.HasMetadata = true;
                person.Name = personResult.name;
                if (!string.IsNullOrWhiteSpace(personResult.place_of_birth))
                    person.ProductionLocations = new string[1]
                    {
                        personResult.place_of_birth
                    };
                person.Overview = personResult.biography;
                DateTimeOffset result1;
                if (DateTimeOffset.TryParseExact(personResult.birthday, "yyyy-MM-dd", new CultureInfo("en-US"), DateTimeStyles.None, out result1))
                    person.PremiereDate = new DateTimeOffset?(result1.ToUniversalTime());
                if (DateTimeOffset.TryParseExact(personResult.deathday, "yyyy-MM-dd", new CultureInfo("en-US"), DateTimeStyles.None, out result1))
                    person.EndDate = new DateTimeOffset?(result1.ToUniversalTime());
                ProviderIdsExtensions.SetProviderId(person, MetadataProviders.Tmdb, personResult.id.ToString(_usCulture));
                if (!string.IsNullOrEmpty(personResult.imdb_id))
                    ProviderIdsExtensions.SetProviderId(person, MetadataProviders.Imdb, personResult.imdb_id);
                result.HasMetadata = true;
                result.Item = person;
            }
            return result;
        }

        public Task<MetadataResult<Person>> GetMetadata(
          PersonLookupInfo id,
          CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private async Task<string> GetTmdbId(PersonLookupInfo info, CancellationToken cancellationToken) => (await GetSearchResults(info, cancellationToken).ConfigureAwait(false)).Select(i => ProviderIdsExtensions.GetProviderId(i, MetadataProviders.Tmdb)).FirstOrDefault<string>();

        internal async Task<PersonResult> EnsurePersonInfo(
          string id,
          string language,
          IDirectoryService directoryService,
          CancellationToken cancellationToken)
        {
            string cacheKey = "tmdb_person_" + id + "_" + language;
            PersonResult personResult;
            if (!directoryService.TryGetFromCache(cacheKey, out personResult))
            {
                string dataFilePath = GetPersonDataFilePath(_configurationManager.ApplicationPaths, id, language);
                FileSystemMetadata fileSystemInfo = _fileSystem.GetFileSystemInfo(dataFilePath);
                if (fileSystemInfo.Exists && DateTimeOffset.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileSystemInfo) <= MovieDbProviderBase.CacheTime)
                    personResult = await _jsonSerializer.DeserializeFromFileAsync<PersonResult>(dataFilePath).ConfigureAwait(false);
                if (personResult == null)
                {
                    _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(dataFilePath));
                    personResult = await FetchPersonResult(id, language, cancellationToken).ConfigureAwait(false);
                    using (Stream fileStream = _fileSystem.GetFileStream(dataFilePath, FileOpenMode.Create, FileAccessMode.Write, FileShareMode.Read, false))
                        _jsonSerializer.SerializeToStream(personResult, fileStream);
                }
                directoryService.AddOrUpdateCache(cacheKey, personResult);                
            }
            return personResult;
        }

        private string GetPersonMetadataUrl(string id) => string.Format("https://api.themoviedb.org/3/person/{1}?api_key={0}&append_to_response=credits,images,external_ids", (object)MovieDbProvider.ApiKey, (object)id);

        internal async Task<PersonResult> FetchPersonResult(
          string id,
          string language,
          CancellationToken cancellationToken)
        {
            string personMetadataUrl = GetPersonMetadataUrl(id);
            if (!string.IsNullOrEmpty(language))
                personMetadataUrl += string.Format("&language={0}", language);
            HttpResponseInfo response = await MovieDbProvider.Current.GetMovieDbResponse(new HttpRequestOptions()
            {
                Url = personMetadataUrl,
                CancellationToken = cancellationToken,
                AcceptHeader = MovieDbProvider.AcceptHeader
            }).ConfigureAwait(false);
            PersonResult mainResult;
            Stream json;
            try
            {
                json = response.Content;
                try
                {
                    mainResult = await _jsonSerializer.DeserializeFromStreamAsync<PersonResult>(json).ConfigureAwait(false);
                }
                finally
                {
                    json?.Dispose();
                }
            }
            finally
            {
                ((IDisposable)response)?.Dispose();
            }

            if (mainResult != null && !string.IsNullOrEmpty(language) && !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(mainResult.biography))
            {
                _logger.Info("MovieDbPersonProvider metadata is incomplete for language " + language + ". Trying English...", Array.Empty<object>());
                string str = GetPersonMetadataUrl(id) + string.Format("&language={0}", "en");
                response = await MovieDbProvider.Current.GetMovieDbResponse(new HttpRequestOptions()
                {
                    Url = str,
                    CancellationToken = cancellationToken,
                    AcceptHeader = MovieDbProvider.AcceptHeader
                }).ConfigureAwait(false);
                try
                {
                    json = response.Content;
                    try
                    {
                        PersonResult personResult = await _jsonSerializer.DeserializeFromStreamAsync<PersonResult>(json).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(mainResult.biography))
                            mainResult.biography = personResult.biography;
                    }
                    finally
                    {
                        json?.Dispose();
                    }
                }
                finally
                {
                    ((IDisposable)response)?.Dispose();
                }
            }
            return mainResult;
        }

        private static string GetPersonDataPath(IApplicationPaths appPaths, string tmdbId)
        {
            string path2 = BaseExtensions.GetMD5(tmdbId).ToString().Substring(0, 1);
            return Path.Combine(GetPersonsDataPath(appPaths), path2, tmdbId);
        }

        internal static string GetPersonDataFilePath(
          IApplicationPaths appPaths,
          string tmdbId,
          string language)
        {
            string str = "info";
            if (!string.IsNullOrEmpty(language))
                str = str + "-" + language;
            string path2 = str + ".json";
            return Path.Combine(GetPersonDataPath(appPaths, tmdbId), path2);
        }

        private static string GetPersonsDataPath(IApplicationPaths appPaths) => Path.Combine(appPaths.CachePath, "tmdb-people");

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken) => _httpClient.GetResponse(new HttpRequestOptions()
        {
            CancellationToken = cancellationToken,
            Url = url
        });

        public class PersonSearchResult
        {
            public bool Adult { get; set; }

            public int Id { get; set; }

            public string Name { get; set; }

            public string Profile_Path { get; set; }
        }

        public class PersonSearchResults
        {
            public int Page { get; set; }

            public List<PersonSearchResult> Results { get; set; }

            public int Total_Pages { get; set; }

            public int Total_Results { get; set; }

            public PersonSearchResults() => Results = new List<PersonSearchResult>();
        }

        public class GeneralSearchResults
        {
            public List<PersonSearchResult> person_results { get; set; }

            public GeneralSearchResults() => person_results = new List<PersonSearchResult>();
        }

        public class Credits
        {
            public List<TmdbCast> cast { get; set; }

            public List<TmdbCrew> crew { get; set; }
        }

        public class Images
        {
            public List<TmdbImage> profiles { get; set; }
        }

        public class PersonResult
        {
            public bool adult { get; set; }

            public List<object> also_known_as { get; set; }

            public string biography { get; set; }

            public string birthday { get; set; }

            public string deathday { get; set; }

            public string homepage { get; set; }

            public int id { get; set; }

            public string imdb_id { get; set; }

            public string name { get; set; }

            public string place_of_birth { get; set; }

            public double popularity { get; set; }

            public string profile_path { get; set; }

            public Credits credits { get; set; }

            public Images images { get; set; }

            public TmdbExternalIds external_ids { get; set; }
        }
    }
}
