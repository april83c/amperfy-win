using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsPodcastEpisodesParserTest : AbstractSsParserTest
{
    private readonly Podcast _testPodcast;

    public SsPodcastEpisodesParserTest()
    {
        XmlData = GetTestFileData("podcast_example_1");
        _testPodcast = Library.CreatePodcast(Account);
        _testPodcast.Id = "1";
        Library.SaveContext();
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, SsIdParserDelegate.PrefetchIDs);
        SsParserDelegate = new SsPodcastEpisodeParserDelegate(_testPodcast, prefetch, Account, Library);
    }

    [Fact]
    public void TestCacheParsing()
    {
        var podcast = _testPodcast;

        TestParsing();
        Assert.False(((SsPlayableParserDelegate)SsParserDelegate!).IsCollectionCached);

        // mark all songs cached
        foreach (var episode in podcast.Episodes) episode.RelFilePath = "jop";
        TestParsing();
        Assert.True(((SsPlayableParserDelegate)SsParserDelegate!).IsCollectionCached);

        // mark all songs cached execept the last one
        foreach (var episode in podcast.Episodes) episode.RelFilePath = "jop";
        podcast.Episodes.Last().RelFilePath = null;
        TestParsing();
        Assert.False(((SsPlayableParserDelegate)SsParserDelegate!).IsCollectionCached);
    }

    private static long UnixSeconds(DateTime date) =>
        new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)).ToUnixTimeSeconds();

    protected override void CheckCorrectParsing()
    {
        SsParserDelegate?.PerformPostParseOperations();
        var podcast = _testPodcast;
        Assert.Equal(2, podcast.Episodes.Count);

        PrefetchIdTester.CheckPrefetchIdCounts(
            artworkCount: 3,
            podcastEpisodeCount: 2,
            podcastCount: 1,
            artworkFetchCount: 2, // the podcast cover itself is not created
            podcastLibraryCount: 1); // the podcast for this test created in setup

        // episodes are sorted by publish date
        var episode = podcast.Episodes[1];
        AssertIsAccount1(episode.Account);
        Assert.Equal("34", episode.Id);
        Assert.Equal("Scorpions have re-evolved < eyes", episode.Title);
        Assert.Equal(
            "This week < Dr Chris fills us in on the UK's largest free science festival, plus all this week's big scientific discoveries.",
            episode.Depiction);
        Assert.Equal(1296744403, UnixSeconds(episode.PublishDate)); // "2011-02-03T14:46:43"
        Assert.Equal("523", episode.StreamId);
        Assert.Equal(PodcastEpisodeRemoteStatus.Completed, episode.PodcastStatus);
        AssertIsAccount1(episode.Podcast?.Account);
        Assert.Same(podcast, episode.Podcast);
        Assert.Null(episode.Disk);
        Assert.Equal(0, episode.Track);
        Assert.Equal(3146, episode.Duration);
        Assert.Equal(2011, episode.Year);
        Assert.Equal(128000, episode.Bitrate);
        Assert.Equal("audio/mpeg", episode.ContentType);
        Assert.Null(episode.Url);
        Assert.Equal(78421341, episode.Size);
        AssertIsAccount1(episode.Artwork?.Account);
        Assert.Equal("", episode.Artwork?.Type);
        Assert.Equal("24", episode.Artwork?.Id);

        episode = podcast.Episodes[0];
        AssertIsAccount1(episode.Account);
        Assert.Equal("35", episode.Id);
        Assert.Equal("Scar tissue and snake venom treatment", episode.Title);
        Assert.Equal(
            "This week Dr Karl tells the gruesome tale of a surgeon who operated on himself.",
            episode.Depiction);
        Assert.Equal(1315068472, UnixSeconds(episode.PublishDate)); // "2011-09-03T16:47:52"
        Assert.Equal("524", episode.StreamId);
        Assert.Equal(PodcastEpisodeRemoteStatus.Completed, episode.PodcastStatus);
        AssertIsAccount1(episode.Podcast?.Account);
        Assert.Same(podcast, episode.Podcast);
        Assert.Null(episode.Disk);
        Assert.Equal(0, episode.Track);
        Assert.Equal(3099, episode.Duration);
        Assert.Equal(2011, episode.Year);
        Assert.Equal(128000, episode.Bitrate);
        Assert.Equal("audio/mpeg", episode.ContentType);
        Assert.Null(episode.Url);
        Assert.Equal(45624671, episode.Size);
        AssertIsAccount1(episode.Artwork?.Account);
        Assert.Equal("", episode.Artwork?.Type);
        Assert.Equal("27", episode.Artwork?.Id);

        // denormalized value -> will be updated at save context
        Library.SaveContext();
        Assert.Equal(2, podcast.EpisodeCount);
    }
}
