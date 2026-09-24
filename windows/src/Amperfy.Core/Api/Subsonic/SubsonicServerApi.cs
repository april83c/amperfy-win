using System.Net;

namespace Amperfy.Core.Api.Subsonic;

/// Cached result of the "getOpenSubsonicExtensions" request.
public sealed class OpenSubsonicExtensionsSupport
{
    public OpenSubsonicExtensionsResponse? ExtensionResponse { get; set; }
    public bool IsSupported { get; set; }
}

/// HTTP client of the Subsonic REST API (port of SubsonicServerApi.swift). Thread safe: it
/// does not touch the storage, so it can be called from any thread.
public sealed class SubsonicServerApi : IUrlCleanser
{
    private const string LogCategory = "Subsonic";

    public static readonly SubsonicVersion DefaultClientApiVersionWithToken = new(1, 13, 0);
    public static readonly SubsonicVersion DefaultClientApiVersionPreToken = new(1, 11, 0);

    private readonly HttpClient _httpClient;
    private readonly EventLogger? _eventLogger;
    private readonly AmperfySettings _settings;

    private volatile SubsonicVersion? _serverApiVersion;
    private volatile SubsonicVersion? _clientApiVersion;
    private volatile LoginCredentials? _credentials;
    private volatile OpenSubsonicExtensionsSupport? _openSubsonicExtensionsSupport;
    private int _authType = (int)SubsonicApiAuthType.AutoDetect;

    public SubsonicServerApi(EventLogger? eventLogger, AmperfySettings settings, HttpClient? httpClient = null)
    {
        _eventLogger = eventLogger;
        _settings = settings;
        _httpClient = httpClient ?? AmperfyHttp.Client;
    }

    public SubsonicServerApi(EventLogger? eventLogger, AmperfySettings settings, HttpMessageHandler handler)
        : this(eventLogger, settings, new HttpClient(handler, disposeHandler: false))
    {
    }

    public SubsonicVersion? ServerApiVersion
    {
        get => _serverApiVersion;
        internal set => _serverApiVersion = value;
    }

    public SubsonicVersion? ClientApiVersion
    {
        get => _clientApiVersion;
        internal set => _clientApiVersion = value;
    }

    public EventLogger? EventLogger => _eventLogger;

    public SubsonicApiAuthType AuthType => (SubsonicApiAuthType)Volatile.Read(ref _authType);

    public void SetAuthType(SubsonicApiAuthType newAuthType) => Volatile.Write(ref _authType, (int)newAuthType);

    /// Account of the provided credentials (null if no credentials are provided yet).
    public AccountInfo? Account => _credentials is { } credentials ? AccountInfo.Create(credentials) : null;

    private SubsonicVersion AuthTypeBasedClientApiVersion =>
        AuthType == SubsonicApiAuthType.Legacy ? DefaultClientApiVersionPreToken : DefaultClientApiVersionWithToken;

    public static ArtworkRemoteInfo? ExtractArtworkInfoFromUrl(string urlString)
    {
        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var url)) return null;
        var id = ParseQuery(url.Query).FirstOrDefault(q => q.Name == "id").Value;
        return id is null ? null : new ArtworkRemoteInfo(id, "");
    }

    /// token = md5(password + salt) as lower case hex string (UTF-8).
    private static string GenerateAuthenticationToken(string password, string salt) => StringHasher.Md5Hex($"{password}{salt}");

    private async Task<SubsonicVersion> GetCachedServerApiVersionOrRequestItAsync(LoginCredentials? providedCredentials = null)
    {
        if (_serverApiVersion is { } serverVersion) return serverVersion;
        return await RequestServerApiVersionAsync(providedCredentials).ConfigureAwait(false);
    }

    private async Task<SubsonicVersion> DetermineApiVersionToUseAsync(LoginCredentials? providedCredentials = null)
    {
        if (_clientApiVersion is { } clientApiVersion) return clientApiVersion;
        var serverVersion = await GetCachedServerApiVersionOrRequestItAsync(providedCredentials).ConfigureAwait(false);
        AmperfyLog.Info(LogCategory, $"Server API version is '{serverVersion.Description}'");
        SubsonicVersion version;
        if (AuthType == SubsonicApiAuthType.Legacy)
        {
            version = DefaultClientApiVersionPreToken;
            AmperfyLog.Info(LogCategory, "Client API legacy login");
        }
        else
        {
            version = DefaultClientApiVersionWithToken;
            AmperfyLog.Info(LogCategory, $"Client API version is '{version.Description}'");
        }
        _clientApiVersion = version;
        return version;
    }

    // --- URL creation -------------------------------------------------------------------------

    /// URL under construction (URLComponents replacement).
    private sealed class UrlComponents
    {
        private readonly string _base;
        private readonly List<(string Name, string Value)> _query = [];
        private readonly string _existingQuery;

        public UrlComponents(string baseWithoutQuery, string existingQuery)
        {
            _base = baseWithoutQuery;
            _existingQuery = existingQuery;
        }

        public void AddQueryItem(string name, string value) => _query.Add((name, value));
        public void AddQueryItem(string name, int value) => _query.Add((name, value.ToString(CultureInfo.InvariantCulture)));
        public void AddQueryItem(string name, long value) => _query.Add((name, value.ToString(CultureInfo.InvariantCulture)));

        public Uri CreateUrl()
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(_existingQuery)) parts.Add(_existingQuery);
            parts.AddRange(_query.Select(q => $"{Uri.EscapeDataString(q.Name)}={Uri.EscapeDataString(q.Value)}"));
            var urlString = parts.Count > 0 ? $"{_base}?{string.Join("&", parts)}" : _base;
            if (!Uri.TryCreate(urlString, UriKind.Absolute, out var url)) throw BackendError.InvalidUrl;
            return url;
        }
    }

    private UrlComponents? CreateBasicApiUrlComponent(string forAction, LoginCredentials? providedCredentials = null)
    {
        var localCredentials = providedCredentials ?? _credentials;
        if (localCredentials is null) return null;
        var hostname = string.IsNullOrEmpty(localCredentials.ActiveBackendServerUrl) ? localCredentials.ServerUrl : localCredentials.ActiveBackendServerUrl;
        if (string.IsNullOrEmpty(hostname) || !Uri.TryCreate(hostname, UriKind.Absolute, out var apiUrl)) return null;
        if (apiUrl.Scheme != Uri.UriSchemeHttp && apiUrl.Scheme != Uri.UriSchemeHttps) return null;
        var path = apiUrl.AbsolutePath.TrimEnd('/') + "/rest/" + Uri.EscapeDataString($"{forAction}.view");
        return new UrlComponents(apiUrl.GetLeftPart(UriPartial.Authority) + path, apiUrl.Query.TrimStart('?'));
    }

    private UrlComponents CreateAuthApiUrlComponent(SubsonicVersion version, string forAction, LoginCredentials? providedCredentials = null)
    {
        var localCredentials = providedCredentials ?? _credentials;
        if (localCredentials is null) throw BackendError.InvalidUrl;
        var urlComp = CreateBasicApiUrlComponent(forAction, localCredentials) ?? throw BackendError.InvalidUrl;
        var username = localCredentials.Username;
        var password = localCredentials.Password;

        urlComp.AddQueryItem("u", username);
        urlComp.AddQueryItem("v", version.Description);
        urlComp.AddQueryItem("c", "Amperfy");

        if (version < SubsonicVersion.AuthenticationTokenRequiredServerApi)
        {
            urlComp.AddQueryItem("p", password);
        }
        else
        {
            var salt = StringExtensions.GenerateRandomString(16);
            var authenticationToken = GenerateAuthenticationToken(password, salt);
            urlComp.AddQueryItem("t", authenticationToken);
            urlComp.AddQueryItem("s", salt);
        }
        return urlComp;
    }

    private UrlComponents CreateAuthApiUrlComponent(SubsonicVersion version, string forAction, string id)
    {
        var urlComp = CreateAuthApiUrlComponent(version, forAction);
        urlComp.AddQueryItem("id", id);
        return urlComp;
    }

    public IReadOnlyDictionary<string, string> HttpHeaders =>
        _credentials?.HttpHeaders ?? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>();

    private IReadOnlyDictionary<string, string>? BuildHttpHeaders(LoginCredentials? providedCredentials = null)
    {
        IReadOnlyDictionary<string, string> headers = providedCredentials?.HttpHeaders ?? HttpHeaders;
        return headers.Count == 0 ? null : headers;
    }

    public void ProvideCredentials(LoginCredentials credentials) => _credentials = credentials;

    public CleansedUrl Cleanse(Uri? url)
    {
        if (url is null || !url.IsAbsoluteUri || string.IsNullOrEmpty(url.Query)) return new CleansedUrl("");
        var outputItems = new List<string>();
        foreach (var part in url.Query.TrimStart('?').Split('&'))
        {
            var idx = part.IndexOf('=');
            var name = Uri.UnescapeDataString(idx >= 0 ? part[..idx] : part);
            var replacement = name switch
            {
                "p" => "PASSWORD",
                "t" => "AUTHTOKEN",
                "s" => "SALT",
                "u" => "USER",
                _ => null,
            };
            outputItems.Add(replacement is null ? part : $"{part[..(idx >= 0 ? idx : part.Length)]}={replacement}");
        }
        return new CleansedUrl($"{url.Scheme}://SERVERURL{url.AbsolutePath}?{string.Join("&", outputItems)}");
    }

    internal static List<(string Name, string? Value)> ParseQuery(string query)
    {
        var result = new List<(string, string?)>();
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx < 0) result.Add((Uri.UnescapeDataString(part), null));
            else result.Add((Uri.UnescapeDataString(part[..idx]), Uri.UnescapeDataString(part[(idx + 1)..])));
        }
        return result;
    }

    // --- authentication / version -------------------------------------------------------------

    public async Task IsAuthenticationValidAsync(LoginCredentials credentials)
    {
        var version = await DetermineApiVersionToUseAsync(credentials).ConfigureAwait(false);
        var urlComp = CreateAuthApiUrlComponent(version, "ping", credentials);
        var response = await RequestAsync(urlComp.CreateUrl(), BuildHttpHeaders(credentials)).ConfigureAwait(false);

        var parserDelegate = new SsPingParserDelegate();
        var success = parserDelegate.Parse(response.Data);
        if (parserDelegate.ParserError is { } error)
        {
            AmperfyLog.Error(LogCategory, $"Error during login parsing: {error.Message}");
            throw new AuthenticationError(AuthenticationErrorKind.NotAbleToLogin);
        }
        if (success && parserDelegate.IsAuthValid) return;
        AmperfyLog.Error(LogCategory, "Couldn't login.");
        throw new AuthenticationError(AuthenticationErrorKind.NotAbleToLogin);
    }

    private async Task<SubsonicVersion> RequestServerApiVersionAsync(LoginCredentials? providedCredentials = null)
    {
        UrlComponents urlComp;
        try
        {
            urlComp = CreateAuthApiUrlComponent(AuthTypeBasedClientApiVersion, "ping", providedCredentials);
        }
        catch
        {
            throw BackendError.InvalidUrl;
        }
        var url = urlComp.CreateUrl();
        var response = await RequestAsync(url, BuildHttpHeaders(providedCredentials)).ConfigureAwait(false);

        var parserDelegate = new SsPingParserDelegate();
        parserDelegate.Parse(response.Data);
        if (parserDelegate.ServerApiVersion is not { } serverApiVersionString)
        {
            throw new ResponseError(ResponseErrorType.Xml, cleansedUrl: Cleanse(response.Url), data: response.Data);
        }
        if (SubsonicVersion.Create(serverApiVersionString) is not { } serverApiVersion)
        {
            AmperfyLog.Info(LogCategory, $"The server API version '{serverApiVersionString}' could not be parsed to 'SubsonicVersion'");
            throw new ResponseError(ResponseErrorType.Xml, cleansedUrl: Cleanse(response.Url), data: response.Data);
        }
        _serverApiVersion = serverApiVersion;
        return serverApiVersion;
    }

    public async Task<bool> RequestServerPodcastSupportAsync()
    {
        await DetermineApiVersionToUseAsync().ConfigureAwait(false);
        var isPodcastSupported = false;
        if (_serverApiVersion is { } serverApi) isPodcastSupported = serverApi >= new SubsonicVersion(1, 9, 0);
        if (!isPodcastSupported) return false;
        try
        {
            await RequestPodcastsAsync().ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private Task<ApiDataResponse> RequestOpenSubsonicExtensionsAsync() =>
        RequestAsync(version => CreateAuthApiUrlComponent(version, "getOpenSubsonicExtensions").CreateUrl());

    public async Task<bool> IsOpenSubsonicExtensionSupportedAsync(OpenSubsonicExtension oSsExtension)
    {
        var extensionName = oSsExtension.RawValue();
        // the member variable will be set after the server has been requested
        // here we already asked the server for its support and use the cached response
        if (_openSubsonicExtensionsSupport is { } oSsExtSupport)
        {
            if (!oSsExtSupport.IsSupported)
            {
                AmperfyLog.Info(LogCategory, $"No OpenSubsonicExtensions supported ({extensionName})!");
                return false;
            }
            if (oSsExtSupport.ExtensionResponse is { } cachedResponse)
            {
                var isExtensionSupported = cachedResponse.SupportedExtensions.Contains(extensionName);
                AmperfyLog.Info(LogCategory, $"OpenSubsonicExtension {extensionName} supported: {(isExtensionSupported ? "yes" : "no")}");
                return isExtensionSupported;
            }
            AmperfyLog.Info(LogCategory, $"No OpenSubsonicExtensions supported ({extensionName})!");
            return false;
        }

        // we haven't yet requested the server for OpenSubsonicExtensions support
        try
        {
            var response = await RequestOpenSubsonicExtensionsAsync().ConfigureAwait(false);
            var parserDelegate = new SsOpenSubsonicExtensionsParserDelegate();
            if (!parserDelegate.Parse(response.Data))
            {
                AmperfyLog.Info(LogCategory, $"No OpenSubsonicExtensions supported ({extensionName})!");
                return false;
            }
            var support = new OpenSubsonicExtensionsSupport
            {
                IsSupported = parserDelegate.OpenSubsonicExtensionsResponse.SupportedExtensions.Count > 0,
                ExtensionResponse = parserDelegate.OpenSubsonicExtensionsResponse,
            };
            _openSubsonicExtensionsSupport = support;
            var availableExtensions = support.ExtensionResponse.SupportedExtensions;
            AmperfyLog.Info(LogCategory, $"OpenSubsonicExtensions supported: {string.Join(", ", availableExtensions)}");
            var isSpecificExtensionSupported = availableExtensions.Contains(extensionName);
            AmperfyLog.Info(LogCategory, $"OpenSubsonicExtensions {extensionName} supported: {(isSpecificExtensionSupported ? "yes" : "no")}");
            return isSpecificExtensionSupported;
        }
        catch (Exception)
        {
            AmperfyLog.Info(LogCategory, $"No OpenSubsonicExtension supported ({extensionName})!");
            _openSubsonicExtensionsSupport = new OpenSubsonicExtensionsSupport { IsSupported = false };
            return false;
        }
    }

    // --- playable / artwork URLs --------------------------------------------------------------

    public async Task<Uri> GenerateUrlForDownloadingPlayableAsync(string apiId)
    {
        var version = await DetermineApiVersionToUseAsync().ConfigureAwait(false);
        // If transcoding is selected for caching the subsonic API method 'stream' must be used
        // For raw format subsonic API method 'download' can be used
        switch (_settings.User.CacheTranscodingFormatPreference)
        {
            case CacheTranscodingFormatPreference.Mp3:
                {
                    var urlComp = CreateAuthApiUrlComponent(version, "stream", apiId);
                    urlComp.AddQueryItem("format", "mp3");
                    return urlComp.CreateUrl();
                }
            case CacheTranscodingFormatPreference.ServerConfig:
                // let the server decide which format to use
                return CreateAuthApiUrlComponent(version, "stream", apiId).CreateUrl();
            default:
                return CreateAuthApiUrlComponent(version, "download", apiId).CreateUrl();
        }
    }

    public async Task<Uri> GenerateUrlForStreamingPlayableAsync(string apiId, StreamingMaxBitratePreference maxBitrate, StreamingFormatPreference formatPreference)
    {
        var version = await DetermineApiVersionToUseAsync().ConfigureAwait(false);
        var urlComp = CreateAuthApiUrlComponent(version, "stream", apiId);
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
        if (maxBitrate != StreamingMaxBitratePreference.NoLimit) urlComp.AddQueryItem("maxBitRate", (int)maxBitrate);
        return urlComp.CreateUrl();
    }

    public async Task<Uri> GenerateUrlForArtworkAsync(string id)
    {
        var version = await DetermineApiVersionToUseAsync().ConfigureAwait(false);
        return CreateAuthApiUrlComponent(version, "getCoverArt", id).CreateUrl();
    }

    // --- requests -----------------------------------------------------------------------------

    private Task<ApiDataResponse> RequestAction(string action) =>
        RequestAsync(version => CreateAuthApiUrlComponent(version, action).CreateUrl());

    private Task<ApiDataResponse> RequestAction(string action, string id) =>
        RequestAsync(version => CreateAuthApiUrlComponent(version, action, id).CreateUrl());

    /// this requires that the server supports OpenSubsonicExtension "songLyrics"
    public Task<ApiDataResponse> RequestLyricsBySongIdAsync(string id) => RequestAction("getLyricsBySongId", id);

    public Task<ApiDataResponse> RequestGenresAsync() => RequestAction("getGenres");

    public Task<ApiDataResponse> RequestArtistsAsync() => RequestAction("getArtists");

    public Task<ApiDataResponse> RequestArtistAsync(string id) => RequestAction("getArtist", id);

    public Task<ApiDataResponse> RequestAlbumAsync(string id) => RequestAction("getAlbum", id);

    public Task<ApiDataResponse> RequestSongInfoAsync(string id) => RequestAction("getSong", id);

    public Task<ApiDataResponse> RequestFavoriteElementsAsync() => RequestAction("getStarred2");

    private Task<ApiDataResponse> RequestAlbumList(string type, int offset, int count) =>
        RequestAsync(version =>
        {
            var urlComp = CreateAuthApiUrlComponent(version, "getAlbumList2");
            urlComp.AddQueryItem("type", type);
            urlComp.AddQueryItem("size", count);
            urlComp.AddQueryItem("offset", offset);
            return urlComp.CreateUrl();
        });

    public Task<ApiDataResponse> RequestNewestAlbumsAsync(int offset, int count) => RequestAlbumList("newest", offset, count);

    public Task<ApiDataResponse> RequestRecentAlbumsAsync(int offset, int count) => RequestAlbumList("recent", offset, count);

    public Task<ApiDataResponse> RequestAlbumsAsync(int offset, int count) => RequestAlbumList("alphabeticalByName", offset, count);

    public Task<ApiDataResponse> RequestRandomSongsAsync(int count) =>
        RequestAsync(version =>
        {
            var urlComp = CreateAuthApiUrlComponent(version, "getRandomSongs");
            urlComp.AddQueryItem("size", count);
            return urlComp.CreateUrl();
        });

    public Task<ApiDataResponse> RequestPodcastEpisodeDeleteAsync(string id) => RequestAction("deletePodcastEpisode", id);

    private Task<ApiDataResponse> RequestSearch(string searchText, int artistCount, int albumCount, int songCount) =>
        RequestAsync(version =>
        {
            var urlComp = CreateAuthApiUrlComponent(version, "search3");
            urlComp.AddQueryItem("query", searchText);
            urlComp.AddQueryItem("artistCount", artistCount);
            urlComp.AddQueryItem("artistOffset", 0);
            urlComp.AddQueryItem("albumCount", albumCount);
            urlComp.AddQueryItem("albumOffset", 0);
            urlComp.AddQueryItem("songCount", songCount);
            urlComp.AddQueryItem("songOffset", 0);
            return urlComp.CreateUrl();
        });

    public Task<ApiDataResponse> RequestSearchArtistsAsync(string searchText) => RequestSearch(searchText, 40, 0, 0);

    public Task<ApiDataResponse> RequestSearchAlbumsAsync(string searchText) => RequestSearch(searchText, 0, 40, 0);

    public Task<ApiDataResponse> RequestSearchSongsAsync(string searchText) => RequestSearch(searchText, 0, 0, 40);

    public Task<ApiDataResponse> RequestPlaylistsAsync() => RequestAction("getPlaylists");

    public Task<ApiDataResponse> RequestPlaylistSongsAsync(string id) => RequestAction("getPlaylist", id);

    public Task<ApiDataResponse> RequestPlaylistCreateAsync(string name) =>
        RequestAsync(version =>
        {
            var urlComp = CreateAuthApiUrlComponent(version, "createPlaylist");
            urlComp.AddQueryItem("name", name);
            return urlComp.CreateUrl();
        });

    public Task<ApiDataResponse> RequestPlaylistDeleteAsync(string id) => RequestAction("deletePlaylist", id);

    public ResponseError? CheckForErrorResponse(ApiDataResponse response)
    {
        var errorParser = new SsXmlParser();
        errorParser.Parse(response.Data);
        if (errorParser.Error is not { } subsonicError) return null;
        return subsonicError.CreateResponseError(Cleanse(response.Url), response.Data);
    }

    public Task<ApiDataResponse> RequestPlaylistUpdateAsync(string id, string name, IReadOnlyList<int> songIndicesToRemove, IReadOnlyList<string> songIdsToAdd) =>
        RequestAsync(version =>
        {
            var urlComp = CreateAuthApiUrlComponent(version, "updatePlaylist");
            urlComp.AddQueryItem("playlistId", id);
            urlComp.AddQueryItem("name", name);
            foreach (var songIndex in songIndicesToRemove) urlComp.AddQueryItem("songIndexToRemove", songIndex);
            foreach (var songId in songIdsToAdd) urlComp.AddQueryItem("songIdToAdd", songId);
            return urlComp.CreateUrl();
        });

    public Task<ApiDataResponse> RequestPodcastsAsync() =>
        RequestAsync(version =>
        {
            var urlComp = CreateAuthApiUrlComponent(version, "getPodcasts");
            urlComp.AddQueryItem("includeEpisodes", "false");
            return urlComp.CreateUrl();
        });

    public Task<ApiDataResponse> RequestPodcastEpisodesAsync(string id) =>
        RequestAsync(version =>
        {
            var urlComp = CreateAuthApiUrlComponent(version, "getPodcasts", id);
            urlComp.AddQueryItem("includeEpisodes", "true");
            return urlComp.CreateUrl();
        });

    public Task<ApiDataResponse> RequestNewestPodcastsAsync() =>
        RequestAsync(version =>
        {
            var urlComp = CreateAuthApiUrlComponent(version, "getNewestPodcasts");
            urlComp.AddQueryItem("count", 20);
            return urlComp.CreateUrl();
        });

    public Task<ApiDataResponse> RequestRadiosAsync() => RequestAction("getInternetRadioStations");

    public Task<ApiDataResponse> RequestMusicFoldersAsync() => RequestAction("getMusicFolders");

    public Task<ApiDataResponse> RequestIndexesAsync(string musicFolderId) =>
        RequestAsync(version =>
        {
            var urlComp = CreateAuthApiUrlComponent(version, "getIndexes");
            urlComp.AddQueryItem("musicFolderId", musicFolderId);
            return urlComp.CreateUrl();
        });

    public Task<ApiDataResponse> RequestMusicDirectoryAsync(string id) => RequestAction("getMusicDirectory", id);

    public Task<ApiDataResponse> RequestScrobbleAsync(string id, bool submission, DateTime? date = null) =>
        RequestAsync(version =>
        {
            var urlComp = CreateAuthApiUrlComponent(version, "scrobble", id);
            if (date is { } d)
            {
                var utc = d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d.ToUniversalTime();
                urlComp.AddQueryItem("time", new DateTimeOffset(utc).ToUnixTimeMilliseconds());
            }
            urlComp.AddQueryItem("submission", submission ? "true" : "false");
            return urlComp.CreateUrl();
        });

    /// Only songs, albums, artists are supported by the subsonic API
    public Task<ApiDataResponse> RequestRatingAsync(string id, int rating) =>
        RequestAsync(version =>
        {
            var urlComp = CreateAuthApiUrlComponent(version, "setRating", id);
            urlComp.AddQueryItem("rating", rating);
            return urlComp.CreateUrl();
        });

    public Task<ApiDataResponse> RequestSetFavoriteSongAsync(string songId, bool isFavorite) =>
        RequestAction(isFavorite ? "star" : "unstar", songId);

    public Task<ApiDataResponse> RequestSetFavoriteAlbumAsync(string albumId, bool isFavorite) =>
        RequestAsync(version =>
        {
            var urlComp = CreateAuthApiUrlComponent(version, isFavorite ? "star" : "unstar");
            urlComp.AddQueryItem("albumId", albumId);
            return urlComp.CreateUrl();
        });

    public Task<ApiDataResponse> RequestSetFavoriteArtistAsync(string artistId, bool isFavorite) =>
        RequestAsync(version =>
        {
            var urlComp = CreateAuthApiUrlComponent(version, isFavorite ? "star" : "unstar");
            urlComp.AddQueryItem("artistId", artistId);
            return urlComp.CreateUrl();
        });

    public Task<ApiDataResponse> RequestSimilarSongsAsync(string id, int count = 50) =>
        RequestAsync(version =>
        {
            var urlComp = CreateAuthApiUrlComponent(version, "getSimilarSongs2", id);
            urlComp.AddQueryItem("count", count);
            return urlComp.CreateUrl();
        });

    private async Task<ApiDataResponse> RequestAsync(Func<SubsonicVersion, Uri> urlCreation)
    {
        var version = await DetermineApiVersionToUseAsync().ConfigureAwait(false);
        var url = urlCreation(version);
        return await RequestAsync(url, BuildHttpHeaders()).ConfigureAwait(false);
    }

    private async Task<ApiDataResponse> RequestAsync(Uri url, IReadOnlyDictionary<string, string>? headers)
    {
        using var request = AmperfyHttp.CreateGet(url, headers);
        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        var data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var cleanedUrl = Cleanse(url);
            AmperfyLog.Info(LogCategory, $"API 404: Not Found: {cleanedUrl.Description}");
            throw new ResponseError(ResponseErrorType.Api, (int)SubsonicError.RequestedDataNotFound, "404: Not Found", cleanedUrl, data);
        }
        // Like Alamofire: a response body is returned even for an error status (Subsonic error xml);
        // only an error status without any body is reported as HTTP error.
        if (!response.IsSuccessStatusCode && data.Length == 0)
        {
            throw new HttpRequestException(
                $"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).",
                null, response.StatusCode);
        }
        return new ApiDataResponse(data, url);
    }
}
