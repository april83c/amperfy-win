using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsAlbumParserTest : AbstractSsParserTest
{
    public SsAlbumParserTest()
    {
        XmlData = GetTestFileData("artist_example_1");
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, SsIdParserDelegate.PrefetchIDs);
        SsParserDelegate = new SsAlbumParserDelegate(prefetch, Account, Library, parseNotifier: null);
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(
            artworkCount: 16,
            artistCount: 1,
            albumCount: 15,
            artworkFetchCount: 15); // artist artwork is not created

        var albums = Library.GetAlbums(Account).OrderBy(a => IntId(a.Id)).ToList();
        Assert.Equal(15, albums.Count);

        var album = albums[1];
        AssertIsAccount1(album.Account);
        Assert.Equal("11047", album.Id);
        Assert.Equal("Back In Black", album.Name);
        Assert.Equal(1, album.Rating);
        AssertIsAccount1(album.Artist?.Account);
        Assert.Equal("5432", album.Artist?.Id); // Artist not pre created
        Assert.Equal("AC/DC", album.Artist?.Name);
        Assert.Equal(0, album.Year);
        Assert.False(album.IsCached);
        Assert.Equal(2534, album.Duration);
        Assert.Equal(2534, album.RemoteDuration);
        Assert.Equal(10, album.RemoteSongCount);
        Assert.Null(album.Genre);
        AssertIsAccount1(album.Artwork?.Account);
        Assert.Equal("", album.Artwork?.Type);
        Assert.Equal("al-11047", album.Artwork?.Id);

        album = albums[6];
        AssertIsAccount1(album.Account);
        Assert.Equal("11052", album.Id);
        Assert.Equal("For Those About To Rock", album.Name);
        Assert.Equal(0, album.Rating);
        AssertIsAccount1(album.Artist?.Account);
        Assert.Equal("5432", album.Artist?.Id);
        Assert.Equal("AC/DC", album.Artist?.Name);
        Assert.Equal(0, album.Year);
        Assert.False(album.IsCached);
        Assert.Equal(2403, album.Duration);
        Assert.Equal(2403, album.RemoteDuration);
        Assert.Equal(10, album.RemoteSongCount);
        Assert.Null(album.Genre);
        AssertIsAccount1(album.Artwork?.Account);
        Assert.Equal("", album.Artwork?.Type);
        Assert.Equal("al-11052", album.Artwork?.Id);

        album = albums[7];
        AssertIsAccount1(album.Account);
        Assert.Equal("11053", album.Id);
        Assert.Equal("High Voltage", album.Name);
        Assert.Equal(0, album.Rating);
        AssertIsAccount1(album.Artist?.Account);
        Assert.Equal("5432", album.Artist?.Id);
        Assert.Equal("AC/DC", album.Artist?.Name);
        Assert.Equal(0, album.Year);
        Assert.False(album.IsCached);
        Assert.Equal(2414, album.Duration);
        Assert.Equal(2414, album.RemoteDuration);
        Assert.Equal(8, album.RemoteSongCount);
        Assert.Null(album.Genre);
        AssertIsAccount1(album.Artwork?.Account);
        Assert.Equal("", album.Artwork?.Type);
        Assert.Equal("al-11053", album.Artwork?.Id);

        album = albums[14];
        AssertIsAccount1(album.Account);
        Assert.Equal("11061", album.Id);
        Assert.Equal("Who Made Who", album.Name);
        Assert.Equal(5, album.Rating);
        AssertIsAccount1(album.Artist?.Account);
        Assert.Equal("5432", album.Artist?.Id);
        Assert.Equal("AC/DC", album.Artist?.Name);
        Assert.Equal(0, album.Year);
        Assert.False(album.IsCached);
        Assert.Equal(2291, album.Duration);
        Assert.Equal(2291, album.RemoteDuration);
        Assert.Equal(9, album.RemoteSongCount);
        Assert.Null(album.Genre);
        AssertIsAccount1(album.Artwork?.Account);
        Assert.Equal("", album.Artwork?.Type);
        Assert.Equal("al-11061", album.Artwork?.Id);
    }
}
