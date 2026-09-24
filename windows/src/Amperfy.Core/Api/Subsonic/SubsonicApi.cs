namespace Amperfy.Core.Api.Subsonic;

/// Subsonic implementation of the backend API (port of SubsonicApi.swift).
public sealed class SubsonicApi : IBackendApi
{
    private readonly SubsonicServerApi _subsonicServerApi;
    private readonly INetworkMonitor _networkMonitor;
    private readonly EventLogger _eventLogger;

    public SubsonicApi(SubsonicServerApi subsonicServerApi, INetworkMonitor networkMonitor, EventLogger eventLogger)
    {
        _subsonicServerApi = subsonicServerApi;
        _networkMonitor = networkMonitor;
        _eventLogger = eventLogger;
    }

    public SubsonicServerApi ServerApi => _subsonicServerApi;

    public SubsonicApiAuthType AuthType => _subsonicServerApi.AuthType;

    public void SetAuthType(SubsonicApiAuthType newAuthType) => _subsonicServerApi.SetAuthType(newAuthType);

    public string ClientApiVersion => _subsonicServerApi.ClientApiVersion?.Description ?? "-";

    public string ServerApiVersion => _subsonicServerApi.ServerApiVersion?.Description ?? "-";

    public IReadOnlyDictionary<string, string> HttpHeaders => _subsonicServerApi.HttpHeaders;

    public void ProvideCredentials(LoginCredentials credentials) => _subsonicServerApi.ProvideCredentials(credentials);

    public Task IsAuthenticationValidAsync(LoginCredentials credentials) => _subsonicServerApi.IsAuthenticationValidAsync(credentials);

    public Task<Uri> GenerateUrlForDownloadingPlayableAsync(AbstractPlayableInfo playableInfo)
    {
        var apiId = playableInfo.StreamId ?? playableInfo.Id;
        return _subsonicServerApi.GenerateUrlForDownloadingPlayableAsync(apiId);
    }

    public Task<Uri> GenerateUrlForStreamingPlayableAsync(AbstractPlayableInfo playableInfo, StreamingMaxBitratePreference maxBitrate, StreamingFormatPreference formatPreference)
    {
        var apiId = playableInfo.StreamId ?? playableInfo.Id;
        return _subsonicServerApi.GenerateUrlForStreamingPlayableAsync(apiId, maxBitrate, formatPreference);
    }

    public Task<Uri> GenerateUrlForArtworkAsync(Artwork artwork) => _subsonicServerApi.GenerateUrlForArtworkAsync(artwork.Id);

    public ResponseError? CheckForErrorResponse(ApiDataResponse response) => _subsonicServerApi.CheckForErrorResponse(response);

    public ILibrarySyncer CreateLibrarySyncer(Account account, LibraryStorage storage) =>
        new SubsonicLibrarySyncer(_subsonicServerApi, account, _networkMonitor, storage, _eventLogger);

    public Downloads.IDownloadManagerDelegate CreateArtworkDownloadDelegate() =>
        new SubsonicArtworkDownloadDelegate(_subsonicServerApi, _networkMonitor);

    public ArtworkRemoteInfo? ExtractArtworkInfoFromUrl(string urlString) => SubsonicServerApi.ExtractArtworkInfoFromUrl(urlString);

    public CleansedUrl Cleanse(Uri? url) => _subsonicServerApi.Cleanse(url);
}
