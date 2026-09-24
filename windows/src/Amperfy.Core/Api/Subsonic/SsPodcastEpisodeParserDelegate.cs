namespace Amperfy.Core.Api.Subsonic;

public class SsPodcastEpisodeParserDelegate : SsPlayableParserDelegate
{
    private readonly Podcast? _podcast;
    private PodcastEpisode? _episodeBuffer;
    public List<PodcastEpisode> ParsedEpisodes { get; } = [];

    public SsPodcastEpisodeParserDelegate(Podcast? podcast, PrefetchElementContainer prefetch, Account account, LibraryStorage library)
        : base(prefetch, account, library)
    {
        _podcast = podcast;
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        if (elementName == "episode")
        {
            if (StrAttr(attributes, "id") is not { } episodeId)
            {
                AmperfyLog.Error("Parser", "Found podcast episode with no id");
                return;
            }
            if (!Prefetch.PrefetchedPodcastEpisodeDict.TryGetValue(episodeId, out var episode))
            {
                episode = Library.CreatePodcastEpisode(Account);
                Prefetch.PrefetchedPodcastEpisodeDict[episodeId] = episode;
                episode.Id = episodeId;
            }
            _episodeBuffer = episode;
            PlayableBuffer = episode;
            if (_podcast is { } podcast)
            {
                episode.Podcast = podcast;
            }
            else if (StrAttr(attributes, "channelId") is { } channelId)
            {
                episode.Podcast = Prefetch.PrefetchedPodcastDict.GetValueOrDefault(channelId);
            }

            if (StrAttr(attributes, "description") is { } description) episode.DepictionRawParsed = description;
            if (SubsonicDateParser.ParsePublishDate(StrAttr(attributes, "publishDate")) is { } publishDate)
            {
                // "2011-02-03T14:46:43"
                episode.PublishDate = publishDate;
            }
            if (StrAttr(attributes, "status") is { } status) episode.PodcastStatus = PodcastEpisodeRemoteStatusExtensions.Create(status);
            if (StrAttr(attributes, "streamId") is { } streamId) episode.StreamId = streamId;
            if (StrAttr(attributes, "coverArt") is { } coverArtId) episode.Artwork = ParseArtwork(coverArtId);
        }
        base.DidStartElement(elementName, attributes);
    }

    protected override void DidEndElement(string elementName)
    {
        base.DidEndElement(elementName);
        if (elementName == "episode" && _episodeBuffer is { } episode)
        {
            ParsedCount += 1;
            ResetPlayableBuffer();
            ParsedEpisodes.Add(episode);
            _episodeBuffer = null;
        }
    }

    public override void PerformPostParseOperations()
    {
        foreach (var episode in ParsedEpisodes)
        {
            // Html2String is CPU intensive: only convert when title/depiction are not set yet
            if (episode.TitleRaw is null) episode.Title = episode.TitleRawParsed.Html2String();
            if (episode.Depiction is null) episode.Depiction = episode.DepictionRawParsed?.Html2String();
        }
    }
}
