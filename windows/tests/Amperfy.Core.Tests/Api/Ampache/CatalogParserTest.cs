using System.Globalization;
using Amperfy.Core.Api.Ampache;
using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Api.Ampache;

/// Port of CatalogParserTest.swift
public class CatalogParserTest : AbstractAmpacheTest
{
    public CatalogParserTest()
    {
        XmlData = GetTestFileData("catalogs");
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, IdParserDelegate.PrefetchIDs);
        ParserDelegate = new CatalogParserDelegate(prefetch, Account, Library);
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(musicFolderCount: 4);
        Assert.Equal(4, Library.GetMusicFolderCount(Account));
        var musicFolders = Library.GetMusicFolders(Account).OrderBy(f => int.Parse(f.Id, CultureInfo.InvariantCulture)).ToList();

        (string Id, string Name)[] expected = [("1", "music"), ("2", "video"), ("3", "podcast"), ("4", "upload")];
        for (var i = 0; i < expected.Length; i++)
        {
            AssertTestAccount1(musicFolders[i].Account);
            Assert.Equal(expected[i].Id, musicFolders[i].Id);
            Assert.Equal(expected[i].Name, musicFolders[i].Name);
            Assert.False(musicFolders[i].IsCached);
        }
    }
}
