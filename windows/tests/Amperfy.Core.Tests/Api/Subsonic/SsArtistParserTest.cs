using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsArtistParserTest : AbstractSsParserTest
{
    public SsArtistParserTest()
    {
        XmlData = GetTestFileData("artists_example_1");
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, SsIdParserDelegate.PrefetchIDs);
        SsParserDelegate = new SsArtistParserDelegate(prefetch, Account, Library, parseNotifier: null);
    }

    private static void CheckArtist(Artist artist, string id, string name, int rating, int remoteAlbumCount, string artworkId)
    {
        AssertIsAccount1(artist.Account);
        Assert.Equal(id, artist.Id);
        Assert.Equal(name, artist.Name);
        Assert.Equal(rating, artist.Rating);
        Assert.Equal(0, artist.Duration);
        Assert.Equal(0, artist.RemoteDuration);
        Assert.Equal(remoteAlbumCount, artist.RemoteAlbumCount);
        AssertIsAccount1(artist.Artwork?.Account);
        Assert.Equal("", artist.Artwork?.Type);
        Assert.Equal(artworkId, artist.Artwork?.Id);
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(
            artworkCount: 6,
            artistCount: 6);

        var artists = Library.GetArtists(Account).OrderBy(a => IntId(a.Id)).ToList();
        Assert.Equal(6, artists.Count);

        CheckArtist(artists[0], "5421", "ABBA", 3, 6, "ar-5421");
        CheckArtist(artists[1], "5432", "AC/DC", 0, 15, "ar-5432");
        CheckArtist(artists[2], "5449", "A-Ha", 0, 4, "ar-5449");
        CheckArtist(artists[3], "5950", "Bob Marley", 0, 8, "ar-5950");
        CheckArtist(artists[4], "5957", "Bruce Dickinson", 0, 2, "ar-5957");
        CheckArtist(artists[5], "6633", "Aaron Neville", 5, 1, "ar-6633");
    }
}
