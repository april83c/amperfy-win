using System.Net;
using Amperfy.Core.Api.Ampache;
using Amperfy.Core.Api.Subsonic;
using Amperfy.Core.Downloads;

namespace Amperfy.Core.Api;

/// Selects the active server API (Ampache / Subsonic / Subsonic legacy) for an account and
/// detects it on login (port of BackendProxy.swift).
public sealed class BackendProxy : IBackendApi
{
    private readonly AmpacheApi _ampacheApi;
    private readonly SubsonicApi _subsonicApi;
    private readonly SubsonicApi _subsonicLegacyApi;
    private readonly HttpClient _httpClient;
    private BackendApiType _selectedApi = BackendApiType.Ampache;
    private IDownloadManagerDelegate? _artworkDownloadDelegate;

    public BackendProxy(INetworkMonitor networkMonitor, EventLogger eventLogger, AmperfySettings settings, HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? AmperfyHttp.Client;
        _ampacheApi = new AmpacheApi(new AmpacheXmlServerApi(eventLogger, settings, _httpClient), networkMonitor, eventLogger);
        _subsonicApi = new SubsonicApi(new SubsonicServerApi(eventLogger, settings, _httpClient), networkMonitor, eventLogger);
        _subsonicApi.SetAuthType(SubsonicApiAuthType.AutoDetect);
        _subsonicLegacyApi = new SubsonicApi(new SubsonicServerApi(eventLogger, settings, _httpClient), networkMonitor, eventLogger);
        _subsonicLegacyApi.SetAuthType(SubsonicApiAuthType.Legacy);
    }

    public BackendApiType SelectedApi
    {
        get => _selectedApi;
        set
        {
            AmperfyLog.Info("BackendProxy", $"{value.Description()} is active backend api");
            _selectedApi = value;
            _artworkDownloadDelegate = ActiveApi.CreateArtworkDownloadDelegate();
        }
    }

    private IBackendApi ActiveApi => _selectedApi switch
    {
        BackendApiType.Subsonic => _subsonicApi,
        BackendApiType.SubsonicLegacy => _subsonicLegacyApi,
        _ => _ampacheApi,
    };

    /// Checks reachability and detects the api type that accepts the credentials.
    public async Task<BackendApiType> LoginAsync(BackendApiType apiType, LoginCredentials credentials)
    {
        await CheckServerReachabilityAsync(credentials);
        if (apiType is BackendApiType.NotDetected or BackendApiType.Ampache)
        {
            try { await _ampacheApi.IsAuthenticationValidAsync(credentials); return BackendApiType.Ampache; }
            catch (Exception ex) { AmperfyLog.Info("BackendProxy", $"Ampache login failed: {ex.Message}"); }
        }
        if (apiType is BackendApiType.NotDetected or BackendApiType.Subsonic)
        {
            try { await _subsonicApi.IsAuthenticationValidAsync(credentials); return BackendApiType.Subsonic; }
            catch (Exception ex) { AmperfyLog.Info("BackendProxy", $"Subsonic login failed: {ex.Message}"); }
        }
        if (apiType is BackendApiType.NotDetected or BackendApiType.SubsonicLegacy)
        {
            try { await _subsonicLegacyApi.IsAuthenticationValidAsync(credentials); return BackendApiType.SubsonicLegacy; }
            catch (Exception ex) { AmperfyLog.Info("BackendProxy", $"Subsonic legacy login failed: {ex.Message}"); }
        }
        throw new AuthenticationError(AuthenticationErrorKind.NotAbleToLogin);
    }

    private async Task CheckServerReachabilityAsync(LoginCredentials credentials)
    {
        var urlString = string.IsNullOrEmpty(credentials.ActiveBackendServerUrl) ? credentials.ServerUrl : credentials.ActiveBackendServerUrl;
        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var url) || (url.Scheme != "http" && url.Scheme != "https"))
            throw new AuthenticationError(AuthenticationErrorKind.InvalidUrl);
        try
        {
            using var request = AmperfyHttp.CreateGet(url, credentials.HttpHeaders);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var status = (int)response.StatusCode;
            // ignore 401 Unauthorized: the root website may require basic auth while the API doesn't.
            // ignore 404: API-only servers (or reverse proxies) may not serve anything at the root.
            if (status >= 400 && response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.NotFound))
                throw new AuthenticationError(AuthenticationErrorKind.RequestStatusError, status.ToString(CultureInfo.InvariantCulture));
        }
        catch (HttpRequestException ex)
        {
            throw new AuthenticationError(AuthenticationErrorKind.DownloadError, ex.Message);
        }
        catch (TaskCanceledException ex)
        {
            throw new AuthenticationError(AuthenticationErrorKind.DownloadError, $"Timeout: {ex.Message}");
        }
        AmperfyLog.Info("BackendProxy", "Server url is reachable.");
    }

    // IBackendApi
    public string ClientApiVersion => ActiveApi.ClientApiVersion;
    public string ServerApiVersion => ActiveApi.ServerApiVersion;
    public IReadOnlyDictionary<string, string> HttpHeaders => ActiveApi.HttpHeaders;
    public void ProvideCredentials(LoginCredentials credentials) => ActiveApi.ProvideCredentials(credentials);
    public Task IsAuthenticationValidAsync(LoginCredentials credentials) => ActiveApi.IsAuthenticationValidAsync(credentials);
    public Task<Uri> GenerateUrlForDownloadingPlayableAsync(AbstractPlayableInfo playableInfo) => ActiveApi.GenerateUrlForDownloadingPlayableAsync(playableInfo);
    public Task<Uri> GenerateUrlForStreamingPlayableAsync(AbstractPlayableInfo playableInfo, StreamingMaxBitratePreference maxBitrate, StreamingFormatPreference formatPreference) =>
        ActiveApi.GenerateUrlForStreamingPlayableAsync(playableInfo, maxBitrate, formatPreference);
    public Task<Uri> GenerateUrlForArtworkAsync(Artwork artwork) => ActiveApi.GenerateUrlForArtworkAsync(artwork);
    public ResponseError? CheckForErrorResponse(ApiDataResponse response) => ActiveApi.CheckForErrorResponse(response);
    public ILibrarySyncer CreateLibrarySyncer(Account account, LibraryStorage storage) => ActiveApi.CreateLibrarySyncer(account, storage);
    public IDownloadManagerDelegate CreateArtworkDownloadDelegate() => ActiveApi.CreateArtworkDownloadDelegate();
    public IDownloadManagerDelegate GetActiveArtworkDownloadDelegate() => _artworkDownloadDelegate ??= ActiveApi.CreateArtworkDownloadDelegate();
    public ArtworkRemoteInfo? ExtractArtworkInfoFromUrl(string urlString) => ActiveApi.ExtractArtworkInfoFromUrl(urlString);
    public CleansedUrl Cleanse(Uri? url) => ActiveApi.Cleanse(url);
}

/// Forwards to the library syncer of the currently selected api (port of LibrarySyncerProxy.swift).
public sealed class LibrarySyncerProxy : ILibrarySyncer
{
    private readonly IBackendApi _backendApi;
    private readonly Account _account;
    private readonly LibraryStorage _library;

    public LibrarySyncerProxy(IBackendApi backendApi, Account account, LibraryStorage library)
    {
        _backendApi = backendApi;
        _account = account;
        _library = library;
    }

    private ILibrarySyncer Active => _backendApi.CreateLibrarySyncer(_account, _library);

    public Task SyncInitialAsync(ISyncCallbacks? statusNotifier) => Active.SyncInitialAsync(statusNotifier);
    public Task SyncAsync(Genre genre) => Active.SyncAsync(genre);
    public Task SyncAsync(Artist artist) => Active.SyncAsync(artist);
    public Task SyncAsync(Album album) => Active.SyncAsync(album);
    public Task SyncAsync(Song song) => Active.SyncAsync(song);
    public Task SyncAsync(Podcast podcast) => Active.SyncAsync(podcast);
    public Task SyncNewestAlbumsAsync(int offset, int count) => Active.SyncNewestAlbumsAsync(offset, count);
    public Task SyncRecentAlbumsAsync(int offset, int count) => Active.SyncRecentAlbumsAsync(offset, count);
    public Task SyncNewestPodcastEpisodesAsync() => Active.SyncNewestPodcastEpisodesAsync();
    public Task SyncFavoriteLibraryElementsAsync() => Active.SyncFavoriteLibraryElementsAsync();
    public Task SyncRadiosAsync() => Active.SyncRadiosAsync();
    public Task SyncDownPlaylistsWithoutSongsAsync() => Active.SyncDownPlaylistsWithoutSongsAsync();
    public Task SyncDownAsync(Playlist playlist) => Active.SyncDownAsync(playlist);
    public Task SyncUploadPlaylistNameAsync(Playlist playlist) => Active.SyncUploadPlaylistNameAsync(playlist);
    public Task SyncUploadPlaylistAddSongsAsync(Playlist playlist, IReadOnlyList<Song> songs) => Active.SyncUploadPlaylistAddSongsAsync(playlist, songs);
    public Task SyncUploadPlaylistDeleteSongAsync(Playlist playlist, int index) => Active.SyncUploadPlaylistDeleteSongAsync(playlist, index);
    public Task SyncUploadPlaylistOrderAsync(Playlist playlist) => Active.SyncUploadPlaylistOrderAsync(playlist);
    public Task SyncUploadPlaylistDeleteAsync(string playlistId) => Active.SyncUploadPlaylistDeleteAsync(playlistId);
    public Task SyncDownPodcastsWithoutEpisodesAsync() => Active.SyncDownPodcastsWithoutEpisodesAsync();
    public Task SearchArtistsAsync(string searchText) => Active.SearchArtistsAsync(searchText);
    public Task SearchAlbumsAsync(string searchText) => Active.SearchAlbumsAsync(searchText);
    public Task SearchSongsAsync(string searchText) => Active.SearchSongsAsync(searchText);
    public Task SyncMusicFoldersAsync() => Active.SyncMusicFoldersAsync();
    public Task SyncIndexesAsync(MusicFolder musicFolder) => Active.SyncIndexesAsync(musicFolder);
    public Task SyncAsync(MusicDirectory directory) => Active.SyncAsync(directory);
    public Task RequestRandomSongsAsync(Playlist playlist, int count) => Active.RequestRandomSongsAsync(playlist, count);
    public Task<List<Song>> RequestSimilarSongsAsync(Song song, int count) => Active.RequestSimilarSongsAsync(song, count);
    public Task RequestPodcastEpisodeDeleteAsync(PodcastEpisode podcastEpisode) => Active.RequestPodcastEpisodeDeleteAsync(podcastEpisode);
    public Task SyncNowPlayingAsync(Song song, NowPlayingSongPosition songPosition) => Active.SyncNowPlayingAsync(song, songPosition);
    public Task ScrobbleAsync(Song song, DateTime? date) => Active.ScrobbleAsync(song, date);
    public Task SetRatingAsync(Song song, int rating) => Active.SetRatingAsync(song, rating);
    public Task SetRatingAsync(Album album, int rating) => Active.SetRatingAsync(album, rating);
    public Task SetRatingAsync(Artist artist, int rating) => Active.SetRatingAsync(artist, rating);
    public Task SetFavoriteAsync(Song song, bool isFavorite) => Active.SetFavoriteAsync(song, isFavorite);
    public Task SetFavoriteAsync(Album album, bool isFavorite) => Active.SetFavoriteAsync(album, isFavorite);
    public Task SetFavoriteAsync(Artist artist, bool isFavorite) => Active.SetFavoriteAsync(artist, isFavorite);
    public Task<LyricsList> ParseLyricsAsync(string relFilePath) => Active.ParseLyricsAsync(relFilePath);
}
