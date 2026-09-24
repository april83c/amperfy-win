using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsPlaylistSongsParserTest : AbstractSsParserTest
{
    private readonly Playlist _playlist;
    private int _createdSongCount;

    public SsPlaylistSongsParserTest()
    {
        XmlData = GetTestFileData("playlist_example_1");
        _playlist = Library.CreatePlaylist(Account);
        Library.SaveContext();
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, SsIdParserDelegate.PrefetchIDs);
        SsParserDelegate = new SsPlaylistSongsParserDelegate(_playlist, Account, Library, prefetch);
    }

    private void CreateSongsInPlaylist(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            var song = Library.CreateSong(Account);
            song.Id = i.ToString();
            song.Title = i.ToString();
            _playlist.Append(song);
        }
        _createdSongCount = count;
        Library.SaveContext();
    }

    [Fact]
    public void TestPlaylistContainsBeforeLessSongsThenAfter()
    {
        CreateSongsInPlaylist(3);
        TestParsing();
    }

    [Fact]
    public void TestPlaylistContainsBeforeSameSongCountThenAfter()
    {
        CreateSongsInPlaylist(6);
        TestParsing();
    }

    [Fact]
    public void TestPlaylistContainsBeforeMoreSongsThenAfter()
    {
        CreateSongsInPlaylist(20);
        TestParsing();
    }

    [Fact]
    public void TestCacheParsing()
    {
        TestParsing();
        Assert.False(_playlist.IsCached);

        // mark all songs cached
        foreach (var song in _playlist.Playables) song.RelFilePath = "jop";
        TestParsing();
        Assert.True(_playlist.IsCached);

        // mark all songs cached exect the last one
        foreach (var song in _playlist.Playables) song.RelFilePath = "jop";
        _playlist.Playables.Last().RelFilePath = null;
        TestParsing();
        Assert.False(_playlist.IsCached);
    }

    protected override void CheckCorrectParsing()
    {
        Library.SaveContext();

        PrefetchIdTester.CheckPrefetchIdCounts(
            artworkCount: 7,
            genreNameCount: 4,
            artistCount: 5,
            albumCount: 6,
            songCount: 6,
            artworkFetchCount: 6, // the playlist cover itself is not created
            songLibraryCount: 6 + _createdSongCount);

        var playables = _playlist.Playables;
        Assert.Equal(6, playables.Count);
        Assert.Equal("657", playables[0].Id);
        Assert.Equal("823", playables[1].Id);
        Assert.Equal("748", playables[2].Id);
        Assert.Equal("848", playables[3].Id);
        Assert.Equal("884", playables[4].Id);
        Assert.Equal("805", playables[5].Id);
        Assert.Equal(1391, _playlist.Duration);
        Assert.Equal(1391, _playlist.RemoteDuration);

        Assert.Equal(6 + _createdSongCount, Library.GetSongCount(Account));

        var song = playables[0].AsSong!;
        AssertIsAccount1(song.Account);
        Assert.Equal("657", song.Id);
        Assert.Equal("Making Me Nervous", song.Title);
        Assert.Equal(2, song.Rating);
        AssertIsAccount1(song.Artist?.Account);
        Assert.Equal("45", song.Artist?.Id);
        Assert.Equal("Brad Sucks", song.Artist?.Name);
        AssertIsAccount1(song.Album?.Account);
        Assert.Equal("58", song.Album?.Id);
        Assert.Equal("I Don't Know What I'm Doing", song.Album?.Name);
        Assert.Equal(SubsonicDateParser.ParseIso8601WithFractionalSeconds("2008-04-10T07:10:32"), song.AddedDate);
        Assert.Null(song.Disk);
        Assert.Equal(1, song.Track);
        Assert.Null(song.Genre);
        Assert.Equal(159, song.Duration);
        Assert.Equal(2003, song.Year);
        Assert.Equal(202000, song.Bitrate);
        Assert.Equal("audio/mpeg", song.ContentType);
        Assert.Null(song.Url);
        Assert.Equal(4060113, song.Size);
        AssertIsAccount1(song.Artwork?.Account);
        Assert.Equal("", song.Artwork?.Type);
        Assert.Equal("655", song.Artwork?.Id);

        song = playables[2].AsSong!;
        AssertIsAccount1(song.Account);
        Assert.Equal("748", song.Id);
        Assert.Equal("Stories from Emona II", song.Title);
        Assert.Equal(0, song.Rating);
        AssertIsAccount1(song.Artist?.Account);
        Assert.Equal("51", song.Artist?.Id); // Artist not pre created
        Assert.Equal("Maya Filipič", song.Artist?.Name);
        AssertIsAccount1(song.Album?.Account);
        Assert.Equal("68", song.Album?.Id);
        Assert.Equal("Between two worlds", song.Album?.Name);
        Assert.Equal(SubsonicDateParser.ParseIso8601WithFractionalSeconds("2008-07-30T22:05:40"), song.AddedDate);
        Assert.Equal(2, song.Track);
        Assert.Equal("", song.Genre?.Id);
        Assert.Equal("Classical", song.Genre?.Name);
        Assert.Equal(335, song.Duration);
        Assert.Equal(2008, song.Year);
        Assert.Equal(176000, song.Bitrate);
        Assert.Equal("audio/mpeg", song.ContentType);
        Assert.Null(song.Url);
        Assert.Equal(7458214, song.Size);
        AssertIsAccount1(song.Artwork?.Account);
        Assert.Equal("", song.Artwork?.Type);
        Assert.Equal("746", song.Artwork?.Id);

        song = playables[5].AsSong!;
        AssertIsAccount1(song.Account);
        Assert.Equal("805", song.Id);
        Assert.Equal("Bajo siete lunas (intro)", song.Title);
        Assert.Equal(1, song.Rating);
        AssertIsAccount1(song.Artist?.Account);
        Assert.Equal("54", song.Artist?.Id);
        Assert.Equal("PeerGynt Lobogris", song.Artist?.Name);
        AssertIsAccount1(song.Album?.Account);
        Assert.Equal("74", song.Album?.Id); // Album not pre created
        Assert.Equal("Broken Dreams", song.Album?.Name);
        Assert.Equal(SubsonicDateParser.ParseIso8601WithFractionalSeconds("2008-12-19T14:13:58"), song.AddedDate);
        Assert.Equal(1, song.Track);
        AssertIsAccount1(song.Genre?.Account);
        Assert.Equal("", song.Genre?.Id);
        Assert.Equal("Blues", song.Genre?.Name);
        Assert.Equal(117, song.Duration);
        Assert.Equal(2008, song.Year);
        Assert.Equal(225000, song.Bitrate);
        Assert.Equal("audio/mpeg", song.ContentType);
        Assert.Null(song.Url);
        Assert.Equal(3363271, song.Size);
        AssertIsAccount1(song.Artwork?.Account);
        Assert.Equal("", song.Artwork?.Type);
        Assert.Equal("783", song.Artwork?.Id);
    }
}
