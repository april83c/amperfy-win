using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsSongExample1ParserTest : AbstractSsParserTest
{
    public SsSongExample1ParserTest()
    {
        XmlData = GetTestFileData("album_example_1");
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, SsIdParserDelegate.PrefetchIDs);
        SsParserDelegate = new SsSongParserDelegate(prefetch, Account, Library, parseNotifier: null);
    }

    /// ISO8601DateFormatter with [.withInternetDateTime, .withFractionalSeconds]
    private static DateTime? DateFrom(string s) => SubsonicDateParser.ParseIso8601WithFractionalSeconds(s);

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(
            artworkCount: 2,
            genreNameCount: 1,
            artistCount: 1,
            albumCount: 1,
            songCount: 8,
            artworkFetchCount: 1); // the album cover itself is not created and all songs have the same cover

        var songs = Library.GetSongs(Account).OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        Assert.Equal(8, songs.Count);

        var song = songs[6];
        AssertIsAccount1(song.Account);
        Assert.Equal("71463", song.Id);
        Assert.Equal("The Jack", song.Title);
        Assert.Equal(0, song.Rating);
        AssertIsAccount1(song.Artist?.Account);
        Assert.Equal("5432", song.Artist?.Id);
        Assert.Equal("AC/DC", song.Artist?.Name);
        AssertIsAccount1(song.Album?.Account);
        Assert.Equal("11053", song.Album?.Id);
        Assert.Equal("High Voltage", song.Album?.Name);
        Assert.Equal(DateFrom("2004-11-08T23:36:11"), song.AddedDate);
        Assert.Null(song.Disk);
        Assert.Equal(0, song.Track);
        Assert.Null(song.Genre);
        Assert.Equal(352, song.Duration);
        Assert.Equal(352, song.RemoteDuration);
        Assert.Equal(0, song.Year);
        Assert.Equal(0.0f, song.ReplayGainAlbumGain);
        Assert.Equal(0.0f, song.ReplayGainAlbumPeak);
        Assert.Equal(0.0f, song.ReplayGainTrackGain);
        Assert.Equal(0.0f, song.ReplayGainTrackPeak);
        Assert.Equal(128000, song.Bitrate);
        Assert.True(song.IsFavorite);
        Assert.Equal(DateFrom("2024-07-21T20:02:24.995815902Z"), song.StarredDate);
        Assert.NotNull(song.StarredDate);
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
        Assert.Equal(1, song.Rating);
        AssertIsAccount1(song.Artist?.Account);
        Assert.Equal("5432", song.Artist?.Id);
        Assert.Equal("AC/DC", song.Artist?.Name);
        AssertIsAccount1(song.Album?.Account);
        Assert.Equal("11053", song.Album?.Id);
        Assert.Equal("High Voltage", song.Album?.Name);
        Assert.Equal(DateFrom("2004-11-27T20:23:32"), song.AddedDate);
        Assert.Null(song.Disk);
        Assert.Equal(0, song.Track);
        AssertIsAccount1(song.Genre?.Account);
        Assert.Equal("", song.Genre?.Id);
        Assert.Equal("Rock", song.Genre?.Name);
        Assert.Equal(315, song.Duration);
        Assert.Equal(315, song.RemoteDuration);
        Assert.Equal(1976, song.Year);
        //  <replayGain trackGain="0.1" albumGain="2.3" trackPeak="9.1" albumPeak="9" baseGain="4.1">
        Assert.Equal(2.3f, song.ReplayGainAlbumGain);
        Assert.Equal(0.0f, song.ReplayGainAlbumPeak);
        Assert.Equal(0.1f, song.ReplayGainTrackGain);
        Assert.Equal(9.1f, song.ReplayGainTrackPeak);
        Assert.Equal(128000, song.Bitrate);
        Assert.True(song.IsFavorite);
        Assert.Equal(DateFrom("2022-09-12T13:08:58Z"), song.StarredDate);
        Assert.Equal("audio/mpeg", song.ContentType);
        Assert.Null(song.Url);
        Assert.Equal(5037357, song.Size);
        AssertIsAccount1(song.Artwork?.Account);
        Assert.Equal("", song.Artwork?.Type);
        Assert.Equal("71381", song.Artwork?.Id);
        Assert.Same(song1Artwork, song.Artwork);

        song = songs[5];
        AssertIsAccount1(song.Account);
        Assert.Equal("71462", song.Id);
        Assert.Equal(5, song.Rating);
        Assert.Equal("She's Got Balls", song.Title);
        AssertIsAccount1(song.Artist?.Account);
        Assert.Equal("5432", song.Artist?.Id);
        Assert.Equal("AC/DC", song.Artist?.Name);
        AssertIsAccount1(song.Album?.Account);
        Assert.Equal("11053", song.Album?.Id);
        Assert.Equal("High Voltage", song.Album?.Name);
        Assert.Equal(DateFrom("2004-11-27T20:23:34"), song.AddedDate);
        Assert.Null(song.Disk);
        Assert.Equal(8, song.Track);
        AssertIsAccount1(song.Genre?.Account);
        Assert.Equal("", song.Genre?.Id);
        Assert.Equal("Rock", song.Genre?.Name);
        Assert.Equal(290, song.Duration);
        Assert.Equal(290, song.RemoteDuration);
        Assert.Equal(1976, song.Year);
        Assert.Equal(-5f, song.ReplayGainAlbumGain);
        Assert.Equal(7.4f, song.ReplayGainAlbumPeak);
        Assert.Equal(-7.1f, song.ReplayGainTrackGain);
        Assert.Equal(9.1f, song.ReplayGainTrackPeak);
        Assert.Equal(128000, song.Bitrate);
        Assert.False(song.IsFavorite);
        Assert.Null(song.StarredDate);
        Assert.Equal("audio/mpeg", song.ContentType);
        Assert.Null(song.Url);
        Assert.Equal(4651866, song.Size);
        AssertIsAccount1(song.Artwork?.Account);
        Assert.Equal("", song.Artwork?.Type);
        Assert.Equal("71381", song.Artwork?.Id);
        Assert.Same(song1Artwork, song.Artwork);
    }

    [Fact]
    public void TestSimilarSongs2WrapperParsing()
    {
        var similarSongsData = GetTestFileData("similarSongs2_example_1");

        var idParserDelegate = new SsIDsParserDelegate();
        idParserDelegate.Parse(similarSongsData);
        Assert.Null(idParserDelegate.Error);

        var prefetch = Library.GetElements(Account, idParserDelegate.PrefetchIDs);
        var parserDelegate = new SsSongParserDelegate(prefetch, Account, Library, parseNotifier: null);
        parserDelegate.Parse(similarSongsData);
        Assert.Null(parserDelegate.Error);
        Library.SaveContext();

        var songs = Library.GetSongs(Account).OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        Assert.Equal(2, songs.Count);
        Assert.Equal("71458", songs[0].Id);
        Assert.Equal("It's A Long Way To The Top", songs[0].Title);
        Assert.Equal("71463", songs[1].Id);
        Assert.Equal("The Jack", songs[1].Title);
    }
}
