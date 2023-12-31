﻿using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Providers;

namespace MovieDbWithProxy
{
    public class MovieDbTrailerProvider :
      IHasOrder,
      IRemoteMetadataProvider<Trailer, TrailerInfo>,
      IMetadataProvider<Trailer>,
      IMetadataProvider,
      IRemoteMetadataProvider,
      IRemoteSearchProvider<TrailerInfo>,
      IRemoteSearchProvider,
      IHasMetadataFeatures
    {
        private readonly IHttpClient _httpClient;
        public MovieDbTrailerProvider(IHttpClient httpClient) => this._httpClient = httpClient;
        public string Name => Plugin.ProviderName;

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
          TrailerInfo searchInfo,
          CancellationToken cancellationToken)
        {
            EntryPoint.Current.LogCall();
            return MovieDbProvider.Current.GetMovieSearchResults(searchInfo, cancellationToken);
        }

        public Task<MetadataResult<Trailer>> GetMetadata(
          TrailerInfo info,
          CancellationToken cancellationToken)
        {
            EntryPoint.Current.LogCall();
            return MovieDbProvider.Current.GetItemMetadata<Trailer>(info, cancellationToken);
        }

        public int Order => 0;

        public MetadataFeatures[] Features => new MetadataFeatures[2]
        {
            MetadataFeatures.Adult,
            MetadataFeatures.Collections
        };

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            EntryPoint.Current.LogCall();
            return MovieDbProvider.Current.GetImageResponse(url, cancellationToken);
        }
    }
}
