using Amperfy.Core.Api.Ampache;

namespace Amperfy.Core.Tests.Api.Ampache;

public class ArtistParserTest : AbstractAmpacheTest
{
    public ArtistParserTest()
    {
        XmlData = GetTestFileData("artists");
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, IdParserDelegate.PrefetchIDs);
        ParserDelegate = new ArtistParserDelegate(prefetch, Account, Library, parseNotifier: null);
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(artworkCount: 4, genreIdCount: 1, artistCount: 4);

        var artists = Library.GetArtists(Account);
        Assert.Equal(4, artists.Count);
        Assert.Equal(1, Library.GetGenreCount(Account));

        var artist = artists[0];
        AssertTestAccount1(artist.Account);
        Assert.Equal("16", artist.Id);
        Assert.Equal("CARNÚN", artist.Name);
        Assert.Equal(3, artist.Rating);
        Assert.Equal(4282, artist.Duration);
        Assert.Equal(4282, artist.RemoteDuration);
        Assert.Equal(1, artist.RemoteAlbumCount);
        AssertTestAccount1(artist.Artwork?.Account);
        Assert.Equal("artist", artist.Artwork?.Type);
        Assert.Equal("16", artist.Artwork?.Id);

        artist = artists[1];
        AssertTestAccount1(artist.Account);
        Assert.Equal("27", artist.Id);
        Assert.Equal("Chi.Otic", artist.Name);
        Assert.Equal(0, artist.Rating);
        Assert.Equal(433, artist.Duration);
        Assert.Equal(433, artist.RemoteDuration);
        Assert.Equal(0, artist.RemoteAlbumCount);
        AssertTestAccount1(artist.Artwork?.Account);
        Assert.Equal("artist", artist.Artwork?.Type);
        Assert.Equal("27", artist.Artwork?.Id);

        artist = artists[3];
        AssertTestAccount1(artist.Account);
        Assert.Equal("13", artist.Id);
        Assert.Equal("IOK-1", artist.Name);
        Assert.Equal(5, artist.Rating);
        Assert.Equal(3428, artist.Duration);
        Assert.Equal(3428, artist.RemoteDuration);
        Assert.Equal(1, artist.RemoteAlbumCount);
        AssertTestAccount1(artist.Artwork?.Account);
        Assert.Equal("artist", artist.Artwork?.Type);
        Assert.Equal("13", artist.Artwork?.Id);
        AssertTestAccount1(artist.Genre?.Account);
        Assert.Equal("4", artist.Genre?.Id);
        Assert.Equal("Dark Ambient", artist.Genre?.Name);
    }
}
