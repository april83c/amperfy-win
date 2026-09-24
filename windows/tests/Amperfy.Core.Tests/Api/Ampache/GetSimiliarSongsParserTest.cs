using System.Globalization;
using Amperfy.Core.Api.Ampache;

namespace Amperfy.Core.Tests.Api.Ampache;

public class GetSimiliarSongsParserTest : AbstractAmpacheTest
{
    public GetSimiliarSongsParserTest()
    {
        XmlData = GetTestFileData("get_similar");
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, IdParserDelegate.PrefetchIDs);
        ParserDelegate = new SongParserDelegate(prefetch, Account, Library, parseNotifier: null);
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(artworkCount: 0, genreIdCount: 0, artistCount: 3, albumCount: 3, songCount: 5);

        var songs = Library.GetSongs(Account).OrderBy(s => int.Parse(s.Id, CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(5, songs.Count);

        var song = songs[1];
        AssertTestAccount1(song.Account);
        Assert.Equal("41039", song.Id);
        Assert.Equal("Somebody Told Me", song.Title);
        AssertTestAccount1(song.Artist?.Account);
        Assert.Equal("2277", song.Artist?.Id);
        Assert.Equal("The Killers", song.Artist?.Name);
        AssertTestAccount1(song.Album?.Account);
        Assert.Equal("50260", song.Album?.Id); // Album not pre created
        Assert.Equal("Hot Fuss", song.Album?.Name);
        Assert.Null(song.Genre);

        song = songs[4];
        AssertTestAccount1(song.Account);
        Assert.Equal("777754", song.Id);
        Assert.Equal("Fluorescent Adolescent", song.Title);
        AssertTestAccount1(song.Artist?.Account);
        Assert.Equal("10468", song.Artist?.Id);
        Assert.Equal("Arctic Monkeys", song.Artist?.Name);
        AssertTestAccount1(song.Album?.Account);
        Assert.Equal("84543", song.Album?.Id);
        Assert.Equal("Favourite Worst Nightmare", song.Album?.Name);
    }
}
