using Amperfy.Core.Api.Ampache;

namespace Amperfy.Core.Tests.Api.Ampache;

public class PodcastEpisodesParserTest : AbstractAmpacheTest
{
    private readonly Podcast _testPodcast;

    public PodcastEpisodesParserTest()
    {
        XmlData = GetTestFileData("podcast_episodes");
        _testPodcast = Library.CreatePodcast(Account);
        Library.SaveContext();
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, IdParserDelegate.PrefetchIDs);
        ParserDelegate = new PodcastEpisodeParserDelegate(_testPodcast, prefetch, Account, Library);
    }

    [Fact]
    public void TestCacheParsing()
    {
        var podcast = _testPodcast;
        TestParsing();
        Assert.False(((PlayableParserDelegate)ParserDelegate!).IsCollectionCached);
        foreach (var episode in podcast.Episodes) episode.RelFilePath = "jop";
        TestParsing();
        Assert.True(((PlayableParserDelegate)ParserDelegate!).IsCollectionCached);
        foreach (var episode in podcast.Episodes) episode.RelFilePath = "jop";
        podcast.Episodes[^1].RelFilePath = null;
        TestParsing();
        Assert.False(((PlayableParserDelegate)ParserDelegate!).IsCollectionCached);
    }

    protected override void CheckCorrectParsing()
    {
        ParserDelegate?.PerformPostParseOperations();
        PrefetchIdTester.CheckPrefetchIdCounts(artworkCount: 1, podcastEpisodeCount: 4, podcastCount: 0,
            podcastLibraryCount: 1); // the one create for this test

        var podcast = _testPodcast;
        Assert.Equal(4, podcast.Episodes.Count);

        var episode = podcast.Episodes[0];
        AssertTestAccount1(episode.Account);
        Assert.Equal("44", episode.Id);
        Assert.Equal("COVID, Quickly, Episode < 3: Vaccine Inequality--plus Your Body the Variant Fighter", episode.Title);
        Assert.Equal(5, episode.Rating);
        Assert.Equal("Today we bring you < the third episode in a new podcast series: COVID, Quickly. Every two weeks, Scientific American’s senior health editors Tanya...\n", episode.Depiction);
        Assert.Equal(1616815800, new DateTimeOffset(episode.PublishDate).ToUnixTimeSeconds()); // "3/27/21, 3:30 AM"
        Assert.Null(episode.StreamId);
        Assert.Equal(PodcastEpisodeRemoteStatus.Completed, episode.PodcastStatus);
        AssertTestAccount1(episode.Podcast?.Account);
        Assert.Same(podcast, episode.Podcast);
        Assert.Null(episode.Disk);
        Assert.Equal(0, episode.Track);
        Assert.Equal(325, episode.Duration);
        Assert.Equal(0, episode.Year);
        Assert.Equal(0, episode.Bitrate);
        Assert.Equal("audio/mpeg", episode.ContentType);
        Assert.Equal("https://music.com.au/play/index.php?ssid=cfj3f237d563f479f5223k23189dbb34&type=podcast_episode&oid=44&uid=4&format=raw&player=api&name=60-Second%20Science%20-%20COVID-%20Quickly-%20Episode%203-%20Vaccine%20Inequality-plus%20Your%20Body%20the%20Variant%20Fighter.mp3", episode.Url);
        Assert.Equal(5460000, episode.Size);
        AssertTestAccount1(episode.Artwork?.Account);
        Assert.Equal("podcast", episode.Artwork?.Type);
        Assert.Equal("1", episode.Artwork?.Id);

        episode = podcast.Episodes[2];
        AssertTestAccount1(episode.Account);
        Assert.Equal("46", episode.Id);
        Assert.Equal("Smartphones Can Hear the Shape of Your Door Keys", episode.Title);
        Assert.Equal(0, episode.Rating);
        Assert.Equal("Can you pick a lock with just a smartphone? New research shows that doing so is possible.", episode.Depiction);
        Assert.Equal(1616104800, new DateTimeOffset(episode.PublishDate).ToUnixTimeSeconds()); // "3/18/21, 10:00 PM"
        Assert.Null(episode.StreamId);
        Assert.Equal(PodcastEpisodeRemoteStatus.Downloading, episode.PodcastStatus);
        AssertTestAccount1(episode.Podcast?.Account);
        Assert.Same(podcast, episode.Podcast);
        Assert.Null(episode.Disk);
        Assert.Equal(0, episode.Track);
        Assert.Equal(222, episode.Duration);
        Assert.Equal(0, episode.Year);
        Assert.Equal(0, episode.Bitrate);
        Assert.Equal("", episode.ContentType);
        Assert.Equal("https://music.com.au/play/index.php?ssid=cfj3f237d563f479f5223k23189dbb34&type=podcast_episode&oid=46&uid=4&format=raw&player=api&name=60-Second%20Science%20-%20Smartphones%20Can%20Hear%20the%20Shape%20of%20Your%20Door%20Keys.", episode.Url);
        Assert.Equal(0, episode.Size);
        AssertTestAccount1(episode.Artwork?.Account);
        Assert.Equal("podcast", episode.Artwork?.Type);
        Assert.Equal("1", episode.Artwork?.Id);

        episode = podcast.Episodes[3];
        AssertTestAccount1(episode.Account);
        Assert.Equal("47", episode.Id);
        Assert.Equal("Chimpanzees Show Altruism while Gathering around the Juice Fountain", episode.Title);
        Assert.Equal(1, episode.Rating);
        Assert.Equal("New research tries to tease out whether our closest animal relatives can be selfless.", episode.Depiction);
        Assert.Equal(1615933800, new DateTimeOffset(episode.PublishDate).ToUnixTimeSeconds()); // "3/16/21, 10:30 PM"
        Assert.Null(episode.StreamId);
        Assert.Equal(PodcastEpisodeRemoteStatus.Downloading, episode.PodcastStatus);
        AssertTestAccount1(episode.Podcast?.Account);
        Assert.Same(podcast, episode.Podcast);
        Assert.Null(episode.Disk);
        Assert.Equal(0, episode.Track);
        Assert.Equal(296, episode.Duration);
        Assert.Equal(0, episode.Year);
        Assert.Equal(0, episode.Bitrate);
        Assert.Equal("", episode.ContentType);
        Assert.Equal("https://music.com.au/play/index.php?ssid=cfj3f237d563f479f5223k23189dbb34&type=podcast_episode&oid=47&uid=4&format=raw&player=api&name=60-Second%20Science%20-%20Chimpanzees%20Show%20Altruism%20while%20Gathering%20around%20the%20Juice%20Fountain.", episode.Url);
        Assert.Equal(0, episode.Size);
        AssertTestAccount1(episode.Artwork?.Account);
        Assert.Equal("podcast", episode.Artwork?.Type);
        Assert.Equal("1", episode.Artwork?.Id);

        Library.SaveContext();
        Assert.Equal(4, podcast.EpisodeCount);
    }
}
