using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsAlbumMultidiscExample1ParserTest : AbstractSsParserTest
{
    private const string AlbumId = "e209ff7a279e487ea2f37a4a3e7ed563";

    public SsAlbumMultidiscExample1ParserTest()
    {
        XmlData = GetTestFileData("album_multidisc_example_1");
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
        album.Name = "The Analog Botany Collection";
        Library.SaveContext();
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(
            artworkCount: 27,
            artistCount: 1,
            albumCount: 1,
            songCount: 27);

        var album = Library.GetAlbum(Account, AlbumId)!;
        // SongMO.trackNumberSortedFetchRequest for the album
        var songs = album.Songs;
        Assert.Equal(27, songs.Count);

        var song = songs[0];
        Assert.Equal(2, song.Track);
        Assert.Equal("1", song.Disk);
        song = songs[1];
        Assert.Equal(3, song.Track);
        Assert.Equal("1", song.Disk);
        song = songs[2];
        Assert.Equal(4, song.Track);
        Assert.Equal("1", song.Disk);
        song = songs[3];
        Assert.Equal(1, song.Track);
        Assert.Equal("2", song.Disk);
        song = songs[4];
        Assert.Equal(3, song.Track);
        Assert.Equal("2", song.Disk);
        song = songs[5];
        Assert.Equal(5, song.Track);
        Assert.Equal("2", song.Disk);
        song = songs[6];
        Assert.Equal(2, song.Track);
        Assert.Equal("3", song.Disk);
    }
}
