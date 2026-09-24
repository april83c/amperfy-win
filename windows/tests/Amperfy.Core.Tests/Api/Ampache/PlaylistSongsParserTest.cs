using System.Globalization;
using Amperfy.Core.Api.Ampache;

namespace Amperfy.Core.Tests.Api.Ampache;

public class PlaylistSongsParserTest : AbstractAmpacheTest
{
    private readonly Playlist _playlist;
    private int _createdSongCount;

    public PlaylistSongsParserTest()
    {
        _playlist = Library.CreatePlaylist(Account);
        XmlData = GetTestFileData("playlist_songs");
        CreateTestArtists();
        CreateTestAlbums();
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, IdParserDelegate.PrefetchIDs);
        ParserDelegate = new PlaylistSongsParserDelegate(_playlist, prefetch, Account, Library);
    }

    private void CreateTestArtists()
    {
        (string Id, string Name)[] artists = [("27", "Chi.Otic"), ("20", "R/B"), ("14", "Nofi/found."), ("2", "Synthetic")];
        foreach (var (id, name) in artists)
        {
            var artist = Library.CreateArtist(Account);
            artist.Id = id;
            artist.Name = name;
        }
        Library.SaveContext();
    }

    private void CreateTestAlbums()
    {
        var album = Library.CreateAlbum(Account);
        album.Id = "12";
        album.Name = "Buried in Nausea";
        album = Library.CreateAlbum(Account);
        album.Id = "2";
        album.Name = "Colorsmoke EP";
        Library.SaveContext();
    }

    private void CreatePlaylistSongs(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            var song = Library.CreateSong(Account);
            song.Id = i.ToString(CultureInfo.InvariantCulture);
            song.Title = i.ToString(CultureInfo.InvariantCulture);
            _playlist.Append(song);
        }
        _createdSongCount = count;
        Library.SaveContext();
    }

    [Fact]
    public void TestPlaylistContainsBeforeLessSongsThenAfter()
    {
        CreatePlaylistSongs(3);
        TestParsing();
    }

    [Fact]
    public void TestPlaylistContainsBeforeSameSongCountThenAfter()
    {
        CreatePlaylistSongs(6);
        TestParsing();
    }

    [Fact]
    public void TestPlaylistContainsBeforeMoreSongsThenAfter()
    {
        CreatePlaylistSongs(20);
        TestParsing();
    }

    [Fact]
    public void TestCacheParsing()
    {
        TestParsing();
        Assert.False(_playlist.IsCached);
        foreach (var song in _playlist.Playables) song.RelFilePath = "jop";
        TestParsing();
        Assert.True(_playlist.IsCached);
        foreach (var song in _playlist.Playables) song.RelFilePath = "jop";
        _playlist.Playables[^1].RelFilePath = null;
        TestParsing();
        Assert.False(_playlist.IsCached);
    }

    protected override void CheckCorrectParsing()
    {
        Library.SaveContext();
        PrefetchIdTester.CheckPrefetchIdCounts(artworkCount: 3, genreIdCount: 4, artistCount: 4, albumCount: 2, songCount: 4,
            songLibraryCount: 4 + _createdSongCount);
        Assert.Equal(4 + _createdSongCount, Library.GetSongCount(Account));
        Assert.Equal(4, _playlist.Playables.Count);
        Assert.Equal(1442, _playlist.Duration);
        Assert.Equal(1442, _playlist.RemoteDuration);

        var song = _playlist.Playables[0].AsSong!;
        AssertTestAccount1(song.Account);
        Assert.Equal("56", song.Id);
        Assert.Equal("Black&BlueSmoke", song.Title);
        Assert.Equal(4, song.Rating);
        AssertTestAccount1(song.Artist?.Account);
        Assert.Equal("2", song.Artist?.Id);
        Assert.Equal("Synthetic", song.Artist?.Name);
        AssertTestAccount1(song.Album?.Account);
        Assert.Equal("2", song.Album?.Id);
        Assert.Equal("Colorsmoke EP", song.Album?.Name);
        Assert.Null(song.AddedDate);
        Assert.Equal("1", song.Disk);
        Assert.Equal(1, song.Track);
        AssertTestAccount1(song.Genre?.Account);
        Assert.Equal("1", song.Genre?.Id);
        Assert.Equal("Electronic", song.Genre?.Name);
        Assert.Equal(500, song.Duration);
        Assert.Equal(2007, song.Year);
        Assert.Equal(64000, song.Bitrate);
        Assert.Equal("audio/mpeg", song.ContentType);
        Assert.Equal("https://music.com.au/play/index.php?ssid=cfj3f237d563f479f5223k23189dbb34&type=song&oid=56&uid=4&player=api&name=Synthetic%20-%20Black-BlueSmoke.mp3", song.Url);
        Assert.Equal(4010069, song.Size);
        AssertTestAccount1(song.Artwork?.Account);
        Assert.Equal("album", song.Artwork?.Type);
        Assert.Equal("2", song.Artwork?.Id);

        song = _playlist.Playables[1].AsSong!;
        AssertTestAccount1(song.Account);
        Assert.Equal("107", song.Id);
        Assert.Equal("Arrest Me", song.Title);
        Assert.Equal(1, song.Rating);
        AssertTestAccount1(song.Artist?.Account);
        Assert.Equal("20", song.Artist?.Id);
        Assert.Equal("R/B", song.Artist?.Name);
        AssertTestAccount1(song.Album?.Account);
        Assert.Equal("12", song.Album?.Id);
        Assert.Equal("Buried in Nausea", song.Album?.Name);
        Assert.Null(song.AddedDate);
        Assert.Equal("1", song.Disk);
        Assert.Equal(9, song.Track);
        AssertTestAccount1(song.Genre?.Account);
        Assert.Equal("7", song.Genre?.Id);
        Assert.Equal("Punk", song.Genre?.Name);
        Assert.Equal(96, song.Duration);
        Assert.Equal(2012, song.Year);
        Assert.Equal(252864, song.Bitrate);
        Assert.Equal("audio/mp4", song.ContentType);
        Assert.Equal("https://music.com.au/play/index.php?ssid=cfj3f237d563f479f5223k23189dbb34&type=song&oid=107&uid=4&player=api&name=R-B%20-%20Arrest%20Me.m4a", song.Url);
        Assert.Equal(3091727, song.Size);
        AssertTestAccount1(song.Artwork?.Account);
        Assert.Equal("album", song.Artwork?.Type);
        Assert.Equal("12", song.Artwork?.Id);

        song = _playlist.Playables[2].AsSong!;
        AssertTestAccount1(song.Account);
        Assert.Equal("115", song.Id);
        Assert.Equal("Are we going Crazy", song.Title);
        Assert.Equal(0, song.Rating);
        AssertTestAccount1(song.Artist?.Account);
        Assert.Equal("27", song.Artist?.Id);
        Assert.Equal("Chi.Otic", song.Artist?.Name);
        AssertTestAccount1(song.Album?.Account);
        Assert.Equal("12", song.Album?.Id);
        Assert.Equal("Buried in Nausea", song.Album?.Name);
        Assert.Equal("1", song.Disk);
        Assert.Equal(7, song.Track);
        Assert.Null(song.AddedDate);
        Assert.Null(song.Genre);
        Assert.Equal(433, song.Duration);
        Assert.Equal(2012, song.Year);
        Assert.Equal(32582, song.Bitrate);
        Assert.Equal("audio/x-ms-wma", song.ContentType);
        Assert.Equal("https://music.com.au/play/index.php?ssid=cfj3f237d563f479f5223k23189dbb34&type=song&oid=115&uid=4&player=api&name=Chi.Otic%20-%20Are%20we%20going%20Crazy.wma", song.Url);
        Assert.Equal(1776580, song.Size);
        AssertTestAccount1(song.Artwork?.Account);
        Assert.Equal("album", song.Artwork?.Type);
        Assert.Equal("12", song.Artwork?.Id);

        song = _playlist.Playables[3].AsSong!;
        AssertTestAccount1(song.Account);
        Assert.Equal("85", song.Id);
        Assert.Equal("Beq Ultra Fat", song.Title);
        AssertTestAccount1(song.Artist?.Account);
        Assert.Equal("14", song.Artist?.Id);
        Assert.Equal("Nofi/found.", song.Artist?.Name);
        Assert.Null(song.Album);
        Assert.Null(song.AddedDate);
        Assert.Equal("1", song.Disk);
        Assert.Equal(4, song.Track);
        AssertTestAccount1(song.Genre?.Account);
        Assert.Equal("6", song.Genre?.Id);
        Assert.Equal("Dance", song.Genre?.Name);
        Assert.Equal(413, song.Duration);
        Assert.Equal(0, song.Year);
        Assert.Equal(192000, song.Bitrate);
        Assert.Equal("audio/mpeg", song.ContentType);
        Assert.Equal("https://music.com.au/play/index.php?ssid=cfj3f237d563f479f5223k23189dbb34&type=song&oid=85&uid=4&player=api&name=Nofi-found.%20-%20Beq%20Ultra%20Fat.mp3", song.Url);
        Assert.Equal(9935896, song.Size);
        AssertTestAccount1(song.Artwork?.Account);
        Assert.Equal("album", song.Artwork?.Type);
        Assert.Equal("8", song.Artwork?.Id);
    }
}
