// Ported 1:1 from XCTest: keep the original assertion style (count comparisons, force unwraps).
#pragma warning disable xUnit2013, CS8602

using Amperfy.Core.Player;
using Amperfy.Core.Tests.Helper;
using static Amperfy.Core.Tests.Player.TestUtil;

namespace Amperfy.Core.Tests.Storage;

/// Port of AmperfyKitTests/Cases/Storage/ManagedObjects/PodcastEpisodeTest.swift
public class PodcastEpisodeTest
{
    private readonly CoreDataHelper cdHelper;
    private readonly LibraryStorage library;
    private readonly Account account;
    private readonly PodcastEpisode testEpisode;
    private const string testId = "2345";

    public PodcastEpisodeTest()
    {
        cdHelper = new CoreDataHelper();
        library = cdHelper.CreateSeededStorage();
        account = library.GetAccount(TestAccountInfo.Create1());
        testEpisode = library.CreatePodcastEpisode(account);
        testEpisode.Id = testId;
    }


    [Fact]
    public void TestCreation()
    {
        var episode = library.CreatePodcastEpisode(account);
        Assert.Equal(TestAccountInfo.Test1ServerHash, episode.Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, episode.Account?.UserHash);
        Assert.Equal("", episode.Id);
        Assert.Null(episode.Artwork);
        Assert.Equal("Unknown Title", episode.Title);
        Assert.Equal(0, episode.Track);
        Assert.Null(episode.Url);
        Assert.Null(episode.Podcast);
        Assert.Equal("Unknown Podcast - Unknown Title", episode.DisplayString);
        Assert.Null(episode.ImagePath(ArtworkDisplayPreference.ServerArtworkOnly));
        Assert.Equal(ArtworkType.PodcastEpisode, episode.DefaultArtworkType);
        Assert.False(episode.IsCached);
        Assert.Equal(0.0, episode.ReplayGainAlbumGain);
        Assert.Equal(0.0, episode.ReplayGainAlbumPeak);
        Assert.Equal(0.0, episode.ReplayGainTrackGain);
        Assert.Equal(0.0, episode.ReplayGainTrackPeak);
    }

    [Fact]
    public void TestPodcast()
    {
        var podcast = library.CreatePodcast(account);
        Assert.Equal(TestAccountInfo.Test1ServerHash, podcast.Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, podcast.Account?.UserHash);
        podcast.Id = "1234";
        testEpisode.Podcast = podcast;
        Assert.Equal("1234", podcast.Id);
        Assert.Equal(podcast.Id, testEpisode.Podcast!.Id);
        library.SaveContext();
        var episodeFetched = NN(library.GetPodcastEpisode(account, testId));
        Assert.Equal(podcast.Id, episodeFetched.Podcast!.Id);
    }

    [Fact]
    public void TestTitle()
    {
        var testTitle = "Alright";
        testEpisode.Title = testTitle;
        Assert.Equal(testTitle, testEpisode.Title);
        Assert.Equal("Unknown Podcast - " + testTitle, testEpisode.DisplayString);
        library.SaveContext();
        var episodeFetched = NN(library.GetPodcastEpisode(account, testId));
        Assert.Equal(testTitle, episodeFetched.Title);
        Assert.Equal("Unknown Podcast - " + testTitle, episodeFetched.DisplayString);
    }

    [Fact]
    public void TestUrl()
    {
        var testUrl = "www.Blub.De";
        testEpisode.Url = testUrl;
        Assert.Equal(testUrl, testEpisode.Url);
        library.SaveContext();
        var episodeFetched = NN(library.GetPodcastEpisode(account, testId));
        Assert.Equal(testUrl, episodeFetched.Url);
    }

    [Fact]
    public void TestArtworkAndImage()
    {
        var testData = PngBytes;
        var relFilePath = "testArtwork";
        var absFilePath = CacheFileManager.Shared.GetAbsoluteAmperfyPath(relFilePath)!;
        CacheFileManager.Shared.WriteDataIntoCache(testData, relFilePath, account.Info);
        testEpisode.Artwork = library.CreateArtwork(account);
        Assert.Equal(TestAccountInfo.Test1ServerHash, testEpisode.Artwork?.Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, testEpisode.Artwork?.Account?.UserHash);
        testEpisode.Artwork?.RelFilePath = relFilePath;
        testEpisode.Artwork?.Status = ImageStatus.CustomImage;
        Assert.NotNull(testEpisode.Artwork?.ImagePath);
        Assert.Equal(absFilePath, testEpisode.ImagePath(ArtworkDisplayPreference.ServerArtworkOnly));
        library.SaveContext();
        var episodeFetched = NN(library.GetPodcastEpisode(account, testId));
        Assert.Equal(absFilePath, episodeFetched.ImagePath(ArtworkDisplayPreference.ServerArtworkOnly));
        CacheFileManager.Shared.RemoveItem(absFilePath, account.Info);
    }

    [Fact]
    public void TestEpisodeDeleteCache()
    {
        var podcast = library.CreatePodcast(account);
        Assert.Equal(TestAccountInfo.Test1ServerHash, podcast.Account?.ServerHash);
        Assert.Equal(TestAccountInfo.Test1UserHash, podcast.Account?.UserHash);
        podcast.Id = "1234";
        testEpisode.Podcast = podcast;

        var playlist1 = NN(library.GetPlaylist(account, cdHelper.Seeder.Playlists[0].Id));
        playlist1.Append(testEpisode);
        var playlist2 = NN(library.GetPlaylist(account, cdHelper.Seeder.Playlists[1].Id));
        playlist2.Append(testEpisode);

        testEpisode.RelFilePath = "blub";
        podcast.IsCached = true;
        playlist1.IsCached = true;
        playlist2.IsCached = true;
        library.SaveContext();

        Assert.True(testEpisode.IsCached);
        Assert.True(podcast.IsCached);
        Assert.True(playlist1.IsCached);
        Assert.True(playlist2.IsCached);
        library.DeleteCache(testEpisode);
        Assert.False(testEpisode.IsCached);
        Assert.False(podcast.IsCached);
        Assert.False(playlist1.IsCached);
        Assert.False(playlist2.IsCached);
    }
}
