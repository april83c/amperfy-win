using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsAlbumMissingArtistsIdParserTest : AbstractSsParserTest
{
    private const string AlbumId = "101941fb3433f9748a21d087cdccea3c";

    public SsAlbumMissingArtistsIdParserTest()
    {
        XmlData = GetTestFileData("album_missing_artistId");
        CreateTestAlbum();
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, SsIdParserDelegate.PrefetchIDs);
        SsParserDelegate = new SsSongParserDelegate(prefetch, Account, Library, parseNotifier: null);
    }

    private void CreateTestAlbum()
    {
        var album = Library.CreateAlbum(Account);
        album.Id = AlbumId;
        album.Name = "FabricLive 25: High Contrast";
        Library.SaveContext();
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(
            artworkCount: 22,
            genreNameCount: 1,
            artistCount: 1,
            localArtistCount: 20,
            albumCount: 1,
            songCount: 22);

        var album = Library.GetAlbum(Account, AlbumId)!;
        // SongMO.trackNumberSortedFetchRequest for the album
        var songs = album.Songs;
        Assert.Equal(22, songs.Count);

        var song = songs[0];
        Assert.Equal(1, song.Track);
        Assert.Equal("Adam F", song.Artist?.Name);
        Assert.Equal(AlbumId, song.Album?.Id);
        AssertIsAccount1(song.Album?.Account);
        AssertIsAccount1(song.Artist?.Account);
        song = songs[1];
        Assert.Equal(2, song.Track);
        Assert.Equal("London Elektricity", song.Artist?.Name);
        song = songs[2];
        Assert.Equal(3, song.Track);
        Assert.Equal("DJ Marky, Bungle & DJ Roots", song.Artist?.Name);
        song = songs[3];
        Assert.Equal(4, song.Track);
        Assert.Equal("Logistics", song.Artist?.Name);
        song = songs[4];
        Assert.Equal(5, song.Track);
        Assert.Equal("Cyantific & Logistics", song.Artist?.Name);
        AssertIsAccount1(song.Artist?.Account);
        song = songs[5];
        Assert.Equal(6, song.Track);
        Assert.Equal("Funky Technicians", song.Artist?.Name);
        song = songs[6];
        Assert.Equal(7, song.Track);
        Assert.Equal("Martyn", song.Artist?.Name);
        song = songs[21];
        Assert.Equal(22, song.Track);
        Assert.Equal("High Contrast", song.Artist?.Name);
        Assert.Equal(AlbumId, song.Album?.Id);

        var localArtist = Library.GetArtistLocal(Account, "Logistics");
        Assert.NotNull(localArtist);
        Assert.Same(localArtist, songs[3].Artist);
        AssertIsAccount1(song.Artist?.Account);
        Assert.Equal(2, localArtist.Songs.Count);
        // songCount is denormalized -> will be updated at save context
        Library.SaveContext();
        Assert.Equal(2, localArtist.SongCount);
    }
}
