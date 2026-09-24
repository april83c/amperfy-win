using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsMusicFolderParserTest : AbstractSsParserTest
{
    public SsMusicFolderParserTest()
    {
        XmlData = GetTestFileData("musicFolders_example_1");
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, SsIdParserDelegate.PrefetchIDs);
        SsParserDelegate = new SsMusicFolderParserDelegate(prefetch, Account, Library);
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(musicFolderCount: 3);
        Assert.Equal(3, Library.GetMusicFolderCount(Account));

        var musicFolders = Library.GetMusicFolders(Account).OrderBy(f => IntId(f.Id)).ToList();
        AssertIsAccount1(musicFolders[0].Account);
        Assert.Equal("1", musicFolders[0].Id);
        Assert.Equal("Music", musicFolders[0].Name);
        Assert.False(musicFolders[0].IsCached);
        AssertIsAccount1(musicFolders[1].Account);
        Assert.Equal("2", musicFolders[1].Id);
        Assert.Equal("Movies", musicFolders[1].Name);
        Assert.False(musicFolders[1].IsCached);
        AssertIsAccount1(musicFolders[2].Account);
        Assert.Equal("3", musicFolders[2].Id);
        Assert.Equal("Incoming", musicFolders[2].Name);
        Assert.False(musicFolders[2].IsCached);
    }
}
