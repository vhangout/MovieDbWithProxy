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

namespace MovieDbWithProxy
{
    public class MovieDbEpisodeProvider :
      MovieDbProviderBase,
      IRemoteMetadataProviderWithOptions<Episode, EpisodeInfo>,
      IRemoteMetadataProvider<Episode, EpisodeInfo>,
      IMetadataProvider<Episode>,
      IMetadataProvider,
      IRemoteMetadataProvider,
      IRemoteSearchProvider<EpisodeInfo>,
      IRemoteSearchProvider,
      IHasOrder,
      IHasMetadataFeatures
    {
        public int Order => 1;
        public string Name => Plugin.ProviderName;

        public MovieDbEpisodeProvider(
          IHttpClient httpClient,
          IServerConfigurationManager configurationManager,
          IJsonSerializer jsonSerializer,
          IFileSystem fileSystem,
          ILocalizationManager localization,
          ILogManager logManager)
          : base(httpClient, configurationManager, jsonSerializer, fileSystem, localization, logManager)
        {
        }

        public MetadataFeatures[] Features => new MetadataFeatures[1]
        {
            MetadataFeatures.Adult
        };

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
          EpisodeInfo searchInfo,
          CancellationToken cancellationToken)
        {
            MovieDbEpisodeProvider dbEpisodeProvider1 = this;
            List<RemoteSearchResult> list = new List<RemoteSearchResult>();
            int? nullable = searchInfo.IndexNumber;
            if (nullable.HasValue)
            {
                nullable = searchInfo.ParentIndexNumber;
                if (nullable.HasValue)
                {
                    DirectoryService directoryService = new DirectoryService(dbEpisodeProvider1.FileSystem);
                    MovieDbEpisodeProvider dbEpisodeProvider2 = dbEpisodeProvider1;
                    RemoteMetadataFetchOptions<EpisodeInfo> options = new RemoteMetadataFetchOptions<EpisodeInfo>();
                    options.SearchInfo = searchInfo;
                    options.DirectoryService = directoryService;
                    CancellationToken cancellationToken1 = cancellationToken;
                    // ISSUE: explicit non-virtual call
                    MetadataResult<Episode> metadataResult = await dbEpisodeProvider2.GetMetadata(options, cancellationToken1).ConfigureAwait(false);
                    if (metadataResult.HasMetadata)
                    {
                        // ISSUE: explicit non-virtual call
                        list.Add(metadataResult.ToRemoteSearchResult(dbEpisodeProvider1.Name));
                    }
                    return list;
                }
            }
            return list;
        }

        public async Task<MetadataResult<Episode>> GetMetadata(
          RemoteMetadataFetchOptions<EpisodeInfo> options,
          CancellationToken cancellationToken)
        {
            MovieDbEpisodeProvider dbEpisodeProvider = this;
            EpisodeInfo info = options.SearchInfo;
            MetadataResult<Episode> result = new MetadataResult<Episode>();
            if (info.IsMissingEpisode)
                return result;
            string seriesTmdbId;
            info.SeriesProviderIds.TryGetValue(MetadataProviders.Tmdb.ToString(), out seriesTmdbId);
            if (string.IsNullOrEmpty(seriesTmdbId))
                return result;
            int? seasonNumber = info.ParentIndexNumber;
            int? episodeNumber = info.IndexNumber;
            if (!seasonNumber.HasValue || !episodeNumber.HasValue)
                return result;
            string tmdbImageUrl = (await MovieDbProvider.Current.GetTmdbSettings(cancellationToken).ConfigureAwait(false)).images.GetImageUrl("original");
            try
            {
                MovieDbProviderBase.RootObject rootObject = await dbEpisodeProvider.GetEpisodeInfo(seriesTmdbId, seasonNumber.Value, episodeNumber.Value, info.MetadataLanguage, info.MetadataCountryCode, options.DirectoryService, cancellationToken).ConfigureAwait(false);
                result.HasMetadata = true;
                result.QueriedById = true;
                if (!string.IsNullOrEmpty(rootObject.overview))
                    result.ResultLanguage = info.MetadataLanguage;
                Episode episode = new Episode();
                result.Item = episode;
                episode.Name = info.Name;
                episode.IndexNumber = info.IndexNumber;
                episode.ParentIndexNumber = info.ParentIndexNumber;
                episode.IndexNumberEnd = info.IndexNumberEnd;
                if (rootObject.external_ids != null)
                {
                    if (rootObject.external_ids.tvdb_id > 0)
                        ProviderIdsExtensions.SetProviderId(episode, MetadataProviders.Tvdb, rootObject.external_ids.tvdb_id.ToString(CultureInfo.InvariantCulture));
                    if (rootObject.external_ids.tvrage_id > 0)
                        ProviderIdsExtensions.SetProviderId(episode, MetadataProviders.TvRage, rootObject.external_ids.tvrage_id.ToString(CultureInfo.InvariantCulture));
                    if (!string.IsNullOrEmpty(rootObject.external_ids.imdb_id) && !string.Equals(rootObject.external_ids.imdb_id, "0", StringComparison.OrdinalIgnoreCase))
                        ProviderIdsExtensions.SetProviderId(episode, MetadataProviders.Imdb, rootObject.external_ids.imdb_id);
                }
                episode.PremiereDate = new DateTimeOffset?(rootObject.air_date);
                episode.ProductionYear = new int?(result.Item.PremiereDate.Value.Year);
                episode.Name = rootObject.name;
                episode.Overview = rootObject.overview;
                episode.CommunityRating = new float?((float)rootObject.vote_average);
                if (rootObject.videos != null && rootObject.videos.results != null)
                {
                    foreach (MovieDbProviderBase.Video result1 in rootObject.videos.results)
                    {
                        if (string.Equals(result1.type, "trailer", StringComparison.OrdinalIgnoreCase) && string.Equals(result1.site, "youtube", StringComparison.OrdinalIgnoreCase))
                        {
                            string str = string.Format("http://www.youtube.com/watch?v={0}", result1.key);
                            Extensions.AddTrailerUrl(episode, str);
                        }
                    }
                }
                result.ResetPeople();
                Credits credits = rootObject.credits;
                if (credits != null)
                {
                    if (credits.cast != null)
                    {
                        foreach (TmdbCast tmdbCast in (IEnumerable<TmdbCast>)credits.cast.OrderBy(a => a.order))
                        {
                            PersonInfo personInfo = new PersonInfo()
                            {
                                Name = tmdbCast.name.Trim(),
                                Role = tmdbCast.character,
                                Type = PersonType.Actor
                            };
                            if (!string.IsNullOrWhiteSpace(tmdbCast.profile_path))
                                personInfo.ImageUrl = tmdbImageUrl + tmdbCast.profile_path;
                            if (tmdbCast.id > 0)
                                ProviderIdsExtensions.SetProviderId(personInfo, MetadataProviders.Tmdb, tmdbCast.id.ToString(CultureInfo.InvariantCulture));
                            result.AddPerson(personInfo);
                        }
                    }
                    if (credits.guest_stars != null)
                    {
                        foreach (GuestStar guestStar in (IEnumerable<GuestStar>)credits.guest_stars.OrderBy(a => a.order))
                        {
                            PersonInfo personInfo = new PersonInfo()
                            {
                                Name = guestStar.name.Trim(),
                                Role = guestStar.character,
                                Type = PersonType.GuestStar
                            };
                            if (!string.IsNullOrWhiteSpace(guestStar.profile_path))
                                personInfo.ImageUrl = tmdbImageUrl + guestStar.profile_path;
                            if (guestStar.id > 0)
                                ProviderIdsExtensions.SetProviderId(personInfo, MetadataProviders.Tmdb, guestStar.id.ToString(CultureInfo.InvariantCulture));
                            result.AddPerson(personInfo);
                        }
                    }
                    if (credits.crew != null)
                    {
                        PersonType[] source = new PersonType[1]
                        {
                            PersonType.Director
                        };
                        foreach (TmdbCrew tmdbCrew in credits.crew)
                        {
                            PersonType personType = PersonType.Lyricist;
                            string department = tmdbCrew.department;
                            if (string.Equals(department, "writing", StringComparison.OrdinalIgnoreCase))
                                personType = PersonType.Writer;
                            PersonType result2;
                            if (Enum.TryParse(department, true, out result2))
                                personType = result2;
                            else if (Enum.TryParse(tmdbCrew.job, true, out result2))
                                personType = result2;
                            if (source.Contains(personType))
                                result.AddPerson(new PersonInfo()
                                {
                                    Name = tmdbCrew.name.Trim(),
                                    Role = tmdbCrew.job,
                                    Type = personType
                                });
                        }
                    }
                }
            }
            catch (HttpException ex)
            {
                if (ex.StatusCode.HasValue && ex.StatusCode.Value == HttpStatusCode.NotFound)
                    return result;
                throw;
            }
            return result;
        }

        public Task<MetadataResult<Episode>> GetMetadata(
          EpisodeInfo info,
          CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken) => GetResponse(url, cancellationToken);        
    }
}
