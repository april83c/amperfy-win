using System.Text;
using System.Globalization;
using Amperfy.Core.Api.Ampache;
using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Api.Ampache;

/// Additional tests (no Swift equivalent): handshake, token caching, url generation.
public class AmpacheXmlServerApiTest
{
    private readonly FakeAmpacheServer _server = new();
    private readonly AmperfySettings _settings = new();
    private readonly AmpacheXmlServerApi _api;
    private readonly LoginCredentials _credentials = new("https://ampache.test:8443/", "user", "secret", BackendApiType.Ampache);

    public AmpacheXmlServerApiTest()
    {
        _api = new AmpacheXmlServerApi(null, _settings, _server);
    }

    private static Dictionary<string, string> Query(Uri url) =>
        (AmpacheUrlComponents.ParseQueryItems(url.Query) ?? []).ToDictionary(i => i.Key, i => i.Value ?? "");

    [Fact]
    public void TestGeneratePassphrase()
    {
        // sha256("1616980000" + sha256("secret"))
        Assert.Equal("d35f68ac01a8904404d15f0ad9cf635309521341e28a44096f60064d23d6ea82",
            AmpacheXmlServerApi.GeneratePassphrase(StringHasher.Sha256("secret"), 1616980000));
    }

    [Fact]
    public async Task TestHandshakeRequest()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _api.IsAuthenticationValidAsync(_credentials);
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var request = Assert.Single(_server.Requests);
        Assert.Equal("https", request.Url.Scheme);
        Assert.Equal("ampache.test", request.Url.Host);
        Assert.Equal(8443, request.Url.Port);
        Assert.Equal("/server/xml.server.php", request.Url.AbsolutePath);
        Assert.Equal("handshake", request.Query["action"]);
        Assert.Equal("500000", request.Query["version"]);
        Assert.Equal("user", request.Query["user"]);
        var timestamp = long.Parse(request.Query["timestamp"], CultureInfo.InvariantCulture);
        Assert.InRange(timestamp, before, after);
        var expectedPassphrase = StringHasher.Sha256(timestamp.ToString(CultureInfo.InvariantCulture) + StringHasher.Sha256("secret"));
        Assert.Equal(expectedPassphrase, request.Query["auth"]);
        Assert.Equal("500000", _api.ServerApiVersion);
    }

    [Fact]
    public async Task TestAuthTokenIsCached()
    {
        _server.Actions["genres"] = _ => TestFiles.GetTestFileData("Ampache", "genres");
        _api.ProvideCredentials(_credentials);
        await _api.RequestGenresAsync();
        await _api.RequestGenresAsync();

        Assert.Single(_server.RequestsFor("handshake"));
        var genreRequests = _server.RequestsFor("genres").ToList();
        Assert.Equal(2, genreRequests.Count);
        Assert.All(genreRequests, r => Assert.Equal("token-1", r.Query["auth"]));
        Assert.All(genreRequests, r => Assert.Equal("/server/xml.server.php", r.Url.AbsolutePath));
    }

    [Fact]
    public async Task TestReauthenticationWhenSessionExpiresSoon()
    {
        // session expires within the 5 minute safety offset -> every request needs a new handshake
        _server.SessionDuration = TimeSpan.FromMinutes(2);
        _server.Actions["genres"] = _ => TestFiles.GetTestFileData("Ampache", "genres");
        _api.ProvideCredentials(_credentials);
        await _api.RequestGenresAsync();
        _server.Token = "token-2";
        await _api.RequestGenresAsync();

        Assert.Equal(2, _server.RequestsFor("handshake").Count());
        Assert.Equal(["token-1", "token-2"], _server.RequestsFor("genres").Select(r => r.Query["auth"]).ToList());
    }

    [Fact]
    public async Task TestProvideCredentialsResetsToken()
    {
        _server.Actions["genres"] = _ => TestFiles.GetTestFileData("Ampache", "genres");
        _api.ProvideCredentials(_credentials);
        await _api.RequestGenresAsync();
        _server.Token = "token-2";
        _api.ProvideCredentials(_credentials.Clone());
        await _api.RequestGenresAsync();
        Assert.Equal(["token-1", "token-2"], _server.RequestsFor("genres").Select(r => r.Query["auth"]).ToList());
    }

    [Fact]
    public async Task TestNoCredentials()
    {
        var error = await Assert.ThrowsAsync<BackendError>(() => _api.RequestGenresAsync());
        Assert.Equal(BackendErrorKind.NoCredentials, error.Kind);
    }

    [Fact]
    public async Task TestCustomHttpHeadersOnEveryRequest()
    {
        var credentials = _credentials.Clone();
        credentials.HttpHeaders["CF-Access-Client-Id"] = "abc";
        _server.Actions["genres"] = _ => TestFiles.GetTestFileData("Ampache", "genres");
        _api.ProvideCredentials(credentials);
        await _api.RequestGenresAsync();

        Assert.Equal(2, _server.Requests.Count);
        Assert.All(_server.Requests, r => Assert.Equal("abc", r.Headers["CF-Access-Client-Id"]));
        Assert.Equal("abc", _api.HttpHeaders["CF-Access-Client-Id"]);
    }

    [Fact]
    public async Task TestHandshakeErrors()
    {
        _server.HandshakeResponse = FakeAmpacheServer.ErrorXml(4701, "Invalid handshake");
        var apiError = await Assert.ThrowsAsync<AmpacheResponseError>(() => _api.IsAuthenticationValidAsync(_credentials));
        Assert.Equal(4701, apiError.StatusCode);
        Assert.Equal(AmpacheError.ReceivedInvalidHandshake, apiError.AmpacheError);

        _server.HandshakeResponse = Encoding.UTF8.GetBytes("<html><body>no xml");
        var xmlError = await Assert.ThrowsAsync<ResponseError>(() => _api.IsAuthenticationValidAsync(_credentials));
        Assert.Equal(ResponseErrorType.Xml, xmlError.Type);

        _server.HandshakeResponse = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><root><api>500000</api></root>");
        var authError = await Assert.ThrowsAsync<AuthenticationError>(() => _api.IsAuthenticationValidAsync(_credentials));
        Assert.Equal(AuthenticationErrorKind.NotAbleToLogin, authError.Kind);
    }

    [Theory]
    [InlineData("500000", true)]
    [InlineData("420000", true)]
    [InlineData("410000", false)]
    [InlineData("5.0.0", false)] // like Swift: Int("5.0.0") == nil
    public async Task TestServerPodcastSupport(string apiVersion, bool isSupported)
    {
        _server.ApiVersion = apiVersion;
        _api.ProvideCredentials(_credentials);
        Assert.Equal(isSupported, await _api.RequestServerPodcastSupportAsync());
    }

    [Fact]
    public async Task TestStreamingUrl()
    {
        _api.ProvideCredentials(_credentials);
        var url = await _api.GenerateUrlForStreamingPlayableAsync(true, "12", StreamingMaxBitratePreference.Limit128, StreamingFormatPreference.Mp3);
        Assert.Equal("https://ampache.test:8443/server/xml.server.php?auth=token-1&action=stream&type=song&id=12&format=mp3&bitrate=128&length=1", url.ToString());

        url = await _api.GenerateUrlForStreamingPlayableAsync(false, "7", StreamingMaxBitratePreference.NoLimit, StreamingFormatPreference.ServerConfig);
        var query = Query(url);
        Assert.Equal("podcast_episode", query["type"]);
        Assert.False(query.ContainsKey("format"));
        Assert.False(query.ContainsKey("bitrate"));
        Assert.Equal("1", query["length"]);

        url = await _api.GenerateUrlForStreamingPlayableAsync(true, "7", StreamingMaxBitratePreference.NoLimit, StreamingFormatPreference.Raw);
        Assert.Equal("raw", Query(url)["format"]);
    }

    [Fact]
    public async Task TestDownloadUrl()
    {
        _api.ProvideCredentials(_credentials);
        _settings.User.CacheTranscodingFormatPreference = CacheTranscodingFormatPreference.Mp3;
        var url = await _api.GenerateUrlForDownloadingPlayableAsync(true, "12");
        Assert.Equal("https://ampache.test:8443/server/xml.server.php?auth=token-1&action=download&type=song&id=12&format=mp3", url.ToString());

        _settings.User.CacheTranscodingFormatPreference = CacheTranscodingFormatPreference.ServerConfig;
        url = await _api.GenerateUrlForDownloadingPlayableAsync(false, "3");
        var query = Query(url);
        Assert.Equal("podcast_episode", query["type"]);
        Assert.Equal("raw", query["format"]);
    }

    [Fact]
    public async Task TestArtworkUrl()
    {
        var credentials = new LoginCredentials("http://music.local/ampache", "user", "secret", BackendApiType.Ampache);
        _api.ProvideCredentials(credentials);
        var url = await _api.GenerateUrlForArtworkAsync(new ArtworkRemoteInfo("12", "album"));
        Assert.Equal("http://music.local/ampache/image.php?auth=token-1&object_id=12&object_type=album", url.ToString());
        Assert.Equal("/ampache/server/xml.server.php", Assert.Single(_server.Requests).Url.AbsolutePath);
        Assert.Equal(new ArtworkRemoteInfo("12", "album"), AmpacheXmlServerApi.ExtractArtworkInfoFromUrl(url.ToString()));
    }

    [Fact]
    public void TestExtractArtworkInfoFromUrl()
    {
        Assert.Equal(new ArtworkRemoteInfo("1", "podcast"),
            AmpacheXmlServerApi.ExtractArtworkInfoFromUrl("https://music.com.au/image.php?object_id=1&object_type=podcast&auth=eeb9f1&name=art.jpg"));
        Assert.Null(AmpacheXmlServerApi.ExtractArtworkInfoFromUrl("https://music.com.au/image.php?object_id=1"));
        Assert.Null(AmpacheXmlServerApi.ExtractArtworkInfoFromUrl(""));
    }

    [Fact]
    public async Task TestQueryValuesAreEscaped()
    {
        _server.Actions["search_songs"] = _ => TestFiles.GetTestFileData("Ampache", "songs");
        _api.ProvideCredentials(_credentials);
        await _api.RequestSearchSongsAsync("AC/DC & Friends + more");
        var request = Assert.Single(_server.RequestsFor("search_songs"));
        Assert.Equal("AC/DC & Friends + more", request.Query["filter"]);
        Assert.Equal("40", request.Query["limit"]);
    }

    [Fact]
    public void TestCleanse()
    {
        var cleansed = _api.Cleanse(new Uri("https://my.server:8443/server/xml.server.php?action=handshake&auth=abc&timestamp=1&user=me&ssid=xyz"));
        Assert.Equal("https://SERVERURL/server/xml.server.php?action=handshake&auth=AUTH&timestamp=1&user=USER&ssid=SSID", cleansed.Description);
        Assert.Equal("", _api.Cleanse(new Uri("https://my.server/")).Description);
        Assert.Equal("", _api.Cleanse(null).Description);
    }

    [Fact]
    public void TestCheckForErrorResponse()
    {
        var url = new Uri("https://my.server/server/xml.server.php?action=genres&auth=abc");
        var error = _api.CheckForErrorResponse(new ApiDataResponse(TestFiles.GetTestFileData("Ampache", "error-4700"), url));
        Assert.NotNull(error);
        Assert.Equal(ResponseErrorType.Api, error!.Type);
        Assert.Equal(4700, error.StatusCode);
        Assert.Equal("Access Denied", error.ErrorMessage);
        Assert.Equal(AmpacheError.AccessControlNotEnabled, error.AsAmpacheError());
        Assert.Equal("https://SERVERURL/server/xml.server.php?action=genres&auth=AUTH", error.CleansedUrl?.Description);

        Assert.Null(_api.CheckForErrorResponse(new ApiDataResponse(TestFiles.GetTestFileData("Ampache", "genres"), url)));
    }

    [Fact]
    public async Task TestUpdateUrlToken()
    {
        _api.ProvideCredentials(_credentials);
        var url = await _api.UpdateUrlTokenAsync("https://other.host/play/index.php?ssid=old&type=song&oid=56");
        Assert.Equal("https://ampache.test:8443/play/index.php?ssid=token-1&type=song&oid=56", url.ToString());
    }

    [Fact]
    public void TestAmpacheErrorBehavior()
    {
        Assert.False(AmpacheError.Empty.ShouldErrorBeDisplayedToUser());
        Assert.False(AmpacheError.NotFound.ShouldErrorBeDisplayedToUser());
        Assert.True(AmpacheError.AccessDenied.ShouldErrorBeDisplayedToUser());
        Assert.False(AmpacheError.NotFound.IsRemoteAvailable());
        Assert.True(AmpacheError.BadRequest.IsRemoteAvailable());
        Assert.Null(AmpacheErrorExtensions.FromStatusCode(1234));
    }
}
