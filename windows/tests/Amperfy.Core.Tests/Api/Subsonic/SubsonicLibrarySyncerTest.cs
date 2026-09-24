using Amperfy.Core.Api.Subsonic;
using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Api.Subsonic;

/// CacheFileManager.Shared and MainThread are process wide -> run these tests serially.
[CollectionDefinition(nameof(SubsonicLibrarySyncerCollection), DisableParallelization = true)]
public sealed class SubsonicLibrarySyncerCollection;

/// End-to-end tests of the library syncer against a fake Subsonic server.
[Collection(nameof(SubsonicLibrarySyncerCollection))]
public class SubsonicLibrarySyncerTest
{
    private readonly FakeSubsonicHttpHandler _handler = new();
    private readonly LibraryStorage _library;
    private readonly Account _account;
    private readonly SubsonicServerApi _serverApi;
    private readonly SubsonicLibrarySyncer _syncer;

    public SubsonicLibrarySyncerTest()
    {
        _library = new CoreDataHelper().CreateInMemoryLibrary();
        _account = _library.GetAccount(TestAccountInfo.Create2());
        _serverApi = new SubsonicServerApi(null, new AmperfySettings(), _handler);
        _serverApi.ProvideCredentials(new LoginCredentials("https://music.example.com", "user", "pw", BackendApiType.Subsonic));
        _syncer = new SubsonicLibrarySyncer(_serverApi, _account, new AlwaysOnlineNetworkMonitor(), _library, new EventLogger(_library) { SuppressAlerts = true });
    }

    private static void Run(Func<Task> func) => SingleThreadSynchronizationContext.Run(func);

    [Fact]
    public void SyncAlbumParsesSongsAndMarksRemovedSongs()
    {
        _handler.RespondWithSample("getAlbum", "album_example_1");
        var album = _library.CreateAlbum(_account);
        album.Id = "11053";
        var removedSong = _library.CreateSong(_account);
        removedSong.Id = "removed";
        removedSong.Album = album;
        _library.SaveContext();

        Run(() => _syncer.SyncAsync(album));

        Assert.False(_library.HasChanges);
        Assert.Equal("11053", _handler.LastRequest("getAlbum").Query["id"]);
        Assert.Equal("High Voltage", album.Name);
        Assert.Equal("AC/DC", album.Artist?.Name);
        Assert.Equal(8, album.Songs.Count);
        Assert.DoesNotContain(removedSong, album.Songs);
        Assert.Equal(RemoteStatus.Deleted, removedSong.RemoteStatus);
        Assert.True(album.IsSongsMetaDataSynced);
        Assert.False(album.IsCached);
        Assert.Equal(8, _library.GetSongs(_account).Count(s => s.RemoteStatus == RemoteStatus.Available));
    }

    [Fact]
    public void SyncAlbumNotFoundMarksAlbumAsDeleted()
    {
        var album = _library.CreateAlbum(_account);
        album.Id = "gone";
        album.Name = "Gone";
        var song = _library.CreateSong(_account);
        song.Id = "s1";
        song.Album = album;
        _library.SaveContext();

        ResponseError? error = null;
        Run(async () => error = await Assert.ThrowsAsync<ResponseError>(() => _syncer.SyncAsync(album)));

        Assert.Equal(70, error?.StatusCode);
        Assert.Equal(RemoteStatus.Deleted, album.RemoteStatus);
        Assert.Equal(RemoteStatus.Deleted, song.RemoteStatus);
        Assert.False(_library.HasChanges);
    }

    [Fact]
    public void SyncSongDownloadsLyrics()
    {
        _handler.Respond("getSong", """
            <subsonic-response xmlns="http://subsonic.org/restapi" status="ok" version="1.16.1">
              <song id="71463" parent="71381" title="The Jack" album="High Voltage" artist="AC/DC" isDir="false" coverArt="71381"
                    duration="352" bitRate="128" size="5624132" contentType="audio/mpeg" albumId="11053" artistId="5432"/>
            </subsonic-response>
            """);
        _handler.RespondWithSample("getOpenSubsonicExtensions", "OpenSubsonicExtensions_example_1");
        _handler.RespondWithSample("getLyricsBySongId", "getLyricsBySongId_example_1");
        var song = _library.CreateSong(_account);
        song.Id = "71463";
        _library.SaveContext();

        LyricsList? lyrics = null;
        Run(async () =>
        {
            await _syncer.SyncAsync(song);
            lyrics = await _syncer.ParseLyricsAsync(song.LyricsRelFilePath!);
        });

        Assert.Equal("The Jack", song.Title);
        Assert.Equal("AC/DC", song.Artist?.Name);
        Assert.Equal("High Voltage", song.Album?.Name);
        Assert.Equal(CacheFileManager.GetRelLyricsFilePath(_account.Info, "71463"), song.LyricsRelFilePath);
        Assert.True(CacheFileManager.Shared.FileExists(song.LyricsRelFilePath!));
        Assert.Equal(2, lyrics?.Lyrics.Count);
        Assert.Equal("Hysteria", lyrics?.GetFirstSyncedLyricsOrUnsyncedAsDefault()?.DisplayTitle);
        Assert.False(_library.HasChanges);
    }

    [Fact]
    public void SyncSongWithoutLyricsSupport()
    {
        _handler.RespondWithSample("getSong", "album_example_1");
        var song = _library.CreateSong(_account);
        song.Id = "71463";
        _library.SaveContext();

        Run(() => _syncer.SyncAsync(song));

        Assert.Equal("The Jack", song.Title);
        Assert.Null(song.LyricsRelFilePath);
        Assert.DoesNotContain(_handler.Requests, r => r.Action == "getLyricsBySongId");
    }

    [Fact]
    public void ParseLyricsOfMissingFileThrows()
    {
        Run(async () =>
        {
            var error = await Assert.ThrowsAsync<ResponseError>(() => _syncer.ParseLyricsAsync("does/not/exist.xml"));
            Assert.Equal(ResponseErrorType.Xml, error.Type);
        });
    }

    [Fact]
    public void SyncInitialCreatesLibrary()
    {
        _handler.RespondWithSample("getGenres", "genres_example_1");
        _handler.RespondWithSample("getArtists", "artists_example_1");
        _handler.RespondWithSample("getAlbumList2", "artist_example_1");
        _handler.RespondWithSample("getPlaylists", "playlists_example_1");
        _handler.RespondWithSample("getPodcasts", "podcasts_example_1");
        var notifier = new RecordingSyncCallbacks();

        Run(() => _syncer.SyncInitialAsync(notifier));

        Assert.False(_library.HasChanges);
        Assert.Equal(7, _library.GetGenreCount(_account));
        Assert.Equal(6, _library.GetArtistCount(_account));
        Assert.Equal(15, _library.GetAlbumCount(_account));
        Assert.Equal(2, _library.GetPlaylistCount(_account));
        Assert.Equal(2, _library.GetPodcastCount(_account));
        Assert.Equal("Dr Karl and the < Naked Scientist", _library.GetPodcast(_account, "1")?.Title);
        Assert.All(_library.GetAlbums(_account), a => Assert.Equal("5432", a.Artist?.Id));

        // 36 remote albums -> 1 poll (+1 like in Swift)
        var albumListRequests = _handler.Requests.Where(r => r.Action == "getAlbumList2").ToList();
        Assert.Equal(2, albumListRequests.Count);
        Assert.Equal(["0", "500"], albumListRequests.Select(r => r.Query["offset"]).Order());
        Assert.All(albumListRequests, r => Assert.Equal("alphabeticalByName", r.Query["type"]));
        Assert.Contains((ParsedObjectType.Album, 1), notifier.Started);
        Assert.Equal(2, notifier.Parsed.Count(p => p == ParsedObjectType.Album));
    }

    [Fact]
    public void SyncDownPlaylist()
    {
        _handler.RespondWithSample("getPlaylist", "playlist_example_1");
        var playlist = _library.CreatePlaylist(_account);
        playlist.Id = "15";
        _library.SaveContext();

        Run(() => _syncer.SyncDownAsync(playlist));

        Assert.False(_library.HasChanges);
        Assert.Equal(["657", "823", "748", "848", "884", "805"], playlist.Playables.Select(p => p.Id));
        Assert.Equal(6, playlist.SongCount);
        Assert.False(playlist.IsCached);
    }

    [Fact]
    public void CreatePlaylistOnUpload()
    {
        _handler.RespondWithSample("createPlaylist", "playlist_example_1");
        _handler.Respond("updatePlaylist", FakeSubsonicHttpHandler.PingXml);
        var song = _library.CreateSong(_account);
        song.Id = "999";
        var playlist = _library.CreatePlaylist(_account);
        playlist.Name = "New";
        _library.SaveContext();

        Run(() => _syncer.SyncUploadPlaylistAddSongsAsync(playlist, [song]));

        Assert.Equal("New", _handler.LastRequest("createPlaylist").Query["name"]);
        Assert.Equal("15", playlist.Id); // id assigned from the createPlaylist response
        var update = _handler.LastRequest("updatePlaylist");
        Assert.Equal("15", update.Query["playlistId"]);
        Assert.Equal(["999"], update.QueryValues("songIdToAdd"));
    }

    [Fact]
    public void UploadErrorIsThrown()
    {
        _handler.RespondWithSample("star", "error_example_1");
        var song = _library.CreateSong(_account);
        song.Id = "1";
        _library.SaveContext();

        Run(async () =>
        {
            var error = await Assert.ThrowsAsync<ResponseError>(() => _syncer.SetFavoriteAsync(song, true));
            Assert.Equal(40, error.StatusCode);
            Assert.Equal("Wrong username or password", error.ErrorMessage);
        });
        Assert.Equal("1", _handler.LastRequest("star").Query["id"]);
    }

    [Fact]
    public void SyncRadiosMarksRemovedRadios()
    {
        _handler.RespondWithSample("getInternetRadioStations", "internetRadioStations_example_1");
        var oldRadio = _library.CreateRadio(_account);
        oldRadio.Id = "old";
        _library.SaveContext();

        Run(() => _syncer.SyncRadiosAsync());

        Assert.Equal(RemoteStatus.Deleted, oldRadio.RemoteStatus);
        Assert.Equal(["0", "1"], _library.GetRadios(_account).Select(r => r.Id).Order());
    }

    [Fact]
    public void SyncIsSkippedWhenOffline()
    {
        var syncer = new SubsonicLibrarySyncer(_serverApi, _account, new AlwaysOnlineNetworkMonitor { IsConnectedToNetwork = false }, _library, new EventLogger(_library));
        var album = _library.CreateAlbum(_account);
        album.Id = "1";
        _library.SaveContext();

        Run(() => syncer.SyncAsync(album));

        Assert.Empty(_handler.Requests);
    }

    private sealed class RecordingSyncCallbacks : ISyncCallbacks
    {
        public List<(ParsedObjectType, int)> Started { get; } = [];
        public List<ParsedObjectType> Parsed { get; } = [];
        public void NotifySyncStarted(ParsedObjectType parsedObjectType, int totalCount) => Started.Add((parsedObjectType, totalCount));
        public void NotifyParsedObject(ParsedObjectType parsedObjectType) => Parsed.Add(parsedObjectType);
    }
}

/// Artwork download delegate tests.
[Collection(nameof(SubsonicLibrarySyncerCollection))]
public class SubsonicArtworkDownloadDelegateTest
{
    [Fact]
    public void ArtworkDownloadFlow()
    {
        var library = new CoreDataHelper().CreateInMemoryLibrary();
        var credentials = new LoginCredentials("https://music.example.com", "user", "pw", BackendApiType.Subsonic);
        var account = library.GetAccount(AccountInfo.Create(credentials));
        var handler = new FakeSubsonicHttpHandler();
        var serverApi = new SubsonicServerApi(null, new AmperfySettings(), handler);
        serverApi.ProvideCredentials(credentials);
        credentials.HttpHeaders["X-Test"] = "1";
        var artworkDelegate = new SubsonicArtworkDownloadDelegate(serverApi, new AlwaysOnlineNetworkMonitor());
        var artwork = library.CreateArtwork(account);
        artwork.Id = "al-1";
        library.SaveContext();

        Assert.Equal(2, artworkDelegate.ParallelDownloadsCount);
        Assert.Equal("1", artworkDelegate.HttpHeaders["X-Test"]);

        SingleThreadSynchronizationContext.Run(async () =>
        {
            var url = await artworkDelegate.PrepareDownloadAsync(artwork, library);
            Assert.EndsWith("/rest/getCoverArt.view", url.GetLeftPart(UriPartial.Path));
            Assert.Equal("al-1", new FakeSubsonicHttpHandler.RecordedRequest(url, []).Query["id"]);

            // error response instead of an image
            var errorFile = Path.Combine(CacheFileManager.Shared.RootDirectory, "error.xml");
            File.WriteAllBytes(errorFile, TestFiles.GetTestFileData("Subsonic", "error_example_1"));
            Assert.Equal(40, artworkDelegate.ValidateDownloadedData(errorFile, url)?.StatusCode);
            Assert.NotNull(artworkDelegate.ValidateDownloadedData(null, url));

            var imageFile = Path.Combine(CacheFileManager.Shared.RootDirectory, "image.tmp");
            File.WriteAllBytes(imageFile, new byte[5000]);
            Assert.Null(artworkDelegate.ValidateDownloadedData(imageFile, url));

            await artworkDelegate.CompletedDownloadAsync(artwork, imageFile, "image/png", library);
            Assert.Equal(ImageStatus.CustomImage, artwork.Status);
            Assert.Equal(CacheFileManager.GetRelArtworkFilePath(account.Info, "al-1", ""), artwork.RelFilePath);
            Assert.True(CacheFileManager.Shared.FileExists(artwork.RelFilePath!));
            Assert.False(File.Exists(imageFile));

            var failedArtwork = library.CreateArtwork(account);
            failedArtwork.Id = "al-2";
            failedArtwork.Status = ImageStatus.NotChecked;
            await artworkDelegate.FailedDownloadAsync(failedArtwork, library);
            Assert.Equal(ImageStatus.FetchError, failedArtwork.Status);
            await Assert.ThrowsAsync<Amperfy.Core.Downloads.DownloadException>(() => artworkDelegate.PrepareDownloadAsync(library.CreateSong(account), library));
        });
    }
}
