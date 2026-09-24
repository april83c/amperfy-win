namespace Amperfy.Core.Api.Ampache;

/// HTTP client of the Ampache XML API (port of AmpacheXmlServerApi.swift).
/// Handles the handshake (auth token caching and re-authentication before the session expires),
/// all request types, url generation for stream/download/artwork and error parsing.
public sealed class AmpacheXmlServerApi : IUrlCleanser
{
    private const string LogCategory = "Ampache";

    public const int MaxItemCountToPollAtOnce = 500;
    public static readonly string[] ApiPathComponents = ["server", "xml.server.php"];

    public string ClientApiVersion => "500000";

    private readonly object _lock = new();
    private string? _serverApiVersion;
    private LoginCredentials? _credentials;
    private AuthentificationHandshake? _authHandshake;

    private readonly EventLogger? _eventLogger;
    private readonly AmperfySettings _settings;
    private readonly HttpClient _httpClient;

    public AmpacheXmlServerApi(EventLogger? eventLogger, AmperfySettings settings, HttpClient? httpClient = null)
    {
        _eventLogger = eventLogger;
        _settings = settings;
        _httpClient = httpClient ?? AmperfyHttp.Client;
    }

    /// Creates the api with a custom message handler (tests).
    public AmpacheXmlServerApi(EventLogger? eventLogger, AmperfySettings settings, HttpMessageHandler handler)
        : this(eventLogger, settings, new HttpClient(handler))
    {
    }

    public string? ServerApiVersion
    {
        get { lock (_lock) return _serverApiVersion; }
        private set { lock (_lock) _serverApiVersion = value; }
    }

    private LoginCredentials? Credentials
    {
        get { lock (_lock) return _credentials; }
    }

    private AuthentificationHandshake? AuthHandshake
    {
        get { lock (_lock) return _authHandshake; }
        set { lock (_lock) _authHandshake = value; }
    }

    public AccountInfo? Account => Credentials is { } credentials ? AccountInfo.Create(credentials) : null;

    public IReadOnlyDictionary<string, string> HttpHeaders =>
        Credentials?.HttpHeaders ?? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>();

    private IReadOnlyDictionary<string, string>? BuildHttpHeaders(LoginCredentials? providedCredentials = null)
    {
        var headers = providedCredentials?.HttpHeaders ?? HttpHeaders;
        return headers.Count == 0 ? null : headers;
    }

    public async Task<bool> RequestServerPodcastSupportAsync()
    {
        _ = await ReauthenticateAsync();
        var isPodcastSupported = false;
        if (ServerApiVersion is { } serverApi && int.TryParse(serverApi, NumberStyles.Integer, CultureInfo.InvariantCulture, out var serverApiInt))
            isPodcastSupported = serverApiInt >= 420000;
        return isPodcastSupported;
    }

    public static ArtworkRemoteInfo? ExtractArtworkInfoFromUrl(string urlString)
    {
        if (string.IsNullOrEmpty(urlString)) return null;
        if (!Uri.TryCreate(urlString, UriKind.RelativeOrAbsolute, out _)) return null;
        var queryStart = urlString.IndexOf('?');
        if (queryStart < 0) return null;
        var query = urlString[(queryStart + 1)..];
        var fragmentStart = query.IndexOf('#');
        if (fragmentStart >= 0) query = query[..fragmentStart];
        var items = AmpacheUrlComponents.ParseQueryItems(query);
        var objectId = items?.FirstOrDefault(i => i.Key == "object_id").Value;
        var objectType = items?.FirstOrDefault(i => i.Key == "object_type").Value;
        if (objectId is null || objectType is null) return null;
        return new ArtworkRemoteInfo(objectId, objectType);
    }

    private static bool IsAuthenticated(AuthentificationHandshake auth) => auth.ReauthenticateTime >= DateTime.UtcNow;

    /// Ampache passphrase: sha256(unixtime + sha256(password)) where '+' denotes concatenation
    public static string GeneratePassphrase(string passwordHash, long timestamp) =>
        StringHasher.Sha256($"{timestamp.ToString(CultureInfo.InvariantCulture)}{passwordHash}");

    private AmpacheUrlComponents? CreateApiUrl(LoginCredentials? providedCredentials = null)
    {
        var localCredentials = providedCredentials ?? Credentials;
        if (localCredentials?.ActiveBackendServerUrl is not { } hostname) return null;
        var apiUrl = AmpacheUrlComponents.Create(hostname);
        if (apiUrl is null) return null;
        foreach (var component in ApiPathComponents) apiUrl.AppendPathComponent(component);
        return apiUrl;
    }

    private AmpacheUrlComponents CreateAuthApiUrlComponent(AuthentificationHandshake auth)
    {
        var urlComp = CreateApiUrl() ?? throw BackendError.InvalidUrl;
        urlComp.AddQueryItem("auth", auth.Token);
        return urlComp;
    }

    public void ProvideCredentials(LoginCredentials credentials)
    {
        lock (_lock)
        {
            _authHandshake = null;
            _credentials = credentials;
        }
    }

    private async Task<AuthentificationHandshake> AuthenticateAsync(LoginCredentials credentials)
    {
        try
        {
            var auth = await RequestAuthAsync(credentials);
            AuthHandshake = auth;
            return auth;
        }
        catch
        {
            AuthHandshake = null;
            throw;
        }
    }

    public async Task IsAuthenticationValidAsync(LoginCredentials credentials) => await RequestAuthAsync(credentials);

    private async Task<AuthentificationHandshake> RequestAuthAsync(LoginCredentials credentials)
    {
        var url = CreateAuthUrl(credentials);
        var response = await RequestAsync(url, BuildHttpHeaders(credentials));
        return ParseAuthResult(response);
    }

    private Uri CreateAuthUrl(LoginCredentials credentials)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var passphrase = GeneratePassphrase(credentials.PasswordHash, timestamp);
        var urlComp = CreateApiUrl(credentials) ?? throw BackendError.InvalidUrl;
        urlComp.AddQueryItem("action", "handshake");
        urlComp.AddQueryItem("auth", passphrase);
        urlComp.AddQueryItem("timestamp", timestamp);
        urlComp.AddQueryItem("version", ClientApiVersion);
        urlComp.AddQueryItem("user", credentials.Username);
        try
        {
            return urlComp.ToUri();
        }
        catch (BackendError)
        {
            AmperfyLog.Error(LogCategory, $"Ampache authentication url is invalid: {urlComp}");
            throw;
        }
    }

    public CleansedUrl Cleanse(Uri? url)
    {
        if (AmpacheUrlComponents.Create(url) is not { QueryItems: { } queryItems } urlComp) return new CleansedUrl("");
        urlComp.Host = "SERVERURL";
        urlComp.Port = null;
        var outputItems = new List<KeyValuePair<string, string?>>();
        foreach (var queryItem in queryItems)
        {
            outputItems.Add(queryItem.Key switch
            {
                "ssid" => new(queryItem.Key, "SSID"),
                "auth" => new(queryItem.Key, "AUTH"),
                "user" => new(queryItem.Key, "USER"),
                _ => queryItem,
            });
        }
        urlComp.QueryItems = outputItems;
        return new CleansedUrl(urlComp.ToString());
    }

    private AuthentificationHandshake ParseAuthResult(ApiDataResponse response)
    {
        var parser = new AuthParserDelegate();
        var success = parser.Parse(response.Data);
        if (parser.ServerApiVersion is { } serverApiVersion) ServerApiVersion = serverApiVersion;
        if (parser.ParserError is { } error)
        {
            AmperfyLog.Error(LogCategory, $"Error during AuthPars: {error.Message}");
            throw new ResponseError(ResponseErrorType.Xml, cleansedUrl: Cleanse(response.Url), data: response.Data);
        }
        if (success && parser.AuthHandshake is { } auth) return auth;
        AuthHandshake = null;
        AmperfyLog.Error(LogCategory, "Couldn't get a login token.");
        if (parser.Error is { } apiError) throw apiError;
        throw new AuthenticationError(AuthenticationErrorKind.NotAbleToLogin);
    }

    private async Task<AuthentificationHandshake> ReauthenticateAsync()
    {
        if (AuthHandshake is { } auth && IsAuthenticated(auth)) return auth;
        var cred = Credentials ?? throw BackendError.NoCredentials;
        return await AuthenticateAsync(cred);
    }

    // --- requests ------------------------------------------------------------------------------

    private Task<ApiDataResponse> RequestActionAsync(string action, params (string Name, string Value)[] items) =>
        RequestAsync(auth =>
        {
            var urlComp = CreateAuthApiUrlComponent(auth);
            urlComp.AddQueryItem("action", action);
            foreach (var (name, value) in items) urlComp.AddQueryItem(name, value);
            return urlComp.ToUri();
        });

    private static string Str(int value) => value.ToString(CultureInfo.InvariantCulture);

    public Task<ApiDataResponse> RequestCatalogsAsync() => RequestActionAsync("catalogs");

    public Task<ApiDataResponse> RequestGenresAsync() => RequestActionAsync("genres");

    public Task<ApiDataResponse> RequestArtistsAsync(int startIndex, int pollCount = MaxItemCountToPollAtOnce) =>
        RequestAsync(auth =>
        {
            var offset = startIndex < auth.ArtistCount ? startIndex : auth.ArtistCount - 1;
            var urlComp = CreateAuthApiUrlComponent(auth);
            urlComp.AddQueryItem("action", "artists");
            urlComp.AddQueryItem("offset", offset);
            urlComp.AddQueryItem("limit", pollCount);
            return urlComp.ToUri();
        });

    public Task<ApiDataResponse> RequestArtistWithinCatalogAsync(string id) =>
        RequestActionAsync("advanced_search", ("rule_1", "catalog"), ("rule_1_operator", "0"),
            ("rule_1_input", Str(int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0)), ("type", "artist"));

    public Task<ApiDataResponse> RequestArtistInfoAsync(string id) => RequestActionAsync("artist", ("filter", id));

    public Task<ApiDataResponse> RequestArtistAlbumsAsync(string id) => RequestActionAsync("artist_albums", ("filter", id));

    public Task<ApiDataResponse> RequestArtistSongsAsync(string id) => RequestActionAsync("artist_songs", ("filter", id));

    public Task<ApiDataResponse> RequestAlbumInfoAsync(string id) => RequestActionAsync("album", ("filter", id));

    public Task<ApiDataResponse> RequestAlbumSongsAsync(string id) => RequestActionAsync("album_songs", ("filter", id));

    public Task<ApiDataResponse> RequestSongInfoAsync(string id) => RequestActionAsync("song", ("filter", id));

    public Task<ApiDataResponse> RequestAlbumsAsync(int startIndex, int pollCount = MaxItemCountToPollAtOnce) =>
        RequestAsync(auth =>
        {
            var offset = startIndex < auth.AlbumCount ? startIndex : auth.AlbumCount - 1;
            var urlComp = CreateAuthApiUrlComponent(auth);
            urlComp.AddQueryItem("action", "albums");
            urlComp.AddQueryItem("offset", offset);
            urlComp.AddQueryItem("limit", pollCount);
            return urlComp.ToUri();
        });

    public Task<ApiDataResponse> RequestRandomSongsAsync(int count) =>
        RequestActionAsync("playlist_generate", ("mode", "random"), ("format", "song"), ("limit", Str(count)));

    public Task<ApiDataResponse> RequestPodcastEpisodeDeleteAsync(string id) => RequestActionAsync("podcast_episode_delete", ("filter", id));

    public Task<ApiDataResponse> RequestFavoriteArtistsAsync() =>
        RequestActionAsync("advanced_search", ("rule_1", "favorite"), ("rule_1_operator", "0"), ("rule_1_input", ""), ("type", "artist"));

    public Task<ApiDataResponse> RequestFavoriteAlbumsAsync() =>
        RequestActionAsync("advanced_search", ("rule_1", "favorite"), ("rule_1_operator", "0"), ("rule_1_input", ""), ("type", "album"));

    public Task<ApiDataResponse> RequestFavoriteSongsAsync() =>
        RequestActionAsync("advanced_search", ("rule_1", "favorite"), ("rule_1_operator", "0"), ("rule_1_input", ""), ("type", "song"));

    public Task<ApiDataResponse> RequestNewestAlbumsAsync(int offset, int count) =>
        RequestActionAsync("stats", ("type", "album"), ("filter", "newest"), ("limit", Str(count)), ("offset", Str(offset)));

    public Task<ApiDataResponse> RequestRecentAlbumsAsync(int offset, int count) =>
        RequestActionAsync("stats", ("type", "album"), ("filter", "recent"), ("limit", Str(count)), ("offset", Str(offset)));

    public Task<ApiDataResponse> RequestPlaylistsAsync() => RequestActionAsync("playlists");

    public Task<ApiDataResponse> RequestPlaylistAsync(string id) => RequestActionAsync("playlist", ("filter", id));

    public Task<ApiDataResponse> RequestPlaylistSongsAsync(string id) => RequestActionAsync("playlist_songs", ("filter", id));

    public Task<ApiDataResponse> RequestPlaylistCreateAsync(string name) => RequestActionAsync("playlist_create", ("name", name), ("type", "private"));

    public Task<ApiDataResponse> RequestPlaylistDeleteAsync(string id) => RequestActionAsync("playlist_delete", ("filter", id));

    public Task<ApiDataResponse> RequestPlaylistAddSongAsync(string playlistId, string songId) =>
        RequestActionAsync("playlist_add_song", ("filter", playlistId), ("song", songId));

    public Task<ApiDataResponse> RequestPlaylistDeleteItemAsync(string id, int index) =>
        RequestActionAsync("playlist_remove_song", ("filter", id), ("track", Str(index + 1)));

    public Task<ApiDataResponse> RequestPlaylistEditOnlyNameAsync(string id, string name) =>
        RequestActionAsync("playlist_edit", ("filter", id), ("name", name));

    public Task<ApiDataResponse> RequestPlaylistEditAsync(string id, IReadOnlyList<string> songsIds) =>
        RequestActionAsync("playlist_edit", ("filter", id), ("items", string.Join(",", songsIds)),
            ("tracks", string.Join(",", Enumerable.Range(1, songsIds.Count).Select(Str))));

    public Task<ApiDataResponse> RequestRadiosAsync() => RequestActionAsync("live_streams");

    public Task<ApiDataResponse> RequestPodcastsAsync() => RequestActionAsync("podcasts");

    public Task<ApiDataResponse> RequestPodcastEpisodesAsync(string id, int? limit = null) =>
        limit is { } l
            ? RequestActionAsync("podcast_episodes", ("filter", id), ("limit", Str(l)))
            : RequestActionAsync("podcast_episodes", ("filter", id));

    public Task<ApiDataResponse> RequestRecordPlayAsync(string songId, DateTime? date) =>
        RequestAsync(auth =>
        {
            var urlComp = CreateAuthApiUrlComponent(auth);
            urlComp.AddQueryItem("action", "record_play");
            if (Credentials?.Username is { } username) urlComp.AddQueryItem("user", username);
            if (date is { } d) urlComp.AddQueryItem("date", new DateTimeOffset(d.ToUniversalTime()).ToUnixTimeSeconds());
            urlComp.AddQueryItem("id", songId);
            return urlComp.ToUri();
        });

    public Task<ApiDataResponse> RequestRateSongAsync(string songId, int rating) =>
        RequestActionAsync("rate", ("type", "song"), ("id", songId), ("rating", Str(rating)));

    public Task<ApiDataResponse> RequestRateAlbumAsync(string albumId, int rating) =>
        RequestActionAsync("rate", ("type", "album"), ("id", albumId), ("rating", Str(rating)));

    public Task<ApiDataResponse> RequestRateArtistAsync(string artistId, int rating) =>
        RequestActionAsync("rate", ("type", "artist"), ("id", artistId), ("rating", Str(rating)));

    public Task<ApiDataResponse> RequestSetFavoriteSongAsync(string songId, bool isFavorite) =>
        RequestActionAsync("flag", ("type", "song"), ("id", songId), ("flag", isFavorite ? "1" : "0"));

    public Task<ApiDataResponse> RequestSetFavoriteAlbumAsync(string albumId, bool isFavorite) =>
        RequestActionAsync("flag", ("type", "album"), ("id", albumId), ("flag", isFavorite ? "1" : "0"));

    public Task<ApiDataResponse> RequestSetFavoriteArtistAsync(string artistId, bool isFavorite) =>
        RequestActionAsync("flag", ("type", "artist"), ("id", artistId), ("flag", isFavorite ? "1" : "0"));

    public Task<ApiDataResponse> RequestSearchArtistsAsync(string searchText) =>
        RequestActionAsync("artists", ("filter", searchText), ("limit", "40"));

    public Task<ApiDataResponse> RequestSearchAlbumsAsync(string searchText) =>
        RequestActionAsync("albums", ("filter", searchText), ("limit", "40"));

    public Task<ApiDataResponse> RequestSearchSongsAsync(string searchText) =>
        RequestActionAsync("search_songs", ("filter", searchText), ("limit", "40"));

    public Task<ApiDataResponse> RequestSimilarSongsAsync(string id, int count = 50) =>
        RequestActionAsync("get_similar", ("type", "song"), ("filter", id), ("limit", Str(count)));

    /// GET request. Like Alamofire's validate()+responseData: the body is returned when the server
    /// delivered one (even for an error status); without a body an HTTP error is thrown.
    private async Task<ApiDataResponse> RequestAsync(Uri url, IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = AmperfyHttp.CreateGet(url, headers);
        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        var data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && !GenericXmlParser.LooksLikeXml(data))
            throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).", null, response.StatusCode);
        return new ApiDataResponse(data, url);
    }

    public Task<AuthentificationHandshake> RequestLibraryMetaDataAsync() => ReauthenticateAsync();

    public async Task<Uri> GenerateUrlForDownloadingPlayableAsync(bool isSong, string id)
    {
        var auth = await ReauthenticateAsync();
        var urlComp = CreateAuthApiUrlComponent(auth);
        urlComp.AddQueryItem("action", "download");
        urlComp.AddQueryItem("type", isSong ? "song" : "podcast_episode");
        urlComp.AddQueryItem("id", id);
        urlComp.AddQueryItem("format", _settings.User.CacheTranscodingFormatPreference == CacheTranscodingFormatPreference.Mp3 ? "mp3" : "raw");
        return urlComp.ToUri();
    }

    public async Task<Uri> GenerateUrlForStreamingPlayableAsync(bool isSong, string id, StreamingMaxBitratePreference maxBitrate, StreamingFormatPreference formatPreference)
    {
        var auth = await ReauthenticateAsync();
        var urlComp = CreateAuthApiUrlComponent(auth);
        urlComp.AddQueryItem("action", "stream");
        urlComp.AddQueryItem("type", isSong ? "song" : "podcast_episode");
        urlComp.AddQueryItem("id", id);
        switch (formatPreference)
        {
            case StreamingFormatPreference.Mp3:
                urlComp.AddQueryItem("format", "mp3");
                break;
            case StreamingFormatPreference.Raw:
                urlComp.AddQueryItem("format", "raw");
                break;
            case StreamingFormatPreference.ServerConfig:
                break; // do nothing
        }
        if (maxBitrate != StreamingMaxBitratePreference.NoLimit) urlComp.AddQueryItem("bitrate", (int)maxBitrate);
        urlComp.AddQueryItem("length", 1);
        return urlComp.ToUri();
    }

    public async Task<Uri> GenerateUrlForArtworkAsync(ArtworkRemoteInfo artworkRemoteInfo)
    {
        var hostname = Credentials?.ActiveBackendServerUrl ?? throw BackendError.NoCredentials;
        var urlComp = AmpacheUrlComponents.Create(hostname) ?? throw BackendError.InvalidUrl;
        urlComp.AppendPathComponent("image.php");
        var auth = await ReauthenticateAsync();
        urlComp.AddQueryItem("auth", auth.Token);
        urlComp.AddQueryItem("object_id", artworkRemoteInfo.Id);
        urlComp.AddQueryItem("object_type", artworkRemoteInfo.Type);
        return urlComp.ToUri();
    }

    public ResponseError? CheckForErrorResponse(ApiDataResponse response)
    {
        if (!GenericXmlParser.LooksLikeXml(response.Data)) return null;
        var errorParser = new AmpacheXmlParser();
        errorParser.Parse(response.Data);
        if (errorParser.Error is not { } ampacheError) return null;
        return ampacheError.ToResponseError(Cleanse(response.Url), response.Data);
    }

    /// Replaces the auth/ssid token of an (older) api url with the current one.
    public async Task<Uri> UpdateUrlTokenAsync(string urlString)
    {
        var auth = await ReauthenticateAsync();
        if (AmpacheUrlComponents.Create(urlString) is not { QueryItems: { } queryItems } inputUrlComp) throw BackendError.InvalidUrl;
        AmpacheUrlComponents outputUrlComp;
        try { outputUrlComp = CreateAuthApiUrlComponent(auth); }
        catch (BackendError) { throw BackendError.InvalidUrl; }

        var outputItems = queryItems
            .Select(i => i.Key is "ssid" or "auth" ? new KeyValuePair<string, string?>(i.Key, auth.Token) : i)
            .ToList();
        outputUrlComp.QueryItems = outputItems;
        outputUrlComp.Path = inputUrlComp.Path;
        return outputUrlComp.ToUri();
    }

    private async Task<ApiDataResponse> RequestAsync(Func<AuthentificationHandshake, Uri> urlCreation)
    {
        var auth = await ReauthenticateAsync();
        var url = urlCreation(auth);
        return await RequestAsync(url, BuildHttpHeaders());
    }
}
