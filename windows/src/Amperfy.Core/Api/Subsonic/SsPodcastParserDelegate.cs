namespace Amperfy.Core.Api.Subsonic;

public class SsPodcastParserDelegate : SsXmlLibWithArtworkParser
{
    public HashSet<Podcast> ParsedPodcasts { get; } = [];
    private Podcast? _podcastBuffer;

    public SsPodcastParserDelegate(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(prefetch, account, library, parseNotifier)
    {
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        if (elementName != "channel") return;
        if (StrAttr(attributes, "id") is not { } podcastId) return;
        if (StrAttr(attributes, "status") is not { } attributePodcastStatus || attributePodcastStatus == "error") return;

        if (!Prefetch.PrefetchedPodcastDict.TryGetValue(podcastId, out var podcast))
        {
            podcast = Library.CreatePodcast(Account);
            Prefetch.PrefetchedPodcastDict[podcastId] = podcast;
            podcast.Id = podcastId;
        }
        _podcastBuffer = podcast;
        podcast.RemoteStatus = RemoteStatus.Available;

        if (StrAttr(attributes, "title") is { } title) podcast.TitleRawParsed = title;
        if (StrAttr(attributes, "description") is { } description) podcast.DepictionRawParsed = description;
        if (StrAttr(attributes, "coverArt") is { } coverArt) podcast.Artwork = ParseArtwork(coverArt);
    }

    protected override void DidEndElement(string elementName)
    {
        if (elementName == "channel")
        {
            ParsedCount += 1;
            if (_podcastBuffer is { } parsedPodcast) ParsedPodcasts.Add(parsedPodcast);
            _podcastBuffer = null;
        }
        base.DidEndElement(elementName);
    }

    public override void PerformPostParseOperations()
    {
        foreach (var podcast in ParsedPodcasts)
        {
            podcast.Title = podcast.TitleRawParsed.Html2String();
            podcast.Depiction = podcast.DepictionRawParsed.Html2String();
        }
    }
}
