using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsDirectoriesExample1ParserTest : AbstractSsParserTest
{
    private readonly MusicDirectory _directory;

    public SsDirectoriesExample1ParserTest()
    {
        XmlData = GetTestFileData("directory_example_1");
        _directory = Library.CreateDirectory(Account);
        Library.SaveContext();
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, SsIdParserDelegate.PrefetchIDs);
        SsParserDelegate = new SsDirectoryParserDelegate(_directory, prefetch, Account, Library);
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(
            artworkCount: 2,
            directoryCount: 2,
            directoryFetchCount: 2,
            directoryLibraryCount: 3); // one more -> it's the directory to sync to and created in setup

        Assert.Empty(_directory.Songs);
        var directories = _directory.Subdirectories.OrderBy(d => IntId(d.Id)).ToList();
        Assert.Equal(2, directories.Count);

        AssertIsAccount1(directories[0].Account);
        Assert.Equal("11", directories[0].Id);
        Assert.Equal("Arrival", directories[0].Name);
        AssertIsAccount1(directories[0].Artwork?.Account);
        Assert.Equal("", directories[0].Artwork?.Type);
        Assert.Equal("22", directories[0].Artwork?.Id);
        Assert.False(directories[0].IsCached);
        AssertIsAccount1(directories[1].Account);
        Assert.Equal("12", directories[1].Id);
        Assert.Equal("Super Trouper", directories[1].Name);
        AssertIsAccount1(directories[1].Artwork?.Account);
        Assert.Equal("", directories[1].Artwork?.Type);
        Assert.Equal("23", directories[1].Artwork?.Id);
        Assert.False(directories[1].IsCached);

        // denormalized value -> will be updated at save context
        Library.SaveContext();
        Assert.Equal(2, _directory.SubdirectoryCount);
        Assert.Equal(0, _directory.SongCount);
    }
}
