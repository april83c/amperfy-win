using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsPodcastParserTest : AbstractSsParserTest
{
    public SsPodcastParserTest()
    {
        XmlData = GetTestFileData("podcasts_example_1");
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, SsIdParserDelegate.PrefetchIDs);
        SsParserDelegate = new SsPodcastParserDelegate(prefetch, Account, Library, parseNotifier: null);
    }

    protected override void CheckCorrectParsing()
    {
        SsParserDelegate?.PerformPostParseOperations();

        PrefetchIdTester.CheckPrefetchIdCounts(
            artworkCount: 4,
            podcastEpisodeCount: 2,
            podcastCount: 3,
            artworkFetchCount: 2, // the podcast episodes cover itself is not created
            podcastEpisodeFetchCount: 0, // no episodes are parsed
            podcastFetchCount: 2); // one podcast has an error

        var podcasts = Library.GetPodcasts(Account).OrderBy(p => IntId(p.Id)).ToList();
        Assert.Equal(2, podcasts.Count);

        var podcast = podcasts[0];
        AssertIsAccount1(podcast.Account);
        Assert.Equal("1", podcast.Id);
        Assert.Equal("Dr Karl and the < Naked Scientist", podcast.Title);
        Assert.Equal(
            "Dr Chris Smith aka The < Naked Scientist with the latest news from the world of science and Dr Karl answers listeners' science questions.",
            podcast.Depiction);
        AssertIsAccount1(podcast.Artwork?.Account);
        Assert.Equal("", podcast.Artwork?.Type);
        Assert.Equal("pod-1", podcast.Artwork?.Id);
        Assert.False(podcast.IsCached);

        podcast = podcasts[1];
        AssertIsAccount1(podcast.Account);
        Assert.Equal("2", podcast.Id);
        Assert.Equal("NRK P1 - Herreavdelingen", podcast.Title);
        Assert.Equal(
            "Et program der herrene Yan Friis og Finn Bjelke møtes og musikk nytes.",
            podcast.Depiction);
        AssertIsAccount1(podcast.Artwork?.Account);
        Assert.Equal("", podcast.Artwork?.Type);
        Assert.Equal("pod-2", podcast.Artwork?.Id);
        Assert.False(podcast.IsCached);
    }
}
