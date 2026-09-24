using Amperfy.Core.Api.Subsonic;
using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Api.Subsonic;

/// Tests of the HTTP client (URL generation, authentication, headers, error handling).
public class SubsonicServerApiTest
{
    private const string ServerUrl = "https://music.example.com:4040/subsonic/";
    private const string Password = "s3cr3t+&=pw";

    private readonly FakeSubsonicHttpHandler _handler = new();
    private readonly AmperfySettings _settings = new();
    private readonly SubsonicServerApi _api;
    private readonly LoginCredentials _credentials;

    public SubsonicServerApiTest()
    {
        _api = new SubsonicServerApi(null, _settings, _handler);
        _credentials = new LoginCredentials(ServerUrl, "user name", Password, BackendApiType.Subsonic);
        _api.ProvideCredentials(_credentials);
    }

    private static void AssertTokenAuth(FakeSubsonicHttpHandler.RecordedRequest request)
    {
        var query = request.Query;
        Assert.Equal("user name", query["u"]);
        Assert.Equal("1.13.0", query["v"]);
        Assert.Equal("Amperfy", query["c"]);
        Assert.False(query.ContainsKey("p"));
        var salt = query["s"];
        Assert.NotNull(salt);
        Assert.Equal(16, salt.Length);
        Assert.Equal(StringHasher.Md5Hex(Password + salt), query["t"]);
    }

    [Fact]
    public async Task TokenAuthenticationParameters()
    {
        var url = await _api.GenerateUrlForArtworkAsync("al-123");

        // first the server API version is requested via ping
        var ping = _handler.LastRequest("ping");
        AssertTokenAuth(ping);
        Assert.Equal("1.16.1", _api.ServerApiVersion?.Description);
        Assert.Equal("1.13.0", _api.ClientApiVersion?.Description);

        Assert.Equal("https://music.example.com:4040/subsonic/rest/getCoverArt.view", url.GetLeftPart(UriPartial.Path));
        var request = new FakeSubsonicHttpHandler.RecordedRequest(url, []);
        AssertTokenAuth(request);
        Assert.Equal("al-123", request.Query["id"]);
    }

    [Fact]
    public async Task SaltIsNewForEveryRequest()
    {
        var url1 = await _api.GenerateUrlForArtworkAsync("1");
        var url2 = await _api.GenerateUrlForArtworkAsync("1");
        var q1 = new FakeSubsonicHttpHandler.RecordedRequest(url1, []).Query;
        var q2 = new FakeSubsonicHttpHandler.RecordedRequest(url2, []).Query;
        Assert.NotEqual(q1["s"], q2["s"]);
        Assert.NotEqual(q1["t"], q2["t"]);
        // version is requested only once
        Assert.Single(_handler.Requests, r => r.Action == "ping");
    }

    [Fact]
    public async Task LegacyAuthenticationUsesPassword()
    {
        _api.SetAuthType(SubsonicApiAuthType.Legacy);
        var url = await _api.GenerateUrlForArtworkAsync("al-123");

        var ping = _handler.LastRequest("ping").Query;
        Assert.Equal("1.11.0", ping["v"]);
        Assert.Equal(Password, ping["p"]);

        var query = new FakeSubsonicHttpHandler.RecordedRequest(url, []).Query;
        Assert.Equal("1.11.0", query["v"]);
        Assert.Equal("user name", query["u"]);
        Assert.Equal(Password, query["p"]);
        Assert.False(query.ContainsKey("t"));
        Assert.False(query.ContainsKey("s"));
        Assert.Equal("1.11.0", _api.ClientApiVersion?.Description);
    }

    [Fact]
    public async Task ActiveBackendServerUrlIsUsedAsBase()
    {
        _credentials.ActiveBackendServerUrl = "http://192.168.0.2:4533";
        var url = await _api.GenerateUrlForArtworkAsync("1");
        Assert.Equal("http://192.168.0.2:4533/rest/getCoverArt.view", url.GetLeftPart(UriPartial.Path));
        Assert.Equal("http://192.168.0.2:4533/rest/ping.view", _handler.LastRequest("ping").Url.GetLeftPart(UriPartial.Path));
    }

    [Theory]
    [InlineData(StreamingFormatPreference.Mp3, StreamingMaxBitratePreference.Limit128, "mp3", "128")]
    [InlineData(StreamingFormatPreference.Raw, StreamingMaxBitratePreference.Limit320, "raw", "320")]
    [InlineData(StreamingFormatPreference.ServerConfig, StreamingMaxBitratePreference.Limit32, null, "32")]
    [InlineData(StreamingFormatPreference.Mp3, StreamingMaxBitratePreference.NoLimit, "mp3", null)]
    [InlineData(StreamingFormatPreference.ServerConfig, StreamingMaxBitratePreference.NoLimit, null, null)]
    public async Task StreamingUrl(StreamingFormatPreference format, StreamingMaxBitratePreference bitrate, string? expectedFormat, string? expectedMaxBitRate)
    {
        var api = new SubsonicApi(_api, new AlwaysOnlineNetworkMonitor(), null!);
        var url = await api.GenerateUrlForStreamingPlayableAsync(new AbstractPlayableInfo("song-1", 1, DerivedPlayableType.Song, null), bitrate, format);
        Assert.EndsWith("/rest/stream.view", url.GetLeftPart(UriPartial.Path));
        var query = new FakeSubsonicHttpHandler.RecordedRequest(url, []).Query;
        Assert.Equal("song-1", query["id"]);
        Assert.Equal(expectedFormat, query.GetValueOrDefault("format"));
        Assert.Equal(expectedMaxBitRate, query.GetValueOrDefault("maxBitRate"));
    }

    [Fact]
    public async Task StreamingUrlOfPodcastEpisodeUsesStreamId()
    {
        var api = new SubsonicApi(_api, new AlwaysOnlineNetworkMonitor(), null!);
        var url = await api.GenerateUrlForStreamingPlayableAsync(new AbstractPlayableInfo("34", 1, DerivedPlayableType.PodcastEpisode, "523"),
            StreamingMaxBitratePreference.NoLimit, StreamingFormatPreference.Raw);
        Assert.Equal("523", new FakeSubsonicHttpHandler.RecordedRequest(url, []).Query["id"]);
        var downloadUrl = await api.GenerateUrlForDownloadingPlayableAsync(new AbstractPlayableInfo("34", 1, DerivedPlayableType.PodcastEpisode, "523"));
        Assert.Equal("523", new FakeSubsonicHttpHandler.RecordedRequest(downloadUrl, []).Query["id"]);
    }

    [Theory]
    [InlineData(CacheTranscodingFormatPreference.Mp3, "stream", "mp3")]
    [InlineData(CacheTranscodingFormatPreference.ServerConfig, "stream", null)]
    [InlineData(CacheTranscodingFormatPreference.Raw, "download", null)]
    public async Task DownloadUrl(CacheTranscodingFormatPreference preference, string expectedAction, string? expectedFormat)
    {
        _settings.User.CacheTranscodingFormatPreference = preference;
        var url = await _api.GenerateUrlForDownloadingPlayableAsync("song-7");
        Assert.EndsWith($"/rest/{expectedAction}.view", url.GetLeftPart(UriPartial.Path));
        var query = new FakeSubsonicHttpHandler.RecordedRequest(url, []).Query;
        Assert.Equal("song-7", query["id"]);
        Assert.Equal(expectedFormat, query.GetValueOrDefault("format"));
        Assert.False(query.ContainsKey("maxBitRate"));
    }

    [Fact]
    public async Task CustomHttpHeadersAreSentWithEveryRequest()
    {
        _credentials.HttpHeaders["CF-Access-Client-Id"] = "client-id";
        _credentials.HttpHeaders["X-Custom"] = "value";
        _handler.RespondWithSample("getGenres", "genres_example_1");

        await _api.RequestGenresAsync();

        Assert.Equal(2, _handler.Requests.Count); // ping + getGenres
        foreach (var request in _handler.Requests)
        {
            Assert.Equal("client-id", request.Headers["CF-Access-Client-Id"]);
            Assert.Equal("value", request.Headers["X-Custom"]);
        }
        Assert.Equal("client-id", _api.HttpHeaders["CF-Access-Client-Id"]);
    }

    [Fact]
    public async Task AuthenticationCheckUsesProvidedCredentialsAndHeaders()
    {
        var api = new SubsonicServerApi(null, _settings, _handler);
        var credentials = new LoginCredentials("https://other.example.com", "other", "pw", BackendApiType.Subsonic);
        credentials.HttpHeaders["Authorization"] = "Bearer abc";

        await api.IsAuthenticationValidAsync(credentials);

        Assert.All(_handler.Requests, r =>
        {
            Assert.Equal("ping", r.Action);
            Assert.Equal("other.example.com", r.Url.Host);
            Assert.Equal("other", r.Query["u"]);
            Assert.Equal("Bearer abc", r.Headers["Authorization"]);
        });
    }

    [Fact]
    public async Task AuthenticationCheckFailsForErrorResponse()
    {
        _handler.RespondWithSample("ping", "error_example_1");
        // server version 1.1.0 is known from the error response -> auth check fails because of the error element
        var error = await Assert.ThrowsAsync<AuthenticationError>(() => _api.IsAuthenticationValidAsync(_credentials));
        Assert.Equal(AuthenticationErrorKind.NotAbleToLogin, error.Kind);
    }

    [Fact]
    public async Task NotFoundResponseIsDataNotFoundError()
    {
        var error = await Assert.ThrowsAsync<ResponseError>(() => _api.RequestAlbumAsync("unknown"));
        Assert.Equal(ResponseErrorType.Api, error.Type);
        Assert.Equal(70, error.StatusCode);
        Assert.Equal(SubsonicError.RequestedDataNotFound, error.AsSubsonicError());
        Assert.False(error.AsSubsonicError()!.Value.IsRemoteAvailable());
        Assert.Equal("404: Not Found", error.ErrorMessage);
        Assert.StartsWith("https://SERVERURL/subsonic/rest/getAlbum.view?u=USER&v=1.13.0&c=Amperfy&t=AUTHTOKEN&s=SALT&id=unknown", error.CleansedUrl?.Description);
    }

    [Fact]
    public void CleanseRemovesCredentials()
    {
        var cleansed = _api.Cleanse(new Uri("https://music.example.com:4040/rest/ping.view?u=bob&v=1.11.0&c=Amperfy&p=secret&id=5"));
        Assert.Equal("https://SERVERURL/rest/ping.view?u=USER&v=1.11.0&c=Amperfy&p=PASSWORD&id=5", cleansed.Description);
        Assert.Equal("", _api.Cleanse(null).Description);
        Assert.Equal("", _api.Cleanse(new Uri("https://music.example.com/rest/ping.view")).Description);
    }

    [Fact]
    public void ExtractArtworkInfoFromUrl()
    {
        var info = SubsonicServerApi.ExtractArtworkInfoFromUrl("https://host/rest/getCoverArt.view?u=a&id=al-11053&v=1.13.0");
        Assert.Equal(new ArtworkRemoteInfo("al-11053", ""), info);
        Assert.Null(SubsonicServerApi.ExtractArtworkInfoFromUrl("https://host/rest/getCoverArt.view?u=a"));
        Assert.Null(SubsonicServerApi.ExtractArtworkInfoFromUrl("not a url"));
    }

    [Fact]
    public void CheckForErrorResponse()
    {
        var url = new Uri("https://music.example.com/rest/getAlbum.view?u=bob&t=abc&s=def&id=3");
        var error = _api.CheckForErrorResponse(new ApiDataResponse(TestFiles.GetTestFileData("Subsonic", "error_example_1"), url));
        Assert.NotNull(error);
        Assert.Equal(40, error.StatusCode);
        Assert.Equal("Wrong username or password", error.ErrorMessage);
        Assert.Equal("https://SERVERURL/rest/getAlbum.view?u=USER&t=AUTHTOKEN&s=SALT&id=3", error.CleansedUrl?.Description);
        Assert.Null(_api.CheckForErrorResponse(new ApiDataResponse(TestFiles.GetTestFileData("Subsonic", "album_example_1"), url)));
    }

    [Fact]
    public async Task PlaylistUpdateParameters()
    {
        _handler.Respond("updatePlaylist", FakeSubsonicHttpHandler.PingXml);
        await _api.RequestPlaylistUpdateAsync("pl-1", "My & List", [0, 2], ["s1", "s2"]);
        var request = _handler.LastRequest("updatePlaylist");
        Assert.Equal("pl-1", request.Query["playlistId"]);
        Assert.Equal("My & List", request.Query["name"]);
        Assert.Equal(["0", "2"], request.QueryValues("songIndexToRemove"));
        Assert.Equal(["s1", "s2"], request.QueryValues("songIdToAdd"));
    }

    [Fact]
    public async Task ScrobbleParameters()
    {
        _handler.Respond("scrobble", FakeSubsonicHttpHandler.PingXml);
        var date = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await _api.RequestScrobbleAsync("song-1", submission: true, date);
        var query = _handler.LastRequest("scrobble").Query;
        Assert.Equal("song-1", query["id"]);
        Assert.Equal("true", query["submission"]);
        Assert.Equal(new DateTimeOffset(date).ToUnixTimeMilliseconds().ToString(), query["time"]);

        await _api.RequestScrobbleAsync("song-1", submission: false);
        query = _handler.LastRequest("scrobble").Query;
        Assert.Equal("false", query["submission"]);
        Assert.False(query.ContainsKey("time"));
    }

    [Fact]
    public async Task OpenSubsonicExtensionSupportIsCached()
    {
        _handler.RespondWithSample("getOpenSubsonicExtensions", "OpenSubsonicExtensions_example_1");
        Assert.True(await _api.IsOpenSubsonicExtensionSupportedAsync(OpenSubsonicExtension.SongLyrics));
        Assert.True(await _api.IsOpenSubsonicExtensionSupportedAsync(OpenSubsonicExtension.SongLyrics));
        Assert.Single(_handler.Requests, r => r.Action == "getOpenSubsonicExtensions");
    }

    [Fact]
    public async Task OpenSubsonicExtensionNotSupportedWhenRequestFails()
    {
        // no response configured -> 404
        Assert.False(await _api.IsOpenSubsonicExtensionSupportedAsync(OpenSubsonicExtension.SongLyrics));
        Assert.False(await _api.IsOpenSubsonicExtensionSupportedAsync(OpenSubsonicExtension.SongLyrics));
        Assert.Single(_handler.Requests, r => r.Action == "getOpenSubsonicExtensions");
    }

    [Fact]
    public async Task PodcastSupportDependsOnServerVersion()
    {
        _handler.RespondWithSample("ping", "ping_example_1"); // version 1.1.1
        Assert.False(await _api.RequestServerPodcastSupportAsync());
        Assert.DoesNotContain(_handler.Requests, r => r.Action == "getPodcasts");

        var api = new SubsonicServerApi(null, _settings, _handler);
        api.ProvideCredentials(_credentials);
        _handler.Respond("ping", FakeSubsonicHttpHandler.PingXml); // version 1.16.1
        _handler.RespondWithSample("getPodcasts", "podcasts_example_1");
        Assert.True(await api.RequestServerPodcastSupportAsync());
        Assert.Equal("false", _handler.LastRequest("getPodcasts").Query["includeEpisodes"]);
    }

    [Fact]
    public async Task MissingCredentialsGiveInvalidUrl()
    {
        var api = new SubsonicServerApi(null, _settings, _handler);
        var error = await Assert.ThrowsAsync<BackendError>(() => api.RequestGenresAsync());
        Assert.Equal(BackendErrorKind.InvalidUrl, error.Kind);
        Assert.Empty(_handler.Requests);
    }
}
