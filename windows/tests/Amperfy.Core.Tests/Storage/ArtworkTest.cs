// Ported 1:1 from XCTest: keep the original assertion style (count comparisons, force unwraps).
#pragma warning disable xUnit2013, CS8602

using Amperfy.Core.Player;
using Amperfy.Core.Tests.Helper;
using static Amperfy.Core.Tests.Player.TestUtil;

namespace Amperfy.Core.Tests.Storage;

/// Port of AmperfyKitTests/Cases/Storage/ManagedObjects/ArtworkTest.swift
public class ArtworkTest
{
    private readonly CoreDataHelper cdHelper;
    private readonly LibraryStorage library;
    private readonly Account account;
    private readonly Artwork testArtwork;

    public ArtworkTest()
    {
        cdHelper = new CoreDataHelper();
        library = cdHelper.CreateSeededStorage();
        account = library.GetAccount(TestAccountInfo.Create1());
        testArtwork = library.CreateArtwork(account);
    }


    [Fact]
    public void TestCreation()
    {
        var artwork = library.CreateArtwork(account);
        Assert.Equal(TestAccountInfo.Test1ServerHash, artwork.Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, artwork.Account?.UserHash);
        Assert.Equal(ImageStatus.IsDefaultImage, artwork.Status);
        Assert.Equal("", artwork.Id);
        Assert.Equal("", artwork.Type);
        Assert.Null(artwork.ImagePath);
        Assert.Equal(0, artwork.Owners.Count);
    }

    [Fact]
    public void TestStatus()
    {
        testArtwork.Status = ImageStatus.FetchError;
        Assert.Equal(ImageStatus.FetchError, testArtwork.Status);
        var artist1 = NN(library.GetArtist(account, cdHelper.Seeder.Artists[0].Id));
        artist1.Artwork = testArtwork;
        library.SaveContext();
        var artistFetched = NN(library.GetArtist(account, cdHelper.Seeder.Artists[0].Id));
        Assert.Equal(ImageStatus.FetchError, artistFetched.Artwork?.Status);
    }

    [Fact]
    public void TestImageWithCorrectStatus()
    {
        testArtwork.Status = ImageStatus.CustomImage;
        var testData = Convert.FromBase64String("Test");
        var relFilePath = "testArtwork";
        var absFilePath = CacheFileManager.Shared.GetAbsoluteAmperfyPath(relFilePath)!;
        CacheFileManager.Shared.WriteDataIntoCache(testData, relFilePath, account.Info);
        testArtwork.RelFilePath = relFilePath;
        Assert.Equal(ImageStatus.CustomImage, testArtwork.Status);
        Assert.Equal(absFilePath, testArtwork.ImagePath);
        CacheFileManager.Shared.RemoveItem(absFilePath, account.Info);
    }

    [Fact]
    public void TestImageWithWrongStatus()
    {
        testArtwork.Status = ImageStatus.NotChecked;
        var testData = Convert.FromBase64String("Test");
        var relFilePath = "testArtwork";
        var absFilePath = CacheFileManager.Shared.GetAbsoluteAmperfyPath(relFilePath)!;
        CacheFileManager.Shared.WriteDataIntoCache(testData, relFilePath, account.Info);
        testArtwork.RelFilePath = relFilePath;
        Assert.Equal(ImageStatus.NotChecked, testArtwork.Status);
        Assert.Null(testArtwork.ImagePath);
        CacheFileManager.Shared.RemoveItem(absFilePath, account.Info);
    }

    [Fact]
    public void TestOwners()
    {
        var artist1 = NN(library.GetArtist(account, cdHelper.Seeder.Artists[0].Id));
        var artist2 = NN(library.GetArtist(account, cdHelper.Seeder.Artists[1].Id));
        Assert.Equal(0, testArtwork.Owners.Count);
        // EF fixes up inverse navigations on DetectChanges (CoreData immediately)
        artist1.Artwork = testArtwork;
        library.Context.ChangeTracker.DetectChanges();
        Assert.Equal(1, testArtwork.Owners.Count);
        artist2.Artwork = testArtwork;
        library.Context.ChangeTracker.DetectChanges();
        Assert.Equal(2, testArtwork.Owners.Count);
    }
}
