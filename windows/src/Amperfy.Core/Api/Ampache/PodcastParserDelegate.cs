namespace Amperfy.Core.Api.Ampache;

public sealed class PodcastParserDelegate : AmpacheXmlLibParser
{
    public HashSet<Podcast> ParsedPodcasts { get; } = [];
    private Podcast? _podcastBuffer;
    private int _rating;

    public PodcastParserDelegate(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(prefetch, account, library, parseNotifier)
    {
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        if (elementName != "podcast") return;
        if (!attributes.TryGetValue("id", out var podcastId))
        {
            AmperfyLog.Error("Ampache", "Error: Podcast could not be parsed -> id is not given");
            return;
        }
        if (Prefetch.PrefetchedPodcastDict.TryGetValue(podcastId, out var prefetchedPodcast))
        {
            _podcastBuffer = prefetchedPodcast;
        }
        else
        {
            _podcastBuffer = Library.CreatePodcast(Account);
            _podcastBuffer.Id = podcastId;
            Prefetch.PrefetchedPodcastDict[podcastId] = _podcastBuffer;
        }
        _podcastBuffer.RemoteStatus = RemoteStatus.Available;
    }

    protected override void DidEndElement(string elementName)
    {
        switch (elementName)
        {
            case "name":
                if (_podcastBuffer is not null) _podcastBuffer.TitleRawParsed = Buffer;
                break;
            case "description":
                if (_podcastBuffer is not null) _podcastBuffer.DepictionRawParsed = Buffer;
                break;
            case "rating":
                _rating = BufferIntOrZero;
                break;
            case "art":
                if (_podcastBuffer is not null) _podcastBuffer.Artwork = ParseArtwork(Buffer);
                break;
            case "podcast":
                if (_podcastBuffer is not null) _podcastBuffer.Rating = _rating;
                _rating = 0;
                if (_podcastBuffer is { } parsedPodcast) ParsedPodcasts.Add(parsedPodcast);
                _podcastBuffer = null;
                ParseNotifier?.NotifyParsedObject(ParsedObjectType.Podcast);
                break;
        }
        base.DidEndElement(elementName);
    }

    public override void PerformPostParseOperations()
    {
        foreach (var podcast in ParsedPodcasts)
        {
            podcast.Title = AmpacheHtml.Html2String(podcast.TitleRawParsed);
            podcast.Depiction = AmpacheHtml.Html2String(podcast.DepictionRawParsed);
        }
    }
}
