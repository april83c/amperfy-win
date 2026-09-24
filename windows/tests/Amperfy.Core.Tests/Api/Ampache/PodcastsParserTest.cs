using Amperfy.Core.Api.Ampache;

namespace Amperfy.Core.Tests.Api.Ampache;

public class PodcastsParserTest : AbstractAmpacheTest
{
    public PodcastsParserTest()
    {
        XmlData = GetTestFileData("podcasts");
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, IdParserDelegate.PrefetchIDs);
        ParserDelegate = new PodcastParserDelegate(prefetch, Account, Library, parseNotifier: null);
    }

    protected override void CheckCorrectParsing()
    {
        ParserDelegate?.PerformPostParseOperations();
        Library.SaveContext();
        PrefetchIdTester.CheckPrefetchIdCounts(artworkCount: 3, podcastCount: 3);

        var podcasts = Library.GetPodcasts(Account);
        Assert.Equal(3, podcasts.Count);

        var podcast = podcasts[0];
        AssertTestAccount1(podcast.Account);
        Assert.Equal("1", podcast.Id);
        Assert.Equal("60-Second < Science", podcast.Title);
        Assert.Equal(2, podcast.Rating);
        Assert.False(podcast.IsCached);
        Assert.Equal("Tune in every < weekday for quick reports and commentaries on the world of science—it'll just take a minute", podcast.Depiction);
        Assert.Equal("podcast", podcast.Artwork?.Type);
        Assert.Equal("1", podcast.Artwork?.Id);

        podcast = podcasts[1];
        AssertTestAccount1(podcast.Account);
        Assert.Equal("2", podcast.Id);
        Assert.Equal("Plays Well with Others", podcast.Title);
        Assert.Equal(0, podcast.Rating);
        Assert.False(podcast.IsCached);
        Assert.Equal("From Creative Commons, a podcast about the art and science of collaboration. With a focus on the tools, techniques, and mechanics of collaboration, we explore how today's most interesting collaborators are making new things, solving old problems, and getting things done — together. Hosted by Creative Commons CEO Ryan Merkley.", podcast.Depiction);
        AssertTestAccount1(podcast.Artwork?.Account);
        Assert.Equal("podcast", podcast.Artwork?.Type);
        Assert.Equal("2", podcast.Artwork?.Id);

        podcast = podcasts[2];
        AssertTestAccount1(podcast.Account);
        Assert.Equal("5", podcast.Id);
        Assert.Equal("Trace", podcast.Title);
        Assert.Equal(1, podcast.Rating);
        Assert.False(podcast.IsCached);
        Assert.Equal("Lawyer Nicola Gobbo represented some of Australia’s most dangerous criminals, all the while secretly working as a police informer. Why did she do it, and how was it allowed to happen? For the first time, she tells the full story behind why she became an informer, and what happened when her double life was exposed to the world.", podcast.Depiction);
        AssertTestAccount1(podcast.Artwork?.Account);
        Assert.Equal("podcast", podcast.Artwork?.Type);
        Assert.Equal("5", podcast.Artwork?.Id);
    }
}
