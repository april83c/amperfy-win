// Ported 1:1 from XCTest: keep the original assertion style (count comparisons, force unwraps).
#pragma warning disable xUnit2013, CS8602

using Amperfy.Core.Player;
using Amperfy.Core.Tests.Helper;
using static Amperfy.Core.Tests.Player.TestUtil;

namespace Amperfy.Core.Tests.Storage;

/// Port of AmperfyKitTests/Cases/Storage/ManagedObjects/ArtistTest.swift
public class ArtistTest
{
    private readonly CoreDataHelper cdHelper;
    private readonly LibraryStorage library;
    private readonly Account account;
    private readonly Artist testArtist;
    private const string testId = "10089";

    public ArtistTest()
    {
        cdHelper = new CoreDataHelper();
        library = cdHelper.CreateSeededStorage();
        account = library.GetAccount(TestAccountInfo.Create1());
        testArtist = library.CreateArtist(account);
        testArtist.Id = testId;
    }


    [Fact]
    public void TestCreation()
    {
        var artist = library.CreateArtist(account);
        Assert.Equal(TestAccountInfo.Test1ServerHash, artist.Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, artist.Account?.UserHash);
        Assert.Equal("", artist.Id);
        Assert.Equal("Unknown Artist", artist.Identifier);
        Assert.Equal("Unknown Artist", artist.Name);
        Assert.Equal(0, artist.Songs.Count);
        Assert.Equal(0, artist.SongCount);
        Assert.False(artist.Playables.HasCachedItems());
        Assert.Equal(0, artist.Albums.Count);
        Assert.Equal(0, artist.AlbumCount);
        Assert.Null(artist.Artwork);
        Assert.Null(artist.ImagePath(ArtworkDisplayPreference.ServerArtworkOnly));
        Assert.Equal(ArtworkType.Artist, artist.DefaultArtworkType);
    }

    [Fact]
    public void TestName()
    {
        var testTitle = "Alright";
        testArtist.Name = testTitle;
        Assert.Equal(testTitle, testArtist.Name);
        Assert.Equal(testTitle, testArtist.Identifier);
        library.SaveContext();
        var artistFetched = NN(library.GetArtist(account, testId));
        Assert.Equal(testTitle, artistFetched.Name);
        Assert.Equal(testTitle, artistFetched.Identifier);
    }

    [Fact]
    public void TestSongs()
    {
        var artist3Items = NN(library.GetArtist(account, cdHelper.Seeder.Artists[0].Id));
        Assert.Equal(3, artist3Items.Songs.Count);
        Assert.Equal(3, artist3Items.SongCount);
        var artist2Items = NN(library.GetArtist(account, cdHelper.Seeder.Artists[1].Id));
        Assert.Equal(2, artist2Items.Songs.Count);
        Assert.Equal(2, artist2Items.SongCount);
    }

    [Fact]
    public void TestHasCachedSongs()
    {
        var artistNoCached = NN(library.GetArtist(account, cdHelper.Seeder.Artists[0].Id));
        Assert.False(artistNoCached.Playables.HasCachedItems());
        var artistTwoCached = NN(library.GetArtist(account, cdHelper.Seeder.Artists[2].Id));
        Assert.True(artistTwoCached.Playables.HasCachedItems());
    }

    [Fact]
    public void TestAlbums()
    {
        var artist1Items = NN(library.GetArtist(account, cdHelper.Seeder.Artists[0].Id));
        Assert.Equal(1, artist1Items.Albums.Count);
        Assert.Equal(1, artist1Items.AlbumCount);
        var artist2Items = NN(library.GetArtist(account, cdHelper.Seeder.Artists[2].Id));
        Assert.Equal(2, artist2Items.Albums.Count);
        Assert.Equal(2, artist2Items.AlbumCount);
    }

    [Fact]
    public void TestArtworkAndImage()
    {
        var testData = PngBytes;
        var relFilePath = "testArtwork";
        var absFilePath = CacheFileManager.Shared.GetAbsoluteAmperfyPath(relFilePath)!;
        CacheFileManager.Shared.WriteDataIntoCache(testData, relFilePath, account.Info);
        testArtist.Artwork = library.CreateArtwork(account);
        testArtist.Artwork?.RelFilePath = relFilePath;
        testArtist.Artwork?.Status = ImageStatus.CustomImage;
        Assert.Equal(TestAccountInfo.Test1ServerHash, testArtist.Artwork?.Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, testArtist.Artwork?.Account?.UserHash);
        Assert.NotNull(testArtist.Artwork?.ImagePath);
        Assert.Equal(absFilePath, testArtist.ImagePath(ArtworkDisplayPreference.ServerArtworkOnly));
        library.SaveContext();
        var artistFetched = NN(library.GetArtist(account, testId));
        Assert.NotNull(artistFetched.Artwork?.ImagePath);
        Assert.Equal(absFilePath, artistFetched.ImagePath(ArtworkDisplayPreference.ServerArtworkOnly));
        CacheFileManager.Shared.RemoveItem(absFilePath, account.Info);
    }
}
