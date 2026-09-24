using Amperfy.Core.Api.Ampache;

namespace Amperfy.Core.Tests.Api.Ampache;

public class AlbumParserTest : AbstractAmpacheTest
{
    public AlbumParserTest()
    {
        XmlData = GetTestFileData("albums");
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, IdParserDelegate.PrefetchIDs);
        ParserDelegate = new AlbumParserDelegate(prefetch, Account, Library, parseNotifier: null);
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(artworkCount: 3, genreIdCount: 2, artistCount: 3, albumCount: 3);

        var albums = Library.GetAlbums(Account).OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
        Assert.Equal(3, albums.Count);
        Assert.Equal(2, Library.GetGenreCount(Account));

        var album = albums[0];
        AssertTestAccount1(album.Account);
        Assert.Equal("12", album.Id);
        Assert.Equal("Buried in Nausea", album.Name);
        Assert.Equal(2, album.Rating);
        AssertTestAccount1(album.Artist?.Account);
        Assert.Equal("19", album.Artist?.Id);
        Assert.Equal("Various Artists", album.Artist?.Name);
        Assert.Equal(2012, album.Year);
        Assert.False(album.IsCached);
        Assert.Equal(1879, album.Duration);
        Assert.Equal(1879, album.RemoteDuration);
        Assert.Equal(9, album.RemoteSongCount);
        AssertTestAccount1(album.Genre?.Account);
        Assert.Equal("7", album.Genre?.Id);
        Assert.Equal("Punk", album.Genre?.Name);
        AssertTestAccount1(album.Artwork?.Account);
        Assert.Equal("12", album.Artwork?.Id);
        Assert.Equal("album", album.Artwork?.Type);

        album = albums[1];
        AssertTestAccount1(album.Account);
        Assert.Equal("98", album.Id);
        Assert.Equal("Blibb uu", album.Name);
        Assert.Equal(0, album.Rating);
        AssertTestAccount1(album.Artist?.Account);
        Assert.Equal("12", album.Artist?.Id); // Artist not pre created
        Assert.Equal("9958A", album.Artist?.Name);
        Assert.Equal(1974, album.Year);
        Assert.False(album.IsCached);
        Assert.Equal(4621, album.Duration);
        Assert.Equal(4621, album.RemoteDuration);
        Assert.Equal(1, album.RemoteSongCount);
        Assert.Null(album.Genre);
        AssertTestAccount1(album.Artwork?.Account);
        Assert.Equal("98", album.Artwork?.Id);
        Assert.Equal("album", album.Artwork?.Type);

        album = albums[2];
        AssertTestAccount1(album.Account);
        Assert.Equal("99", album.Id);
        Assert.Equal("123 GOo", album.Name);
        Assert.Equal(0, album.Rating);
        AssertTestAccount1(album.Artist?.Account);
        Assert.Equal("91", album.Artist?.Id);
        Assert.Equal("ZZZasdf", album.Artist?.Name);
        Assert.Equal(2002, album.Year);
        Assert.False(album.IsCached);
        Assert.Equal(2, album.Duration);
        Assert.Equal(2, album.RemoteDuration);
        Assert.Equal(105, album.RemoteSongCount);
        AssertTestAccount1(album.Genre?.Account);
        Assert.Equal("1", album.Genre?.Id);
        Assert.Equal("Blub", album.Genre?.Name);
        AssertTestAccount1(album.Artwork?.Account);
        Assert.Equal("99", album.Artwork?.Id);
        Assert.Equal("album", album.Artwork?.Type);
    }
}
