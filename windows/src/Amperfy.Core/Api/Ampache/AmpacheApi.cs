using Amperfy.Core.Downloads;

namespace Amperfy.Core.Api.Ampache;

/// IBackendApi implementation for Ampache.
public sealed class AmpacheApi : IBackendApi
{
    private readonly AmpacheXmlServerApi _ampacheXmlServerApi;
    private readonly INetworkMonitor _networkMonitor;
    private readonly EventLogger _eventLogger;

    public AmpacheApi(AmpacheXmlServerApi ampacheXmlServerApi, INetworkMonitor networkMonitor, EventLogger eventLogger)
    {
        _ampacheXmlServerApi = ampacheXmlServerApi;
        _networkMonitor = networkMonitor;
        _eventLogger = eventLogger;
    }

    public AmpacheXmlServerApi ServerApi => _ampacheXmlServerApi;

    public string ClientApiVersion => _ampacheXmlServerApi.ClientApiVersion;

    public string ServerApiVersion => _ampacheXmlServerApi.ServerApiVersion ?? "-";

    public IReadOnlyDictionary<string, string> HttpHeaders => _ampacheXmlServerApi.HttpHeaders;

    public void ProvideCredentials(LoginCredentials credentials) => _ampacheXmlServerApi.ProvideCredentials(credentials);

    public Task IsAuthenticationValidAsync(LoginCredentials credentials) => _ampacheXmlServerApi.IsAuthenticationValidAsync(credentials);

    public Task<Uri> GenerateUrlForDownloadingPlayableAsync(AbstractPlayableInfo playableInfo) =>
        _ampacheXmlServerApi.GenerateUrlForDownloadingPlayableAsync(playableInfo.Type == DerivedPlayableType.Song, playableInfo.Id);

    public Task<Uri> GenerateUrlForStreamingPlayableAsync(AbstractPlayableInfo playableInfo, StreamingMaxBitratePreference maxBitrate, StreamingFormatPreference formatPreference) =>
        _ampacheXmlServerApi.GenerateUrlForStreamingPlayableAsync(playableInfo.Type == DerivedPlayableType.Song, playableInfo.Id, maxBitrate, formatPreference);

    public Task<Uri> GenerateUrlForArtworkAsync(Artwork artwork) => _ampacheXmlServerApi.GenerateUrlForArtworkAsync(artwork.RemoteInfo);

    public ResponseError? CheckForErrorResponse(ApiDataResponse response) => _ampacheXmlServerApi.CheckForErrorResponse(response);

    public ILibrarySyncer CreateLibrarySyncer(Account account, LibraryStorage storage) =>
        new AmpacheLibrarySyncer(_ampacheXmlServerApi, account, _networkMonitor, storage, _eventLogger);

    public IDownloadManagerDelegate CreateArtworkDownloadDelegate() =>
        new AmpacheArtworkDownloadDelegate(_ampacheXmlServerApi, _networkMonitor);

    public ArtworkRemoteInfo? ExtractArtworkInfoFromUrl(string urlString) => AmpacheXmlServerApi.ExtractArtworkInfoFromUrl(urlString);

    public CleansedUrl Cleanse(Uri? url) => _ampacheXmlServerApi.Cleanse(url);
}
