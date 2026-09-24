// Ported 1:1 from XCTest: keep the original assertion style (count comparisons, force unwraps).
#pragma warning disable xUnit2013, CS8602

using Amperfy.Core.Player;
using Amperfy.Core.Tests.Helper;
using static Amperfy.Core.Tests.Player.TestUtil;

namespace Amperfy.Core.Tests.Storage;

/// Port of AmperfyKitTests/Cases/Storage/ManagedObjects/AlbumTest.swift
public class AlbumTest
{
    private readonly CoreDataHelper cdHelper;
    private readonly LibraryStorage library;
    private readonly Account account;
    private readonly Album testAlbum;
    private const string testId = "23489";

    public AlbumTest()
    {
        cdHelper = new CoreDataHelper();
        library = cdHelper.CreateSeededStorage();
        account = library.GetAccount(TestAccountInfo.Create1());
        testAlbum = library.CreateAlbum(account);
        testAlbum.Id = testId;
    }


    [Fact]
    public void TestCreation()
    {
        var album = library.CreateAlbum(account);
        Assert.Equal(TestAccountInfo.Test1ServerHash, album.Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, album.Account?.UserHash);
        Assert.Equal("", album.Id);
        Assert.Equal("Unknown Album", album.Identifier);
        Assert.Equal("Unknown Album", album.Name);
        Assert.Equal(0, album.Year);
        Assert.Null(album.Artist);
        Assert.Equal(0, album.Songs.Count);
        Assert.Equal(0, album.SongCount);
        Assert.Null(album.Artwork);
        Assert.Null(album.ImagePath(ArtworkDisplayPreference.ServerArtworkOnly));
        Assert.Equal(ArtworkType.Album, album.DefaultArtworkType);
        Assert.False(album.Playables.HasCachedItems());
        Assert.False(album.IsOrphaned);
    }

    [Fact]
    public void TestArtist()
    {
        var artist = NN(library.GetArtist(account, cdHelper.Seeder.Artists[0].Id));
        testAlbum.Artist = artist;
        Assert.Equal(artist.Id, testAlbum.Artist!.Id);
        library.SaveContext();
        var albumFetched = NN(library.GetAlbum(account, testId));
        Assert.Equal(artist.Id, albumFetched.Artist!.Id);
    }

    [Fact]
    public void TestTitle()
    {
        var testTitle = "Alright";
        testAlbum.Name = testTitle;
        Assert.Equal(testTitle, testAlbum.Name);
        Assert.Equal(testTitle, testAlbum.Identifier);
        library.SaveContext();
        var albumFetched = NN(library.GetAlbum(account, testId));
        Assert.Equal(testTitle, albumFetched.Name);
        Assert.Equal(testTitle, albumFetched.Identifier);
    }

    [Fact]
    public void TestYear()
    {
        var testYear = 2001;
        testAlbum.Year = testYear;
        Assert.Equal(testYear, testAlbum.Year);
        library.SaveContext();
        var albumFetched = NN(library.GetAlbum(account, testId));
        Assert.Equal(testYear, albumFetched.Year);
    }

    [Fact]
    public void TestArtworkAndImage()
    {
        var testData = PngBytes;
        var relFilePath = "testArtwork";
        var absFilePath = CacheFileManager.Shared.GetAbsoluteAmperfyPath(relFilePath)!;
        CacheFileManager.Shared.WriteDataIntoCache(testData, relFilePath, account.Info);
        testAlbum.Artwork = library.CreateArtwork(account);
        testAlbum.Artwork?.RelFilePath = relFilePath;
        testAlbum.Artwork?.Status = ImageStatus.CustomImage;
        Assert.NotNull(testAlbum.Artwork?.ImagePath);
        library.SaveContext();
        var albumFetched = NN(library.GetAlbum(account, testId));
        Assert.NotNull(albumFetched.Artwork?.ImagePath);
        CacheFileManager.Shared.RemoveItem(absFilePath, account.Info);
    }

    [Fact]
    public void TestSongs()
    {
        var album3Items = NN(library.GetAlbum(account, cdHelper.Seeder.Albums[0].Id));
        Assert.Equal(3, album3Items.Songs.Count);
        Assert.Equal(3, album3Items.SongCount);
        var album2Items = NN(library.GetAlbum(account, cdHelper.Seeder.Albums[2].Id));
        Assert.Equal(2, album2Items.Songs.Count);
        Assert.Equal(2, album2Items.SongCount);
    }

    [Fact]
    public void TestHasCachedSongs()
    {
        var albumNoCached = NN(library.GetAlbum(account, cdHelper.Seeder.Albums[0].Id));
        Assert.False(albumNoCached.Playables.HasCachedItems());
        var albumTwoCached = NN(library.GetAlbum(account, cdHelper.Seeder.Albums[2].Id));
        Assert.True(albumTwoCached.Playables.HasCachedItems());
    }

    [Fact]
    public void TestIsOrphaned()
    {
        testAlbum.Name = "blub";
        Assert.False(testAlbum.IsOrphaned);
        testAlbum.Name = "Unknown Album";
        Assert.False(testAlbum.IsOrphaned);
        testAlbum.Name = "Orphaned";
        Assert.False(testAlbum.IsOrphaned);
        testAlbum.Name = "Unknown (Orphaned)";
        Assert.True(testAlbum.IsOrphaned);
        testAlbum.Name = "blub";
        Assert.False(testAlbum.IsOrphaned);
    }
}
