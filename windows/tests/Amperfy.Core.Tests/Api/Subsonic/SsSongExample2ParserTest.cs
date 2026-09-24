using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsSongExample2ParserTest : AbstractSsParserTest
{
    public SsSongExample2ParserTest()
    {
        XmlData = GetTestFileData("album_example_2");
        CreateTestPartner();
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, SsIdParserDelegate.PrefetchIDs);
        SsParserDelegate = new SsSongParserDelegate(prefetch, Account, Library, parseNotifier: null);
    }

    private void CreateTestPartner()
    {
        var artist = Library.CreateArtist(Account);
        artist.Id = "5432";
        artist.Name = "AC/DC";

        var album = Library.CreateAlbum(Account);
        album.Id = "11053";
        album.Name = "High Voltage";
        Library.SaveContext();
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(
            artworkCount: 2,
            genreNameCount: 1,
            artistCount: 1,
            albumCount: 1,
            songCount: 2,
            artworkFetchCount: 1); // the album cover itself is not created and all songs have the same cover

        var songs = Library.GetSongs(Account).OrderByDescending(s => s.Id, StringComparer.Ordinal).ToList();
        Assert.Equal(2, songs.Count);

        var song = songs[0];
        AssertIsAccount1(song.Account);
        Assert.Equal("71463", song.Id);
        Assert.Equal("The Jack", song.Title);
        AssertIsAccount1(song.Artist?.Account);
        Assert.Equal("5432", song.Artist?.Id);
        Assert.Equal("AC/DC", song.Artist?.Name);
        Assert.Null(song.Album);
        Assert.Null(song.Disk);
        Assert.Equal(SubsonicDateParser.ParseIso8601WithFractionalSeconds("2004-11-08T23:36:11"), song.AddedDate);
        Assert.Equal(0, song.Track);
        Assert.Null(song.Genre);
        Assert.Equal(352, song.Duration);
        Assert.Equal(352, song.RemoteDuration);
        Assert.Equal(0, song.Year);
        Assert.Equal(128000, song.Bitrate);
        Assert.Equal("audio/mpeg", song.ContentType);
        Assert.Null(song.Url);
        Assert.Equal(5624132, song.Size);
        AssertIsAccount1(song.Artwork?.Account);
        Assert.Equal("", song.Artwork?.Type);
        Assert.Equal("71381", song.Artwork?.Id);
        var song1Artwork = song.Artwork;

        song = songs[1];
        AssertIsAccount1(song.Account);
        Assert.Equal("71458", song.Id);
        Assert.Equal("It's A Long Way To The Top", song.Title);
        AssertIsAccount1(song.Artist?.Account);
        Assert.Equal("5432", song.Artist?.Id);
        Assert.Equal("AC/DC", song.Artist?.Name);
        Assert.Null(song.Album);
        Assert.Null(song.Disk);
        Assert.Equal(SubsonicDateParser.ParseIso8601WithFractionalSeconds("2004-11-27T20:23:32"), song.AddedDate);
        Assert.Equal(0, song.Track);
        AssertIsAccount1(song.Genre?.Account);
        Assert.Equal("", song.Genre?.Id);
        Assert.Equal("Rock", song.Genre?.Name);
        Assert.Equal(315, song.Duration);
        Assert.Equal(315, song.RemoteDuration);
        Assert.Equal(1976, song.Year);
        Assert.Equal(128000, song.Bitrate);
        Assert.Equal("audio/mpeg", song.ContentType);
        Assert.Null(song.Url);
        Assert.Equal(5037357, song.Size);
        AssertIsAccount1(song.Artwork?.Account);
        Assert.Equal("", song.Artwork?.Type);
        Assert.Equal("71381", song.Artwork?.Id);
        Assert.Same(song1Artwork, song.Artwork);
    }
}
