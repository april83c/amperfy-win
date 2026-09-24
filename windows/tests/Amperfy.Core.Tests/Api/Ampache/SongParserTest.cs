using Amperfy.Core.Api.Ampache;

namespace Amperfy.Core.Tests.Api.Ampache;

public class SongParserTest : AbstractAmpacheTest
{
    public SongParserTest()
    {
        XmlData = GetTestFileData("songs");
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, IdParserDelegate.PrefetchIDs);
        ParserDelegate = new SongParserDelegate(prefetch, Account, Library, parseNotifier: null);
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(artworkCount: 3, genreIdCount: 4, artistCount: 4, albumCount: 2, songCount: 4);

        var songs = Library.GetSongs(Account);
        Assert.Equal(4, songs.Count);
        Assert.Equal(4, Library.GetGenreCount(Account));

        var song = songs[0];
        AssertTestAccount1(song.Account);
        Assert.Equal("115", song.Id);
        Assert.Equal("Are we going Crazy", song.Title);
        AssertTestAccount1(song.Artist?.Account);
        Assert.Equal("27", song.Artist?.Id);
        Assert.Equal("Chi.Otic", song.Artist?.Name);
        AssertTestAccount1(song.Album?.Account);
        Assert.Equal("12", song.Album?.Id); // Album not pre created
        Assert.Equal("Buried in Nausea", song.Album?.Name);
        Assert.Equal("1", song.Disk);
        Assert.Equal(7, song.Track);
        Assert.Null(song.Genre);
        Assert.Equal(433, song.Duration);
        Assert.Equal(433, song.RemoteDuration);
        Assert.Equal(2012, song.Year);
        Assert.Equal(0.0f, song.ReplayGainAlbumGain);
        Assert.Equal(0.0f, song.ReplayGainAlbumPeak);
        Assert.Equal(0.0f, song.ReplayGainTrackGain);
        Assert.Equal(0.0f, song.ReplayGainTrackPeak);
        Assert.Equal(32582, song.Bitrate);
        Assert.Equal("audio/x-ms-wma", song.ContentType);
        Assert.Equal("https://music.com.au/play/index.php?ssid=cfj3f237d563f479f5223k23189dbb34&type=song&oid=115&uid=4&player=api&name=Chi.Otic%20-%20Are%20we%20going%20Crazy.wma", song.Url);
        Assert.Equal(1776580, song.Size);
        AssertTestAccount1(song.Artwork?.Account);
        Assert.Equal("album", song.Artwork?.Type);
        Assert.Equal("12", song.Artwork?.Id);
        var song1Artwork = song.Artwork;

        song = songs[1];
        AssertTestAccount1(song.Account);
        Assert.Equal("107", song.Id);
        Assert.Equal("Arrest Me", song.Title);
        Assert.Equal(2, song.Rating);
        AssertTestAccount1(song.Artist?.Account);
        Assert.Equal("20", song.Artist?.Id);
        Assert.Equal("R/B", song.Artist?.Name);
        AssertTestAccount1(song.Album?.Account);
        Assert.Equal("12", song.Album?.Id);
        Assert.Equal("Buried in Nausea", song.Album?.Name);
        Assert.Null(song.AddedDate);
        Assert.Equal("1", song.Disk);
        Assert.Equal(9, song.Track);
        Assert.Equal("7", song.Genre?.Id);
        Assert.Equal("Punk", song.Genre?.Name);
        Assert.Equal(96, song.Duration);
        Assert.Equal(96, song.RemoteDuration);
        Assert.Equal(2012, song.Year);
        Assert.Equal(0.0f, song.ReplayGainAlbumGain);
        Assert.Equal(0.0f, song.ReplayGainAlbumPeak);
        Assert.Equal(-1.35f, song.ReplayGainTrackGain);
        Assert.Equal(0.881f, song.ReplayGainTrackPeak);
        Assert.Equal(252864, song.Bitrate);
        Assert.Equal("audio/mp4", song.ContentType);
        Assert.Equal("https://music.com.au/play/index.php?ssid=cfj3f237d563f479f5223k23189dbb34&type=song&oid=107&uid=4&player=api&name=R-B%20-%20Arrest%20Me.m4a", song.Url);
        Assert.Equal(3091727, song.Size);
        AssertTestAccount1(song.Artwork?.Account);
        Assert.Equal("album", song.Artwork?.Type);
        Assert.Equal("12", song.Artwork?.Id);
        Assert.Same(song1Artwork, song.Artwork);

        song = songs[2];
        AssertTestAccount1(song.Account);
        Assert.Equal("85", song.Id);
        Assert.Equal("Beq Ultra Fat", song.Title);
        Assert.Equal(1, song.Rating);
        AssertTestAccount1(song.Artist?.Account);
        Assert.Equal("14", song.Artist?.Id);
        Assert.Equal("Nofi/found.", song.Artist?.Name);
        Assert.Null(song.Album);
        Assert.Null(song.AddedDate);
        Assert.Equal("1", song.Disk);
        Assert.Equal(4, song.Track);
        Assert.Equal("6", song.Genre?.Id);
        Assert.Equal("Dance", song.Genre?.Name);
        Assert.Equal(413, song.Duration);
        Assert.Equal(413, song.RemoteDuration);
        Assert.Equal(0, song.Year);
        Assert.Equal(-1.334f, song.ReplayGainAlbumGain);
        Assert.Equal(7.91f, song.ReplayGainAlbumPeak);
        Assert.Equal(0.94f, song.ReplayGainTrackGain);
        Assert.Equal(0.989f, song.ReplayGainTrackPeak);
        Assert.Equal(192000, song.Bitrate);
        Assert.Equal("audio/mpeg", song.ContentType);
        Assert.Equal("https://music.com.au/play/index.php?ssid=cfj3f237d563f479f5223k23189dbb34&type=song&oid=85&uid=4&player=api&name=Nofi-found.%20-%20Beq%20Ultra%20Fat.mp3", song.Url);
        Assert.Equal(9935896, song.Size);
        AssertTestAccount1(song.Artwork?.Account);
        Assert.Equal("album", song.Artwork?.Type);
        Assert.Equal("8", song.Artwork?.Id);

        song = songs[3];
        AssertTestAccount1(song.Account);
        Assert.Equal("56", song.Id);
        Assert.Equal("Black&BlueSmoke", song.Title);
        Assert.Equal(0, song.Rating);
        AssertTestAccount1(song.Artist?.Account);
        Assert.Equal("2", song.Artist?.Id); // Artist not pre created
        Assert.Equal("Synthetic", song.Artist?.Name);
        AssertTestAccount1(song.Album?.Account);
        Assert.Equal("2", song.Album?.Id);
        Assert.Equal("Colorsmoke EP", song.Album?.Name);
        Assert.Null(song.AddedDate);
        Assert.Equal("1", song.Disk);
        Assert.Equal(1, song.Track);
        AssertTestAccount1(song.Genre?.Account);
        Assert.Equal("1", song.Genre?.Id);
        Assert.Equal("Electronic", song.Genre?.Name);
        Assert.Equal(500, song.Duration);
        Assert.Equal(500, song.RemoteDuration);
        Assert.Equal(2007, song.Year);
        Assert.Equal(9.334f, song.ReplayGainAlbumGain);
        Assert.Equal(0.0f, song.ReplayGainAlbumPeak);
        Assert.Equal(-5.11f, song.ReplayGainTrackGain);
        Assert.Equal(1.0f, song.ReplayGainTrackPeak);
        Assert.Equal(64000, song.Bitrate);
        Assert.Equal("audio/mpeg", song.ContentType);
        Assert.Equal("https://music.com.au/play/index.php?ssid=cfj3f237d563f479f5223k23189dbb34&type=song&oid=56&uid=4&player=api&name=Synthetic%20-%20Black-BlueSmoke.mp3", song.Url);
        Assert.Equal(4010069, song.Size);
        AssertTestAccount1(song.Artwork?.Account);
        Assert.Equal("album", song.Artwork?.Type);
        Assert.Equal("2", song.Artwork?.Id);
    }
}
