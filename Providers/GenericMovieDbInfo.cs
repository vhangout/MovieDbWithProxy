using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MovieDbWithProxy.Models;
using System.Globalization;
using System.Net;

namespace MovieDbWithProxy
{
    public class GenericMovieDbInfo<T> where T : BaseItem, new()
    {
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly CultureInfo _usCulture = new CultureInfo("en-US");

        public GenericMovieDbInfo(
          IJsonSerializer jsonSerializer,
          ILibraryManager libraryManager,
          IFileSystem fileSystem)
        {
            _jsonSerializer = jsonSerializer;
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;
        }

        public async Task<MetadataResult<T>> GetMetadata(
          ItemLookupInfo itemId,
          CancellationToken cancellationToken)
        {
            string tmdbId = ProviderIdsExtensions.GetProviderId(itemId, (MetadataProviders)3);
            string imdbId = ProviderIdsExtensions.GetProviderId(itemId, (MetadataProviders)2);
            if (string.IsNullOrEmpty(tmdbId) && string.IsNullOrEmpty(imdbId))
            {
                var remoteSearchResult = (await new MovieDbSearch(_jsonSerializer, _libraryManager).GetMovieSearchResults(itemId, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
                if (remoteSearchResult != null)
                    tmdbId = ProviderIdsExtensions.GetProviderId(remoteSearchResult, MetadataProviders.Tmdb);
            }
            if (string.IsNullOrEmpty(tmdbId) && string.IsNullOrEmpty(imdbId))
                return new MetadataResult<T>();
            cancellationToken.ThrowIfCancellationRequested();
            return await FetchMovieData(tmdbId, imdbId, itemId.MetadataLanguage, itemId.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
        }

        private async Task<MetadataResult<T>> FetchMovieData(
          string tmdbId,
          string imdbId,
          string language,
          string preferredCountryCode,
          CancellationToken cancellationToken)
        {
            MetadataResult<T> item = new MetadataResult<T>()
            {
                Item = new T()
            };
            if (string.IsNullOrEmpty(tmdbId))
            {
                CompleteMovieData completeMovieData = await MovieDbProvider.Current.FetchMainResult(imdbId, false, language, preferredCountryCode, cancellationToken).ConfigureAwait(false);
                if (completeMovieData != null)
                {
                    tmdbId = completeMovieData.id.ToString(_usCulture);
                    string dataFilePath = MovieDbProvider.Current.GetDataFilePath(tmdbId, language);
                    _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(dataFilePath));
                    _jsonSerializer.SerializeToFile(completeMovieData, dataFilePath);
                }
            }
            if (!string.IsNullOrWhiteSpace(tmdbId))
            {
                CompleteMovieData movieInfo = await MovieDbProvider.Current.EnsureMovieInfo(tmdbId, language, preferredCountryCode, cancellationToken).ConfigureAwait(false);
                if (movieInfo != null)
                {
                    TmdbSettingsResult settings = await MovieDbProvider.Current.GetTmdbSettings(cancellationToken).ConfigureAwait(false);
                    ProcessMainInfo(item, settings, preferredCountryCode, movieInfo);
                    item.HasMetadata = true;
                }
            }
            MetadataResult<T> metadataResult = item;            
            return metadataResult;
        }

        private void ProcessMainInfo(
          MetadataResult<T> resultItem,
          TmdbSettingsResult settings,
          string preferredCountryCode,
          CompleteMovieData movieData)
        {
            T obj = resultItem.Item;
            obj.Name = movieData.GetTitle() ?? obj.Name;
            obj.OriginalTitle = movieData.GetOriginalTitle();
            obj.Overview = string.IsNullOrEmpty(movieData.overview) ? null : WebUtility.HtmlDecode(movieData.overview);
            obj.Overview = obj.Overview != null ? obj.Overview.Replace("\n\n", "\n") : null;
            if (!string.IsNullOrEmpty(movieData.tagline))
                obj.Tagline = movieData.tagline;
            if (movieData.production_countries != null)
                obj.ProductionLocations = LinqExtensions.ToArray(movieData.production_countries.Select(i => i.name), movieData.production_countries.Count);
            ProviderIdsExtensions.SetProviderId(obj, MetadataProviders.Tmdb, movieData.id.ToString(_usCulture));
            ProviderIdsExtensions.SetProviderId(obj, MetadataProviders.Imdb, movieData.imdb_id);
            if (movieData.belongs_to_collection != null && !string.IsNullOrEmpty(movieData.belongs_to_collection.name) && movieData.belongs_to_collection.id > 0)
            {
                LinkedItemInfo linkedItemInfo1 = new LinkedItemInfo();
                linkedItemInfo1.Name = movieData.belongs_to_collection.name;
                LinkedItemInfo linkedItemInfo2 = linkedItemInfo1;
                ProviderIdsExtensions.SetProviderId(linkedItemInfo2, MetadataProviders.Tmdb, movieData.belongs_to_collection.id.ToString(CultureInfo.InvariantCulture));
                obj.AddCollection(linkedItemInfo2);
            }
            float result1;
            if (float.TryParse(movieData.vote_average.ToString(CultureInfo.InvariantCulture), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result1))
                obj.CommunityRating = new float?(result1);
            if (movieData.releases != null && movieData.releases.countries != null)
            {
                List<Country> list = movieData.releases.countries.Where(i => !string.IsNullOrWhiteSpace(i.certification)).ToList();
                Country? country1 = list.FirstOrDefault(c => string.Equals(c.iso_3166_1, preferredCountryCode, StringComparison.OrdinalIgnoreCase));
                Country? country2 = list.FirstOrDefault(c => string.Equals(c.iso_3166_1, "US", StringComparison.OrdinalIgnoreCase));
                if (country1 != null)
                    obj.OfficialRating = country1.GetRating();
                else if (country2 != null)
                    obj.OfficialRating = country2.GetRating();
            }
            DateTimeOffset result2;
            if (!string.IsNullOrEmpty(movieData.release_date) && DateTimeOffset.TryParse(movieData.release_date, _usCulture, DateTimeStyles.None, out result2))
            {
                obj.PremiereDate = new DateTimeOffset?(result2.ToUniversalTime());
                obj.ProductionYear = new int?(obj.PremiereDate.Value.Year);
            }
            if (movieData.production_companies != null)
                obj.SetStudios(movieData.production_companies.Select(c => c.name));
            foreach (string str in (movieData.genres ?? new List<TmdbGenre>()).Select<TmdbGenre, string>(g => g.name))
                obj.AddGenre(str);
            resultItem.ResetPeople();
            string imageUrl = settings.images.GetImageUrl("original");
            if (movieData.casts != null && movieData.casts.cast != null)
            {
                foreach (TmdbCast tmdbCast in (IEnumerable<TmdbCast>)movieData.casts.cast.OrderBy(a => a.order))
                {
                    PersonInfo personInfo = new PersonInfo()
                    {
                        Name = tmdbCast.name.Trim(),
                        Role = tmdbCast.character,
                        Type = PersonType.Actor
                    };
                    if (!string.IsNullOrWhiteSpace(tmdbCast.profile_path))
                        personInfo.ImageUrl = imageUrl + tmdbCast.profile_path;
                    if (tmdbCast.id > 0)
                        ProviderIdsExtensions.SetProviderId(personInfo, MetadataProviders.Tmdb, tmdbCast.id.ToString(CultureInfo.InvariantCulture));
                    resultItem.AddPerson(personInfo);
                }
            }
            if (movieData.casts != null && movieData.casts.crew != null)
            {
                PersonType[] source = new PersonType[1]
                {
                    PersonType.Director
                };
                foreach (TmdbCrew tmdbCrew in movieData.casts.crew)
                {
                    PersonType personType = PersonType.Lyricist;
                    string department = tmdbCrew.department;
                    if (string.Equals(department, "writing", StringComparison.OrdinalIgnoreCase))
                        personType = PersonType.Writer;
                    PersonType result3;
                    if (Enum.TryParse(department, true, out result3))
                        personType = result3;
                    else if (Enum.TryParse(tmdbCrew.job, true, out result3))
                        personType = result3;
                    if (source.Contains(personType))
                    {
                        PersonInfo personInfo = new PersonInfo()
                        {
                            Name = tmdbCrew.name.Trim(),
                            Role = tmdbCrew.job,
                            Type = personType
                        };
                        if (!string.IsNullOrWhiteSpace(tmdbCrew.profile_path))
                            personInfo.ImageUrl = imageUrl + tmdbCrew.profile_path;
                        if (tmdbCrew.id > 0)
                            ProviderIdsExtensions.SetProviderId(personInfo, MetadataProviders.Tmdb, tmdbCrew.id.ToString(CultureInfo.InvariantCulture));
                        resultItem.AddPerson(personInfo);
                    }
                }
            }
            if (movieData.trailers == null || movieData.trailers.youtube == null)
                return;
            obj.RemoteTrailers = movieData.trailers.youtube.Where<Youtube>((Func<Youtube, bool>)(i => string.Equals(i.type, "trailer", StringComparison.OrdinalIgnoreCase))).Select<Youtube, string>((Func<Youtube, string>)(i => string.Format("https://www.youtube.com/watch?v={0}", (object)i.source))).ToArray<string>();
        }
    }
}
