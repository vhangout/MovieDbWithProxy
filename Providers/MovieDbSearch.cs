using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using MovieDbWithProxy.Models;
using System.Globalization;
using System.Net;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace MovieDbWithProxy
{
    public class MovieDbSearch
    {
        private static readonly CultureInfo EnUs = new CultureInfo("en-US");
        private const string Search3 = "https://api.themoviedb.org/3/search/{3}?api_key={1}&query={0}&language={2}";
        internal static string ApiKey = "f6bd687ffa63cd282b6ff2c6877f2669";
        internal static string AcceptHeader = "application/json,image/*";
        private readonly ILogger _logger;
        private readonly IJsonSerializer _json;
        private readonly ILibraryManager _libraryManager;

        public MovieDbSearch(ILogger logger, IJsonSerializer json, ILibraryManager libraryManager)
        {
            _logger = logger;
            _json = json;
            _libraryManager = libraryManager;
        }

        public Task<List<RemoteSearchResult>> GetSearchResults(
          SeriesInfo idInfo,
          CancellationToken cancellationToken)
        {
            return GetSearchResults(idInfo, "tv", cancellationToken);
        }

        public Task<List<RemoteSearchResult>> GetMovieSearchResults(
          ItemLookupInfo idInfo,
          CancellationToken cancellationToken)
        {
            return GetSearchResults(idInfo, "movie", cancellationToken);
        }

        public Task<List<RemoteSearchResult>> GetCollectionSearchResults(
          ItemLookupInfo idInfo,
          CancellationToken cancellationToken)
        {
            return GetSearchResults(idInfo, "collection", cancellationToken);
        }

        private async Task<List<RemoteSearchResult>> GetSearchResults(
          ItemLookupInfo idInfo,
          string searchType,
          CancellationToken cancellationToken)
        {
            string name = idInfo.Name;
            if (string.IsNullOrEmpty(name))
                return new List<RemoteSearchResult>();
            TmdbSettingsResult tmdbSettings = await MovieDbProvider.Current.GetTmdbSettings(cancellationToken).ConfigureAwait(false);
            this._logger.Info("MovieDbProvider: Finding id for item: " + name, Array.Empty<object>());
            string language = idInfo.MetadataLanguage;
            string country = idInfo.MetadataCountryCode;
            List<RemoteSearchResult> searchResults = await this.GetSearchResults(idInfo, searchType, language, country, tmdbSettings, cancellationToken).ConfigureAwait(false);
            if (searchResults.Count == 0 && !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
            {
                language = "en";
                searchResults = await GetSearchResults(idInfo, searchType, language, country, tmdbSettings, cancellationToken).ConfigureAwait(false);
            }
            return searchResults;
        }

        private async Task<List<RemoteSearchResult>> GetSearchResults(
          ItemLookupInfo idInfo,
          string searchType,
          string language,
          string country,
          TmdbSettingsResult tmdbSettings,
          CancellationToken cancellationToken)
        {
            string name = idInfo.Name;
            int? year = idInfo.Year;
            if (string.IsNullOrEmpty(name))
                return new List<RemoteSearchResult>();
            string tmdbImageUrl = tmdbSettings.images.GetImageUrl("original");
            if (!string.IsNullOrWhiteSpace(name))
            {
                ItemLookupInfo name1 = this._libraryManager.ParseName(MemoryExtensions.AsSpan(name));
                int? year1 = name1.Year;
                name = name1.Name;
                year = year ?? year1;
            }
            List<RemoteSearchResult> searchResults = await this.GetSearchResults(name, searchType, year, language, country, idInfo.EnableAdultMetadata, tmdbImageUrl, cancellationToken).ConfigureAwait(false);
            if (searchResults.Count == 0)
            {
                string b = name;
                if (name.EndsWith(",the", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - 4);
                else if (name.EndsWith(", the", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - 5);
                name = name.Replace(',', ' ');
                name = name.Replace('.', ' ');
                name = name.Replace('_', ' ');
                name = name.Replace('-', ' ');
                name = name.Replace('!', ' ');
                name = name.Replace('?', ' ');
                name = name.Replace("'", string.Empty);
                int length1 = name.IndexOfAny(new char[2]
                {
          '(',
          '['
                });
                if (length1 != -1)
                {
                    if (length1 > 0)
                    {
                        name = name.Substring(0, length1);
                    }
                    else
                    {
                        name = name.Replace('[', ' ');
                        name = name.Replace(']', ' ');
                        int length2 = name.IndexOf('(');
                        if (length2 != -1 && length2 > 0)
                            name = name.Substring(0, length2);
                    }
                }
                name = name.Trim();
                if (!string.Equals(name, b, StringComparison.OrdinalIgnoreCase))
                    searchResults = await this.GetSearchResults(name, searchType, year, language, country, idInfo.EnableAdultMetadata, tmdbImageUrl, cancellationToken).ConfigureAwait(false);
            }
            return searchResults;
        }

        private Task<List<RemoteSearchResult>> GetSearchResults(
          string name,
          string type,
          int? year,
          string language,
          string country,
          bool includeAdult,
          string baseImageUrl,
          CancellationToken cancellationToken)
        {
            return type == "tv" ? this.GetSearchResultsTv(name, year, language, country, includeAdult, baseImageUrl, cancellationToken) : this.GetSearchResultsGeneric(name, type, year, language, country, includeAdult, baseImageUrl, cancellationToken);
        }

        public async Task<RemoteSearchResult> FindMovieByExternalId(
          string id,
          string externalSource,
          string providerIdKey,
          CancellationToken cancellationToken)
        {
            string str = string.Format("https://api.themoviedb.org/3/find/{0}?api_key={1}&external_source={2}", (object)id, (object)MovieDbProvider.ApiKey, (object)externalSource);
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
                    MovieDbSearch.ExternalIdLookupResult externalIdLookupResult = await this._json.DeserializeFromStreamAsync<MovieDbSearch.ExternalIdLookupResult>(json).ConfigureAwait(false);
                    if (externalIdLookupResult != null)
                    {
                        if (externalIdLookupResult.movie_results != null)
                        {
                            MovieDbSearch.TmdbMovieSearchResult tv = externalIdLookupResult.movie_results.FirstOrDefault<MovieDbSearch.TmdbMovieSearchResult>();
                            if (tv != null)
                            {
                                string imageUrl = (await MovieDbProvider.Current.GetTmdbSettings(cancellationToken).ConfigureAwait(false)).images.GetImageUrl("original");
                                return this.ParseMovieSearchResult(tv, imageUrl);
                            }
                            tv = (MovieDbSearch.TmdbMovieSearchResult)null;
                        }
                    }
                }
            }
            return (RemoteSearchResult)null;
        }

        private async Task<List<RemoteSearchResult>> GetSearchResultsGeneric(
          string name,
          string type,
          int? year,
          string language,
          string country,
          bool includeAdult,
          string baseImageUrl,
          CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException(nameof(name));
            string str = string.Format("https://api.themoviedb.org/3/search/{3}?api_key={1}&query={0}&language={2}", (object)WebUtility.UrlEncode(name), (object)MovieDbSearch.ApiKey, (object)MovieDbProvider.NormalizeLanguage(language, country), (object)type);
            if (includeAdult)
                str += "&include_adult=true";
            bool enableOneYearTolerance = false;
            if (!enableOneYearTolerance && year.HasValue)
                str = str + "&year=" + year.Value.ToString((IFormatProvider)CultureInfo.InvariantCulture);
            MovieDbProvider current = MovieDbProvider.Current;
            List<RemoteSearchResult> list;
            using (HttpResponseInfo response = await current.GetMovieDbResponse(new HttpRequestOptions()
            {
                Url = str,
                CancellationToken = cancellationToken,
                AcceptHeader = MovieDbSearch.AcceptHeader
            }).ConfigureAwait(false))
            {
                using (Stream json = response.Content)
                    list = ((await this._json.DeserializeFromStreamAsync<MovieDbSearch.TmdbMovieSearchResults>(json).ConfigureAwait(false)).results ?? new List<MovieDbSearch.TmdbMovieSearchResult>()).Select<MovieDbSearch.TmdbMovieSearchResult, RemoteSearchResult>((Func<MovieDbSearch.TmdbMovieSearchResult, RemoteSearchResult>)(i => this.ParseMovieSearchResult(i, baseImageUrl))).Where<RemoteSearchResult>((Func<RemoteSearchResult, bool>)(i =>
                    {
                        if (year.HasValue)
                        {
                            int? productionYear = i.ProductionYear;
                            if (productionYear.HasValue && enableOneYearTolerance)
                            {
                                int num1 = year.Value;
                                productionYear = i.ProductionYear;
                                int num2 = productionYear.Value;
                                return Math.Abs(num1 - num2) <= 1;
                            }
                        }
                        return true;
                    })).ToList<RemoteSearchResult>();
            }
            return list;
        }

        private RemoteSearchResult ParseMovieSearchResult(
          MovieDbSearch.TmdbMovieSearchResult i,
          string baseImageUrl)
        {
            RemoteSearchResult movieSearchResult = new RemoteSearchResult()
            {
                SearchProviderName = MovieDbProvider.Current.Name,
                Name = i.title ?? i.name ?? i.original_title,
                ImageUrl = string.IsNullOrWhiteSpace(i.poster_path) ? (string)null : baseImageUrl + i.poster_path
            };
            DateTimeOffset result;
            if (!string.IsNullOrEmpty(i.release_date) && DateTimeOffset.TryParseExact(i.release_date, "yyyy-MM-dd", (IFormatProvider)MovieDbSearch.EnUs, DateTimeStyles.None, out result))
            {
                movieSearchResult.PremiereDate = new DateTimeOffset?(result.ToUniversalTime());
                movieSearchResult.ProductionYear = new int?(movieSearchResult.PremiereDate.Value.Year);
            }
            ProviderIdsExtensions.SetProviderId((IHasProviderIds)movieSearchResult, (MetadataProviders)3, i.id.ToString((IFormatProvider)MovieDbSearch.EnUs));
            return movieSearchResult;
        }

        private async Task<List<RemoteSearchResult>> GetSearchResultsTv(
          string name,
          int? year,
          string language,
          string country,
          bool includeAdult,
          string baseImageUrl,
          CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException(nameof(name));
            string str = string.Format("https://api.themoviedb.org/3/search/{3}?api_key={1}&query={0}&language={2}", (object)WebUtility.UrlEncode(name), (object)MovieDbSearch.ApiKey, (object)MovieDbProvider.NormalizeLanguage(language, country), (object)"tv");
            if (year.HasValue)
                str = str + "&first_air_date_year=" + year.Value.ToString((IFormatProvider)CultureInfo.InvariantCulture);
            if (includeAdult)
                str += "&include_adult=true";
            MovieDbProvider current = MovieDbProvider.Current;
            List<RemoteSearchResult> list;
            using (HttpResponseInfo response = await current.GetMovieDbResponse(new HttpRequestOptions()
            {
                Url = str,
                CancellationToken = cancellationToken,
                AcceptHeader = MovieDbSearch.AcceptHeader
            }).ConfigureAwait(false))
            {
                using (Stream json = response.Content)
                    list = ((await this._json.DeserializeFromStreamAsync<MovieDbSearch.TmdbTvSearchResults>(json).ConfigureAwait(false)).results ?? new List<MovieDbSearch.TvResult>()).Select<MovieDbSearch.TvResult, RemoteSearchResult>((Func<MovieDbSearch.TvResult, RemoteSearchResult>)(i =>
                    {
                        RemoteSearchResult searchResultsTv = new RemoteSearchResult()
                        {
                            SearchProviderName = MovieDbProvider.Current.Name,
                            Name = i.name ?? i.original_name,
                            ImageUrl = string.IsNullOrWhiteSpace(i.poster_path) ? (string)null : baseImageUrl + i.poster_path
                        };
                        DateTimeOffset result;
                        if (!string.IsNullOrEmpty(i.first_air_date) && DateTimeOffset.TryParseExact(i.first_air_date, "yyyy-MM-dd", (IFormatProvider)MovieDbSearch.EnUs, DateTimeStyles.None, out result))
                        {
                            searchResultsTv.PremiereDate = new DateTimeOffset?(result.ToUniversalTime());
                            searchResultsTv.ProductionYear = new int?(searchResultsTv.PremiereDate.Value.Year);
                        }
                        ProviderIdsExtensions.SetProviderId((IHasProviderIds)searchResultsTv, (MetadataProviders)3, i.id.ToString((IFormatProvider)MovieDbSearch.EnUs));
                        return searchResultsTv;
                    })).ToList<RemoteSearchResult>();
            }
            return list;
        }

        public class TmdbMovieSearchResult
        {
            public bool adult { get; set; }

            public string backdrop_path { get; set; }

            public int id { get; set; }

            public string original_title { get; set; }

            public string original_name { get; set; }

            public string release_date { get; set; }

            public string poster_path { get; set; }

            public double popularity { get; set; }

            public string title { get; set; }

            public double vote_average { get; set; }

            public string name { get; set; }

            public int vote_count { get; set; }
        }

        public class TmdbMovieSearchResults
        {
            public int page { get; set; }

            public List<MovieDbSearch.TmdbMovieSearchResult> results { get; set; }

            public int total_pages { get; set; }

            public int total_results { get; set; }
        }

        public class TvResult
        {
            public string backdrop_path { get; set; }

            public string first_air_date { get; set; }

            public int id { get; set; }

            public string original_name { get; set; }

            public string poster_path { get; set; }

            public double popularity { get; set; }

            public string name { get; set; }

            public double vote_average { get; set; }

            public int vote_count { get; set; }
        }

        public class TmdbTvSearchResults
        {
            public int page { get; set; }

            public List<MovieDbSearch.TvResult> results { get; set; }

            public int total_pages { get; set; }

            public int total_results { get; set; }
        }

        public class ExternalIdLookupResult
        {
            public List<MovieDbSearch.TmdbMovieSearchResult> movie_results { get; set; }

            public List<object> person_results { get; set; }

            public List<MovieDbSearch.TvResult> tv_results { get; set; }
        }
    }
}
