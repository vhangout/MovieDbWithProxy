using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using MovieDbWithProxy.Commons;
using MovieDbWithProxy.Models;
using System.Globalization;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace MovieDbWithProxy
{
    public class MovieDbBoxSetProvider :
      IRemoteMetadataProvider<BoxSet, ItemLookupInfo>,
      IMetadataProvider<BoxSet>,
      IMetadataProvider,
      IRemoteMetadataProvider,
      IRemoteSearchProvider<ItemLookupInfo>,
      IRemoteSearchProvider
    {
        public string Name => Plugin.ProviderName;

        private const string GetCollectionInfo3 = "https://api.themoviedb.org/3/collection/{0}?api_key={1}&append_to_response=images";
        internal static MovieDbBoxSetProvider Current;
        private readonly IJsonSerializer _json;
        private readonly IServerConfigurationManager _config;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localization;
        private readonly ILibraryManager _libraryManager;
        private readonly CultureInfo _usCulture = new CultureInfo("en-US");

        public MovieDbBoxSetProvider(
          IJsonSerializer json,
          IServerConfigurationManager config,
          IFileSystem fileSystem,
          ILocalizationManager localization,
          IHttpClient httpClient,
          ILibraryManager libraryManager)
        {
            _json = json;
            _config = config;
            _fileSystem = fileSystem;
            _localization = localization;
            _libraryManager = libraryManager;
            Current = this;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
          ItemLookupInfo searchInfo,
          CancellationToken cancellationToken)
        {
            EntryPoint.Current.Log(this, LogSeverity.Info, "*** CALL ***");
            string tmdbId = ProviderIdsExtensions.GetProviderId(searchInfo, MetadataProviders.Tmdb);
            if (string.IsNullOrEmpty(tmdbId))
                return await new MovieDbSearch(_json, _libraryManager).GetCollectionSearchResults(searchInfo, cancellationToken).ConfigureAwait(false);
            RootObject rootObject = await EnsureInfo(tmdbId, searchInfo.MetadataLanguage, searchInfo.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
            RootObject info = await _json.DeserializeFromFileAsync<RootObject>(GetDataFilePath(_config.ApplicationPaths, tmdbId, searchInfo.MetadataLanguage)).ConfigureAwait(false);
            List<TmdbImage> images = (info.images ?? new Images()).posters ?? new List<TmdbImage>();
            string imageUrl = (await MovieDbProvider.Current.GetTmdbSettings(cancellationToken).ConfigureAwait(false)).images.GetImageUrl("original");
            RemoteSearchResult remoteSearchResult = new RemoteSearchResult()
            {
                Name = info.name,
                SearchProviderName = Name,
                ImageUrl = images.Count == 0 ? null : imageUrl + images[0].file_path
            };
            ProviderIdsExtensions.SetProviderId(remoteSearchResult, MetadataProviders.Tmdb, info.id.ToString(_usCulture));
            return new RemoteSearchResult[1]
            {
                remoteSearchResult
            };
        }

        public async Task<MetadataResult<BoxSet>> GetMetadata(
          ItemLookupInfo id,
          CancellationToken cancellationToken)
        {
            EntryPoint.Current.Log(this, LogSeverity.Info, "*** CALL ***");
            string tmdbId = ProviderIdsExtensions.GetProviderId(id, MetadataProviders.Tmdb);
            if (string.IsNullOrEmpty(tmdbId))
            {
                RemoteSearchResult remoteSearchResult = (await new MovieDbSearch(_json, _libraryManager).GetCollectionSearchResults(id, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
                if (remoteSearchResult != null)
                    tmdbId = ProviderIdsExtensions.GetProviderId(remoteSearchResult, MetadataProviders.Tmdb);
            }
            MetadataResult<BoxSet> result = new MetadataResult<BoxSet>();
            if (!string.IsNullOrEmpty(tmdbId))
            {
                RootObject rootObject = await GetMovieDbResult(tmdbId, id.MetadataLanguage, id.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
                if (rootObject != null)
                {
                    result.HasMetadata = true;
                    result.Item = GetItem(rootObject);
                }
            }                        
            return result;
        }

        internal Task<RootObject> GetMovieDbResult(
          string tmdbId,
          string preferredMetadataLanguage,
          string preferredMetadataCountry,
          CancellationToken cancellationToken)
        {
            EntryPoint.Current.Log(this, LogSeverity.Info, "*** CALL ***");
            if (string.IsNullOrEmpty(tmdbId))
                throw new ArgumentNullException(nameof(tmdbId));
            return EnsureInfo(tmdbId, preferredMetadataLanguage, preferredMetadataCountry, cancellationToken);
        }

        private BoxSet GetItem(RootObject obj)
        {
            BoxSet boxSet = new BoxSet();
            boxSet.Name = obj.name;
            boxSet.Overview = obj.overview;
            ProviderIdsExtensions.SetProviderId(boxSet, MetadataProviders.Tmdb, obj.id.ToString(_usCulture));
            return boxSet;
        }

        private async Task<RootObject> DownloadInfo(
          string tmdbId,
          string preferredMetadataLanguage,
          string preferredMetadataCountry,
          CancellationToken cancellationToken)
        {
            RootObject rootObject = await FetchMainResult(tmdbId, preferredMetadataLanguage, preferredMetadataCountry, cancellationToken).ConfigureAwait(false);
            if (rootObject == null)
                return null;
            string dataFilePath = GetDataFilePath(_config.ApplicationPaths, tmdbId, preferredMetadataLanguage);
            _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(dataFilePath));
            _json.SerializeToFile(rootObject, dataFilePath);
            return rootObject;
        }

        private async Task<RootObject> FetchMainResult(
          string id,
          string metadataLanguage,
          string preferredMetadataCountry,
          CancellationToken cancellationToken)
        {
            EntryPoint.Current.Log(this, LogSeverity.Info, "*** CALL ***");
            string url = string.Format("https://api.themoviedb.org/3/collection/{0}?api_key={1}&append_to_response=images", id, MovieDbProvider.ApiKey);
            if (!string.IsNullOrEmpty(metadataLanguage))
                url += string.Format("&language={0}", MovieDbProvider.NormalizeLanguage(metadataLanguage, preferredMetadataCountry));
            string str1 = MovieDbProvider.AddImageLanguageParam(url, metadataLanguage, preferredMetadataCountry);
            cancellationToken.ThrowIfCancellationRequested();
            RootObject rootObject = null;
            HttpResponseInfo response = await MovieDbProvider.Current.GetMovieDbResponse(new HttpRequestOptions()
            {
                Url = str1,
                CancellationToken = cancellationToken,
                AcceptHeader = MovieDbSearch.AcceptHeader
            }).ConfigureAwait(false);
            Stream json;
            try
            {
                json = response.Content;
                try
                {
                    rootObject = await _json.DeserializeFromStreamAsync<RootObject>(json).ConfigureAwait(false);
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
            cancellationToken.ThrowIfCancellationRequested();
            if (rootObject != null && string.IsNullOrEmpty(rootObject.name) && !string.IsNullOrEmpty(metadataLanguage) && !string.Equals(metadataLanguage, "en", StringComparison.OrdinalIgnoreCase))
            {
                string str2 = MovieDbProvider.AddImageLanguageParam(string.Format("https://api.themoviedb.org/3/collection/{0}?api_key={1}&append_to_response=images", id, MovieDbSearch.ApiKey) + "&language=en", metadataLanguage, preferredMetadataCountry);
                response = await MovieDbProvider.Current.GetMovieDbResponse(new HttpRequestOptions()
                {
                    Url = str2,
                    CancellationToken = cancellationToken,
                    AcceptHeader = MovieDbSearch.AcceptHeader
                }).ConfigureAwait(false);
                try
                {
                    json = response.Content;
                    try
                    {
                        rootObject = await _json.DeserializeFromStreamAsync<RootObject>(json).ConfigureAwait(false);
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
            return rootObject;
        }

        internal Task<RootObject> EnsureInfo(
          string tmdbId,
          string preferredMetadataLanguage,
          string preferredMetadataCountry,
          CancellationToken cancellationToken)
        {
            string dataFilePath = GetDataFilePath(_config.ApplicationPaths, tmdbId, preferredMetadataLanguage);
            FileSystemMetadata fileSystemInfo = _fileSystem.GetFileSystemInfo(dataFilePath);
            return fileSystemInfo.Exists && DateTimeOffset.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileSystemInfo) <= MovieDbProviderBase.CacheTime ? _json.DeserializeFromFileAsync<RootObject>(dataFilePath) : DownloadInfo(tmdbId, preferredMetadataLanguage, preferredMetadataCountry, cancellationToken);
        }        

        private static string GetDataFilePath(
          IApplicationPaths appPaths,
          string tmdbId,
          string preferredLanguage)
        {
            string dataPath = GetDataPath(appPaths, tmdbId);
            if (string.IsNullOrEmpty(preferredLanguage))
                preferredLanguage = "alllang";
            string path2 = string.Format("all-{0}.json", preferredLanguage);
            return Path.Combine(dataPath, path2);
        }

        private static string GetDataPath(IApplicationPaths appPaths, string tmdbId) => Path.Combine(GetCollectionsDataPath(appPaths), tmdbId);

        private static string GetCollectionsDataPath(IApplicationPaths appPaths) => Path.Combine(appPaths.CachePath, "tmdb-collections");

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken) => EntryPoint.Current.HttpClient.GetResponse(new HttpRequestOptions()
            {
                CancellationToken = cancellationToken,
                Url = url
            });

        internal class Part
        {
            public string title { get; set; }

            public int id { get; set; }

            public string release_date { get; set; }

            public string poster_path { get; set; }

            public string backdrop_path { get; set; }
        }

        internal class Images
        {
            public List<TmdbImage> backdrops { get; set; }

            public List<TmdbImage> posters { get; set; }

            public List<TmdbImage> logos { get; set; }
        }

        internal class RootObject
        {
            public int id { get; set; }

            public string name { get; set; }

            public string overview { get; set; }

            public string poster_path { get; set; }

            public string backdrop_path { get; set; }

            public List<Part> parts { get; set; }

            public Images images { get; set; }
        }
    }
}
