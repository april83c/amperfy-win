using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsDirectoriesExample2ParserTest : AbstractSsParserTest
{
    private readonly MusicDirectory _directory;

    public SsDirectoriesExample2ParserTest()
    {
        XmlData = GetTestFileData("directory_example_2");
        _directory = Library.CreateDirectory(Account);
        Library.SaveContext();
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, SsIdParserDelegate.PrefetchIDs);
        SsParserDelegate = new SsDirectoryParserDelegate(_directory, prefetch, Account, Library);
    }

    [Fact]
    public void TestCacheParsing()
    {
        TestParsing();
        Assert.False(((SsPlayableParserDelegate)SsParserDelegate!).IsCollectionCached);

        // mark all songs cached
        foreach (var song in _directory.Songs) song.RelFilePath = "jop";
        TestParsing();
        Assert.True(((SsPlayableParserDelegate)SsParserDelegate!).IsCollectionCached);

        // mark all songs cached exect the last one
        foreach (var song in _directory.Songs) song.RelFilePath = "jop";
        _directory.Songs.Last().RelFilePath = null;
        TestParsing();
        Assert.False(((SsPlayableParserDelegate)SsParserDelegate!).IsCollectionCached);
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(
            artworkCount: 2,
            genreNameCount: 1,
            artistCount: 1,
            localArtistCount: 1,
            albumCount: 1,
            songCount: 2,
            directoryLibraryCount: 1); // it's the directory to sync to and created in setup

        Assert.Empty(_directory.Subdirectories);
        var songs = _directory.Songs.OrderBy(s => IntId(s.Id)).ToList();
        Assert.Equal(2, songs.Count);

        var song = songs[0];
        AssertIsAccount1(song.Account);
        Assert.Equal("111", song.Id);
        Assert.Equal("Dancing Queen", song.Title);
        AssertIsAccount1(song.Artist?.Account);
        Assert.Equal("5432", song.Artist?.Id);
        Assert.Equal("ABBA", song.Artist?.Name);
        AssertIsAccount1(song.Album?.Account);
        Assert.Equal("11053", song.Album?.Id);
        Assert.Equal("Arrival", song.Album?.Name);
        Assert.Null(song.Disk);
        Assert.Equal(7, song.Track);
        AssertIsAccount1(song.Genre?.Account);
        Assert.Equal("", song.Genre?.Id);
        Assert.Equal("Pop", song.Genre?.Name);
        Assert.Equal(146, song.Duration);
        Assert.Equal(1978, song.Year);
        Assert.Equal(128000, song.Bitrate);
        Assert.Equal("audio/mpeg", song.ContentType);
        Assert.Null(song.Url);
        Assert.Equal(8421341, song.Size);
        AssertIsAccount1(song.Artwork?.Account);
        Assert.Equal("", song.Artwork?.Type);
        Assert.Equal("24", song.Artwork?.Id);

        song = songs[1];
        AssertIsAccount1(song.Account);
        Assert.Equal("112", song.Id);
        Assert.Equal("Money, Money, Money", song.Title);
        AssertIsAccount1(song.Artist?.Account);
        Assert.Equal("ABBA", song.Artist?.Name);
        Assert.Null(song.Album);
        Assert.Null(song.Disk);
        Assert.Equal(7, song.Track);
        AssertIsAccount1(song.Genre?.Account);
        Assert.Equal("", song.Genre?.Id);
        Assert.Equal("Pop", song.Genre?.Name);
        Assert.Equal(208, song.Duration);
        Assert.Equal(1978, song.Year);
        Assert.Equal(128000, song.Bitrate);
        Assert.Equal("audio/flac", song.ContentType);
        Assert.Null(song.Url);
        Assert.Equal(4910028, song.Size);
        AssertIsAccount1(song.Artwork?.Account);
        Assert.Equal("", song.Artwork?.Type);
        Assert.Equal("25", song.Artwork?.Id);

        // denormalized value -> will be updated at save context
        Library.SaveContext();
        Assert.Equal(0, _directory.SubdirectoryCount);
        Assert.Equal(2, _directory.SongCount);
    }
}
