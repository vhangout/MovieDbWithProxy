﻿using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Providers;

namespace MovieDbWithProxy
{
    public class MovieDbMusicVideoProvider :
      IRemoteMetadataProvider<MusicVideo, MusicVideoInfo>,
      IMetadataProvider<MusicVideo>,
      IMetadataProvider,
      IRemoteMetadataProvider,
      IRemoteSearchProvider<MusicVideoInfo>,
      IRemoteSearchProvider,
      IHasMetadataFeatures
    {
        public string Name => Plugin.ProviderName;

        public Task<MetadataResult<MusicVideo>> GetMetadata(
          MusicVideoInfo info,
          CancellationToken cancellationToken)
        {
            EntryPoint.Current.LogCall();
            return MovieDbProvider.Current.GetItemMetadata<MusicVideo>(info, cancellationToken);
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
          MusicVideoInfo searchInfo,
          CancellationToken cancellationToken)
        {
            EntryPoint.Current.LogCall();
            return MovieDbProvider.Current.GetMovieSearchResults(searchInfo, cancellationToken);
        }

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken) => throw new NotImplementedException();

        public MetadataFeatures[] Features => new MetadataFeatures[2]
        {
            MetadataFeatures.Adult,
            MetadataFeatures.Collections
        };
    }
}
