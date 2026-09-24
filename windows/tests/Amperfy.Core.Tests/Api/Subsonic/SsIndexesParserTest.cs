using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsIndexesParserTest : AbstractSsParserTest
{
    private readonly MusicFolder _musicFolder;

    public SsIndexesParserTest()
    {
        XmlData = GetTestFileData("indexes_example_1");
        _musicFolder = Library.CreateMusicFolder(Account);
        Library.SaveContext();
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, SsIdParserDelegate.PrefetchIDs);
        SsParserDelegate = new SsDirectoryParserDelegate(_musicFolder, prefetch, Account, Library);
    }

    [Fact]
    public void TestCacheParsing()
    {
        TestParsing();
        Assert.False(((SsPlayableParserDelegate)SsParserDelegate!).IsCollectionCached);

        // mark all songs cached
        foreach (var song in _musicFolder.Songs) song.RelFilePath = "jop";
        TestParsing();
        Assert.True(((SsPlayableParserDelegate)SsParserDelegate!).IsCollectionCached);

        // mark all songs cached exect the last one
        foreach (var song in _musicFolder.Songs) song.RelFilePath = "jop";
        _musicFolder.Songs.Last().RelFilePath = null;
        TestParsing();
        Assert.False(((SsPlayableParserDelegate)SsParserDelegate!).IsCollectionCached);
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(
            artworkCount: 2,
            genreNameCount: 1,
            localArtistCount: 1,
            songCount: 2,
            directoryCount: 4,
            musicFolderLibraryCount: 1); // it's the one to sync to and created in setup

        var directories = _musicFolder.Directories.OrderBy(d => IntId(d.Id)).ToList();
        Assert.Equal(4, directories.Count);

        AssertIsAccount1(directories[0].Account);
        Assert.Equal("1", directories[0].Id);
        Assert.Equal("ABBA", directories[0].Name);
        AssertIsAccount1(directories[1].Account);
        Assert.Equal("2", directories[1].Id);
        Assert.Equal("Alanis Morisette", directories[1].Name);
        AssertIsAccount1(directories[2].Account);
        Assert.Equal("3", directories[2].Id);
        Assert.Equal("Alphaville", directories[2].Name);
        AssertIsAccount1(directories[3].Account);
        Assert.Equal("4", directories[3].Id);
        Assert.Equal("Bob Dylan", directories[3].Name);

        var songs = _musicFolder.Songs.OrderBy(s => IntId(s.Id)).ToList();
        Assert.Equal(2, songs.Count);

        var song = songs[0];
        AssertIsAccount1(song.Account);
        Assert.Equal("111", song.Id);
        Assert.Equal("Dancing Queen", song.Title);
        AssertIsAccount1(song.Artist?.Account);
        Assert.Equal("ABBA", song.Artist?.Name);
        Assert.Null(song.Album);
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
    }
}
