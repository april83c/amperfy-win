using System.Text;
using System.Globalization;
using Amperfy.Core.Api.Ampache;
using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Api.Ampache;

/// Additional tests (no Swift equivalent): the syncer against a fake Ampache server.
public class AmpacheLibrarySyncerTest
{
    private readonly FakeAmpacheServer _server = new();
    private readonly LibraryStorage _library;
    private readonly Account _account;
    private readonly AmpacheApi _backendApi;
    private readonly ILibrarySyncer _syncer;

    public AmpacheLibrarySyncerTest()
    {
        _library = new CoreDataHelper().CreateInMemoryLibrary();
        _account = _library.GetAccount(TestAccountInfo.Create1());
        var xmlApi = new AmpacheXmlServerApi(null, new AmperfySettings(), _server);
        xmlApi.ProvideCredentials(new LoginCredentials("https://ampache.test", "user", "secret", BackendApiType.Ampache));
        _backendApi = new AmpacheApi(xmlApi, new AlwaysOnlineNetworkMonitor(), new EventLogger(_library));
        _syncer = _backendApi.CreateLibrarySyncer(_account, _library);
    }

    private static byte[] Sample(string name) => TestFiles.GetTestFileData("Ampache", name);

    private static byte[] SinglePlaylistXml(string id, string name) => Encoding.UTF8.GetBytes($"""
        <?xml version="1.0" encoding="UTF-8"?>
        <root>
          <playlist id="{id}">
            <name><![CDATA[{name}]]></name>
            <items>4</items>
          </playlist>
        </root>
        """);

    [Fact]
    public void TestSyncInitial()
    {
        _server.Actions["genres"] = _ => Sample("genres");
        _server.Actions["artists"] = _ => Sample("artists");
        _server.Actions["albums"] = _ => Sample("albums");
        _server.Actions["playlists"] = _ => Sample("playlists");
        _server.Actions["podcasts"] = _ => Sample("podcasts");

        SingleThreadSynchronizationContext.Run(() => _syncer.SyncInitialAsync(null));

        // pollCount + 1 requests (like Swift: 0...pollCount), the offset is clamped to count - 1
        Assert.Equal(["0", "2"], _server.RequestsFor("artists").Select(r => r.Query["offset"]).Order().ToList());
        Assert.Equal(2, _server.RequestsFor("albums").Count());
        Assert.Contains("16", _library.GetArtists(_account).Select(a => a.Id));
        Assert.Equal(3, _library.GetAlbums(_account).Count);
        Assert.Equal(4, _library.GetPlaylists(_account).Count);
        var podcasts = _library.GetPodcasts(_account);
        Assert.Equal(3, podcasts.Count);
        Assert.Equal("60-Second < Science", podcasts[0].Title);
        Assert.NotNull(_library.GetGenre(_account, "6"));
    }

    [Fact]
    public void TestSyncInitialWithoutPodcastSupport()
    {
        _server.ApiVersion = "410000";
        _server.Actions["genres"] = _ => Sample("genres");
        _server.Actions["artists"] = _ => Sample("artists");
        _server.Actions["albums"] = _ => Sample("albums");
        _server.Actions["playlists"] = _ => Sample("playlists");
        _server.Actions["podcasts"] = _ => Sample("podcasts");

        SingleThreadSynchronizationContext.Run(() => _syncer.SyncInitialAsync(null));

        Assert.Empty(_server.RequestsFor("podcasts"));
        Assert.Empty(_library.GetPodcasts(_account));
    }

    [Fact]
    public void TestSyncRadios()
    {
        _server.Actions["live_streams"] = _ => Sample("live_streams");
        var oldRadio = _library.CreateRadio(_account);
        oldRadio.Id = "99";
        _library.SaveContext();

        SingleThreadSynchronizationContext.Run(() => _syncer.SyncRadiosAsync());

        var radios = _library.GetRadios(_account);
        Assert.Equal(3, radios.Count);
        Assert.Equal(RemoteStatus.Deleted, oldRadio.RemoteStatus);
    }

    [Fact]
    public void TestSyncAlbumNotFoundMarksAlbumAsDeleted()
    {
        _server.Actions["album"] = _ => FakeAmpacheServer.ErrorXml(4704, "Not Found");
        var album = _library.CreateAlbum(_account);
        album.Id = "12";
        var song = _library.CreateSong(_account);
        song.Id = "1";
        song.Album = album;
        _library.SaveContext();

        // like Swift's storage.async.perform the not-found error is not propagated
        SingleThreadSynchronizationContext.Run(() => _syncer.SyncAsync(album));

        Assert.Equal(RemoteStatus.Deleted, album.RemoteStatus);
        Assert.Equal(RemoteStatus.Deleted, song.RemoteStatus);
        Assert.Empty(_server.RequestsFor("album_songs"));
    }

    [Fact]
    public void TestSyncAlbum()
    {
        _server.Actions["album"] = _ => Sample("albums");
        _server.Actions["album_songs"] = _ => Sample("songs");
        var album = _library.CreateAlbum(_account);
        album.Id = "12";
        var removedSong = _library.CreateSong(_account);
        removedSong.Id = "999";
        removedSong.Album = album;
        _library.SaveContext();

        SingleThreadSynchronizationContext.Run(() => _syncer.SyncAsync(album));

        Assert.Equal(RemoteStatus.Available, album.RemoteStatus);
        Assert.True(album.IsSongsMetaDataSynced);
        Assert.Equal("Buried in Nausea", album.Name);
        Assert.Equal(RemoteStatus.Deleted, removedSong.RemoteStatus);
        Assert.Null(removedSong.Album);
        Assert.Equal(["107", "115"], album.Songs.Select(s => s.Id).Order().ToList());
    }

    [Fact]
    public void TestSyncDownPlaylist()
    {
        _server.Actions["playlist"] = r => r.Query["filter"] == "" ? FakeAmpacheServer.ErrorXml(4704, "Not Found") : SinglePlaylistXml(r.Query["filter"], "Server Name");
        _server.Actions["playlist_create"] = r => SinglePlaylistXml("77", r.Query["name"]);
        _server.Actions["playlist_songs"] = _ => Sample("playlist_songs");
        var playlist = _library.CreatePlaylist(_account);
        playlist.Name = "My Playlist";
        _library.SaveContext();

        SingleThreadSynchronizationContext.Run(() => _syncer.SyncDownAsync(playlist));

        var createRequest = Assert.Single(_server.RequestsFor("playlist_create"));
        Assert.Equal("My Playlist", createRequest.Query["name"]);
        Assert.Equal("private", createRequest.Query["type"]);
        Assert.Equal("77", playlist.Id);
        Assert.Equal("77", Assert.Single(_server.RequestsFor("playlist_songs")).Query["filter"]);
        Assert.Equal(["56", "107", "115", "85"], playlist.Playables.Select(p => p.Id).ToList());
        Assert.Equal(1442, playlist.Duration);
    }

    [Fact]
    public void TestSyncUploadPlaylistOrder()
    {
        _server.Actions["playlist_edit"] = _ => Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><root><success code=\"1\">ok</success></root>");
        var playlist = _library.CreatePlaylist(_account);
        playlist.Id = "5";
        foreach (var id in new[] { "3", "1", "2" })
        {
            var song = _library.CreateSong(_account);
            song.Id = id;
            playlist.Append(song);
        }
        _library.SaveContext();

        SingleThreadSynchronizationContext.Run(() => _syncer.SyncUploadPlaylistOrderAsync(playlist));

        var request = Assert.Single(_server.RequestsFor("playlist_edit"));
        Assert.Equal("5", request.Query["filter"]);
        Assert.Equal("3,1,2", request.Query["items"]);
        Assert.Equal("1,2,3", request.Query["tracks"]);
    }

    [Fact]
    public void TestRequestSimilarSongs()
    {
        _server.Actions["get_similar"] = _ => Sample("get_similar");
        var song = _library.CreateSong(_account);
        song.Id = "41039";
        _library.SaveContext();

        List<Song> similar = [];
        SingleThreadSynchronizationContext.Run(async () => similar = await _syncer.RequestSimilarSongsAsync(song, 10));

        Assert.Equal(5, similar.Count);
        Assert.Contains(song, similar);
        var request = Assert.Single(_server.RequestsFor("get_similar"));
        Assert.Equal("10", request.Query["limit"]);
        Assert.Equal("song", request.Query["type"]);
    }

    [Fact]
    public void TestScrobbleAndErrors()
    {
        _server.Actions["record_play"] = _ => Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><root><success code=\"1\">ok</success></root>");
        _server.Actions["rate"] = _ => FakeAmpacheServer.ErrorXml(4742, "Access denied");
        var song = _library.CreateSong(_account);
        song.Id = "56";
        _library.SaveContext();
        var date = new DateTime(2021, 3, 27, 3, 30, 0, DateTimeKind.Utc);

        SingleThreadSynchronizationContext.Run(() => _syncer.ScrobbleAsync(song, date));
        var request = Assert.Single(_server.RequestsFor("record_play"));
        Assert.Equal("56", request.Query["id"]);
        Assert.Equal("user", request.Query["user"]);
        Assert.Equal(new DateTimeOffset(date).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), request.Query["date"]);

        // errors of upload requests are thrown (displayable Ampache error)
        var error = Assert.Throws<ResponseError>(() => SingleThreadSynchronizationContext.Run(() => _syncer.SetRatingAsync(song, 3)));
        Assert.Equal(4742, error.StatusCode);
    }

    [Fact]
    public void TestParseLyricsIsNotSupported()
    {
        var error = Assert.Throws<ResponseError>(() => SingleThreadSynchronizationContext.Run(() => _syncer.ParseLyricsAsync("lyrics/1.xml")));
        Assert.Equal(ResponseErrorType.Xml, error.Type);
    }

    [Fact]
    public void TestBackendApi()
    {
        Assert.Equal("500000", _backendApi.ClientApiVersion);
        Assert.Equal("-", _backendApi.ServerApiVersion);
        var artworkDelegate = _backendApi.CreateArtworkDownloadDelegate();
        Assert.Equal(2, artworkDelegate.ParallelDownloadsCount);
        Assert.NotNull(artworkDelegate.ValidateDownloadedData(null, null));
    }
}
