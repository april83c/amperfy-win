// Ported 1:1 from XCTest: keep the original assertion style (count comparisons, force unwraps).
#pragma warning disable xUnit2013, CS8602

using Amperfy.Core.Player;
using Amperfy.Core.Tests.Helper;
using static Amperfy.Core.Tests.Player.TestUtil;

namespace Amperfy.Core.Tests.Storage;

/// Port of AmperfyKitTests/Cases/Storage/ManagedObjects/SongTest.swift
public class SongTest
{
    private readonly CoreDataHelper cdHelper;
    private readonly LibraryStorage library;
    private readonly Account account;
    private readonly Song testSong;
    private const string testId = "2345";

    public SongTest()
    {
        cdHelper = new CoreDataHelper();
        library = cdHelper.CreateSeededStorage();
        account = library.GetAccount(TestAccountInfo.Create1());
        testSong = library.CreateSong(account);
        testSong.Id = testId;
    }


    [Fact]
    public void TestCreation()
    {
        var song = library.CreateSong(account);
        Assert.Equal(TestAccountInfo.Test1ServerHash, song.Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, song.Account?.UserHash);
        Assert.Equal("", song.Id);
        Assert.Null(song.Artwork);
        Assert.Equal("Unknown Title", song.Title);
        Assert.Equal(0, song.Track);
        Assert.Null(song.Url);
        Assert.Null(song.Album);
        Assert.Null(song.Artist);
        Assert.Null(song.AddedDate);
        Assert.Equal("Unknown Artist - Unknown Title", song.DisplayString);
        Assert.Equal("Unknown Title", song.Identifier);
        Assert.Null(song.ImagePath(ArtworkDisplayPreference.ServerArtworkOnly));
        Assert.Equal(ArtworkType.Song, song.DefaultArtworkType);
        Assert.False(song.IsCached);
        Assert.Equal(0.0, song.ReplayGainAlbumGain);
        Assert.Equal(0.0, song.ReplayGainAlbumPeak);
        Assert.Equal(0.0, song.ReplayGainTrackGain);
        Assert.Equal(0.0, song.ReplayGainTrackPeak);
    }

    [Fact]
    public void TestArtist()
    {
        var artist = NN(library.GetArtist(account, cdHelper.Seeder.Artists[0].Id));
        testSong.Artist = artist;
        Assert.Equal(artist.Id, testSong.Artist!.Id);
        library.SaveContext();
        var songFetched = NN(library.GetSong(account, testId));
        Assert.Equal(artist.Id, songFetched.Artist!.Id);
    }

    [Fact]
    public void TestAlbum()
    {
        var album = NN(library.GetAlbum(account, cdHelper.Seeder.Albums[0].Id));
        testSong.Album = album;
        Assert.Equal(album.Id, testSong.Album!.Id);
        library.SaveContext();
        var songFetched = NN(library.GetSong(account, testId));
        Assert.Equal(album.Id, songFetched.Album!.Id);
    }

    [Fact]
    public void TestTitle()
    {
        var testTitle = "Alright";
        testSong.Title = testTitle;
        Assert.Equal(testTitle, testSong.Title);
        Assert.Equal("Unknown Artist - " + testTitle, testSong.DisplayString);
        Assert.Equal(testTitle, testSong.Identifier);
        library.SaveContext();
        var songFetched = NN(library.GetSong(account, testId));
        Assert.Equal(testTitle, songFetched.Title);
        Assert.Equal("Unknown Artist - " + testTitle, songFetched.DisplayString);
        Assert.Equal(testTitle, songFetched.Identifier);
    }

    [Fact]
    public void TestTrack()
    {
        var testTrack = 13;
        testSong.Track = testTrack;
        Assert.Equal(testTrack, testSong.Track);
        library.SaveContext();
        var songFetched = NN(library.GetSong(account, testId));
        Assert.Equal(testTrack, songFetched.Track);
    }

    [Fact]
    public void TestUrl()
    {
        var testUrl = "www.Blub.De";
        testSong.Url = testUrl;
        Assert.Equal(testUrl, testSong.Url);
        library.SaveContext();
        var songFetched = NN(library.GetSong(account, testId));
        Assert.Equal(testUrl, songFetched.Url);
    }

    [Fact]
    public void TestArtworkAndImage()
    {
        var testData = PngBytes;
        var relFilePath = "testArtwork";
        var absFilePath = CacheFileManager.Shared.GetAbsoluteAmperfyPath(relFilePath)!;
        CacheFileManager.Shared.WriteDataIntoCache(testData, relFilePath, account.Info);
        testSong.Artwork = library.CreateArtwork(account);
        Assert.Equal(TestAccountInfo.Test1ServerHash, testSong.Artwork?.Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, testSong.Artwork?.Account?.UserHash);
        testSong.Artwork?.RelFilePath = relFilePath;
        testSong.Artwork?.Status = ImageStatus.CustomImage;
        Assert.NotNull(testSong.Artwork?.ImagePath);
        Assert.Equal(absFilePath, testSong.ImagePath(ArtworkDisplayPreference.ServerArtworkOnly));
        library.SaveContext();
        var songFetched = NN(library.GetSong(account, testId));
        Assert.NotNull(songFetched.Artwork?.ImagePath);
        Assert.Equal(absFilePath, songFetched.ImagePath(ArtworkDisplayPreference.ServerArtworkOnly));
        CacheFileManager.Shared.RemoveItem(absFilePath, account.Info);
    }

    [Fact]
    public void TestRating()
    {
        testSong.Rating = -1;
        Assert.Equal(0, testSong.Rating);
        testSong.Rating = 5;
        Assert.Equal(5, testSong.Rating);
        testSong.Rating = 1;
        Assert.Equal(1, testSong.Rating);
        testSong.Rating = 6;
        Assert.Equal(1, testSong.Rating);
        testSong.Rating = 0;
        Assert.Equal(0, testSong.Rating);
        testSong.Rating = 2;
        Assert.Equal(2, testSong.Rating);
        testSong.Rating = -500;
        Assert.Equal(2, testSong.Rating);
        testSong.Rating = 500;
        Assert.Equal(2, testSong.Rating);
    }

    [Fact]
    public void TestSongDeleteCache()
    {
        var artist = NN(library.GetArtist(account, cdHelper.Seeder.Artists[0].Id));
        testSong.Artist = artist;
        var album = NN(library.GetAlbum(account, cdHelper.Seeder.Albums[0].Id));
        testSong.Album = album;
        var directory = library.CreateDirectory(account);
        Assert.Equal(TestAccountInfo.Test1ServerHash, directory.Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, directory.Account?.UserHash);
        testSong.Directory = directory;
        var musicFolder = library.CreateMusicFolder(account);
        Assert.Equal(TestAccountInfo.Test1ServerHash, musicFolder.Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, musicFolder.Account?.UserHash);
        testSong.MusicFolder = musicFolder;

        var playlist1 = NN(library.GetPlaylist(account, cdHelper.Seeder.Playlists[0].Id));
        playlist1.Append(testSong);
        var playlist2 = NN(library.GetPlaylist(account, cdHelper.Seeder.Playlists[1].Id));
        playlist2.Append(testSong);

        testSong.RelFilePath = "blub";
        album.IsCached = true;
        directory.IsCached = true;
        musicFolder.IsCached = true;
        playlist1.IsCached = true;
        playlist2.IsCached = true;
        library.SaveContext();

        Assert.True(testSong.IsCached);
        Assert.True(album.IsCached);
        Assert.True(directory.IsCached);
        Assert.True(musicFolder.IsCached);
        Assert.True(playlist1.IsCached);
        Assert.True(playlist2.IsCached);
        library.DeleteCache(testSong);
        Assert.False(testSong.IsCached);
        Assert.False(album.IsCached);
        Assert.False(directory.IsCached);
        Assert.False(musicFolder.IsCached);
        Assert.False(playlist1.IsCached);
        Assert.False(playlist2.IsCached);
    }
}
