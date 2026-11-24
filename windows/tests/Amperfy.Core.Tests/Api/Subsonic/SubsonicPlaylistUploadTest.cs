using Amperfy.Core.Api;
using Amperfy.Core.Api.Subsonic;
using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Api.Subsonic;

/// Playlist uploads of large playlists: the updatePlaylist requests are split so the URLs stay
/// short (servers answer "414 Request-URI Too Large" otherwise) and give the right final order.
[Collection(nameof(SubsonicLibrarySyncerCollection))]
public class SubsonicPlaylistUploadTest
{
    private const int MaxUrlLength = 6000;

    private readonly FakeSubsonicHttpHandler _handler = new();
    private readonly LibraryStorage _library;
    private readonly Account _account;
    private readonly SubsonicLibrarySyncer _syncer;

    public SubsonicPlaylistUploadTest()
    {
        _library = new CoreDataHelper().CreateInMemoryLibrary();
        _account = _library.GetAccount(TestAccountInfo.Create2());
        var serverApi = new SubsonicServerApi(null, new AmperfySettings(), _handler);
        serverApi.ProvideCredentials(new LoginCredentials("https://music.example.com/apps/music/subsonic", "user", "pw", BackendApiType.Subsonic));
        _syncer = new SubsonicLibrarySyncer(serverApi, _account, new AlwaysOnlineNetworkMonitor(), _library, new EventLogger(_library) { SuppressAlerts = true });
        _handler.Respond("updatePlaylist", FakeSubsonicHttpHandler.PingXml);
    }

    private static void Run(Func<Task> func) => SingleThreadSynchronizationContext.Run(func);

    private static List<string> TrackIds(int count) => Enumerable.Range(0, count).Select(i => $"track-{500 + i * 3}").ToList();

    private Playlist CreatePlaylist(IReadOnlyList<string> songIds)
    {
        var songs = songIds.Select(id =>
        {
            var song = _library.CreateSong(_account);
            song.Id = id;
            return (AbstractPlayable)song;
        }).ToList();
        var playlist = _library.CreatePlaylist(_account);
        playlist.Id = "113";
        playlist.Name = "00 - Everything";
        playlist.Append(songs);
        _library.SaveContext();
        return playlist;
    }

    private void ServerHasPlaylist(IEnumerable<string> songIds)
    {
        var entries = string.Concat(songIds.Select(id => $"""<entry id="{id}" isDir="false" title="{id}"/>"""));
        _handler.Respond("getPlaylist",
            $"""<?xml version="1.0" encoding="UTF-8"?><subsonic-response xmlns="http://subsonic.org/restapi" status="ok" version="1.16.1"><playlist id="113" name="00 - Everything">{entries}</playlist></subsonic-response>""");
    }

    private List<FakeSubsonicHttpHandler.RecordedRequest> Updates() => _handler.Requests.Where(r => r.Action == "updatePlaylist").ToList();

    /// Applies the recorded updatePlaylist requests like a Subsonic server: the indexes of one
    /// request refer to the order before it, songs are appended afterwards.
    private List<string> ApplyUpdates(IEnumerable<string> serverSongIds)
    {
        var order = serverSongIds.ToList();
        foreach (var update in Updates())
        {
            var remove = update.QueryValues("songIndexToRemove").Select(int.Parse).ToHashSet();
            Assert.All(remove, i => Assert.InRange(i, 0, order.Count - 1));
            order = order.Where((_, i) => !remove.Contains(i)).Concat(update.QueryValues("songIdToAdd")).ToList();
        }
        return order;
    }

    private void AssertShortUrls() =>
        Assert.All(_handler.Requests, r => Assert.True(r.Url.AbsoluteUri.Length < MaxUrlLength, $"{r.Action}: {r.Url.AbsoluteUri.Length} chars"));

    [Fact]
    public void OrderUpload_LargePlaylist_SplitsRequestsAndAppendsBeforeRemoving()
    {
        var ids = TrackIds(1365);
        var playlist = CreatePlaylist(ids);
        ServerHasPlaylist(ids);
        var items = playlist.Items.ToList();
        playlist.MoveItems([items[0], items[2]], items[1300]); // cut the first and third song, paste near the end
        _library.SaveContext();

        Run(() => _syncer.SyncUploadPlaylistOrderAsync(playlist));

        Assert.Equal(playlist.Playables.Select(p => p.Id), ApplyUpdates(ids));
        AssertShortUrls();
        var updates = Updates();
        Assert.True(updates.Count > 2);
        // all songs are appended before anything is removed: an interrupted upload loses nothing
        var firstRemoval = updates.FindIndex(u => u.QueryValues("songIndexToRemove").Count > 0);
        Assert.All(updates.Take(firstRemoval), u => Assert.Empty(u.QueryValues("songIndexToRemove")));
        Assert.All(updates.Skip(firstRemoval), u => Assert.Empty(u.QueryValues("songIdToAdd")));
    }

    [Fact]
    public void OrderUpload_KeepsTheUnchangedStart()
    {
        var ids = TrackIds(1365);
        var playlist = CreatePlaylist(ids);
        ServerHasPlaylist(ids);
        var items = playlist.Items.ToList();
        playlist.MoveItems([items[1360]], items[1349]);
        _library.SaveContext();

        Run(() => _syncer.SyncUploadPlaylistOrderAsync(playlist));

        Assert.Equal(playlist.Playables.Select(p => p.Id), ApplyUpdates(ids));
        var updates = Updates();
        Assert.Equal(2, updates.Count);
        Assert.Equal(15, updates[0].QueryValues("songIdToAdd").Count);
        Assert.Equal(Enumerable.Range(1350, 15).Select(i => i.ToString()), updates[1].QueryValues("songIndexToRemove"));
    }

    [Fact]
    public void OrderUpload_ComparesWithTheServerOrder()
    {
        // an earlier upload failed: the server still has the old order, the app the new one
        var ids = TrackIds(30);
        var playlist = CreatePlaylist(ids);
        var items = playlist.Items.ToList();
        playlist.MoveItems([items[0]], items[29]);
        _library.SaveContext();
        ServerHasPlaylist(ids);

        Run(() => _syncer.SyncUploadPlaylistOrderAsync(playlist));

        Assert.Equal(playlist.Playables.Select(p => p.Id), ApplyUpdates(ids));
    }

    [Fact]
    public void OrderUpload_UnchangedOrder_SendsNoUpdate()
    {
        var ids = TrackIds(50);
        var playlist = CreatePlaylist(ids);
        ServerHasPlaylist(ids);

        Run(() => _syncer.SyncUploadPlaylistOrderAsync(playlist));

        Assert.Empty(Updates());
    }

    [Fact]
    public void DeleteManySongs_SplitsRequestsFromTheEnd()
    {
        var ids = TrackIds(1500);
        var playlist = CreatePlaylist(ids);
        var indexes = Enumerable.Range(0, 1500).Where(i => i % 3 != 1).ToList();

        Run(() => _syncer.SyncUploadPlaylistDeleteSongsAsync(playlist, indexes));

        Assert.Equal(ids.Where((_, i) => i % 3 == 1), ApplyUpdates(ids));
        AssertShortUrls();
        Assert.True(Updates().Count > 1);
    }

    [Fact]
    public void AddManySongs_SplitsRequestsInOrder()
    {
        var ids = TrackIds(1200);
        var playlist = CreatePlaylist([]);
        var songs = ids.Select(id =>
        {
            var song = _library.CreateSong(_account);
            song.Id = id;
            return song;
        }).ToList();
        _library.SaveContext();

        Run(() => _syncer.SyncUploadPlaylistAddSongsAsync(playlist, songs));

        Assert.Equal(ids, ApplyUpdates([]));
        AssertShortUrls();
        Assert.True(Updates().Count > 1);
    }

    [Fact]
    public void ParsePlaylistEntryIds_ReadsTheSongsInOrder()
    {
        var data = TestFiles.GetTestFileData("Subsonic", "playlist_example_1");
        Assert.Equal(["657", "823", "748", "848", "884", "805"], SubsonicLibrarySyncer.ParsePlaylistEntryIds(data));
    }
}

public class RequestChunksTest
{
    [Fact]
    public void Split_KeepsOrderWithinTheBudget()
    {
        var chunks = RequestChunks.Split(Enumerable.Range(1, 10), _ => 3, maxLength: 10);
        Assert.Equal([[1, 2, 3], [4, 5, 6], [7, 8, 9], [10]], chunks);
    }

    [Fact]
    public void Split_OversizedItemGetsItsOwnChunk()
    {
        var chunks = RequestChunks.Split(["a", "bbbbbbbbbbbb", "c"], s => s.Length, maxLength: 5);
        Assert.Equal([["a"], ["bbbbbbbbbbbb"], ["c"]], chunks);
    }

    [Theory]
    [InlineData("ABCDE", "ABCDE", 5, "", "")]
    [InlineData("ABCDE", "ABDCE", 2, "DCE", "2,3,4")]
    [InlineData("ABCDE", "BACDE", 0, "BACDE", "0,1,2,3,4")]
    [InlineData("ABC", "ABCD", 3, "D", "")]
    [InlineData("ABCD", "AB", 2, "", "2,3")]
    public void PlaylistOrderUpload_KeepsCommonStart(string server, string updated, int kept, string append, string remove)
    {
        var upload = PlaylistOrderUpload.Create(server.Select(c => c.ToString()).ToList(), updated.Select(c => c.ToString()).ToList());
        Assert.Equal(kept, upload.KeptCount);
        Assert.Equal(append, string.Concat(upload.SongIdsToAppend));
        Assert.Equal(remove, string.Join(",", upload.IndexesToRemove));
    }
}
