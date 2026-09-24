namespace Amperfy.Core.Api.Ampache;

public sealed class PodcastEpisodeParserDelegate : PlayableParserDelegate
{
    public Podcast Podcast { get; }
    public PodcastEpisode? EpisodeBuffer { get; private set; }
    public List<PodcastEpisode> ParsedEpisodes { get; } = [];

    public PodcastEpisodeParserDelegate(Podcast podcast, PrefetchElementContainer prefetch, Account account, LibraryStorage library)
        : base(prefetch, account, library, parseNotifier: null)
    {
        Podcast = podcast;
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        if (elementName != "podcast_episode") return;
        if (!attributes.TryGetValue("id", out var episodeId))
        {
            AmperfyLog.Error(LogCategory, "Found podcast episode with no id");
            return;
        }
        if (Prefetch.PrefetchedPodcastEpisodeDict.TryGetValue(episodeId, out var prefetchedEpisode))
        {
            EpisodeBuffer = prefetchedEpisode;
        }
        else
        {
            EpisodeBuffer = Library.CreatePodcastEpisode(Account);
            EpisodeBuffer.Id = episodeId;
            Prefetch.PrefetchedPodcastEpisodeDict[episodeId] = EpisodeBuffer;
        }
        PlayableBuffer = EpisodeBuffer;
        EpisodeBuffer.Podcast = Podcast;
    }

    protected override void DidEndElement(string elementName)
    {
        switch (elementName)
        {
            case "description":
                if (EpisodeBuffer is not null) EpisodeBuffer.DepictionRawParsed = Buffer;
                break;
            case "pubdate":
                ParsePubDate();
                break;
            case "state":
                if (EpisodeBuffer is not null) EpisodeBuffer.PodcastStatus = PodcastEpisodeRemoteStatusExtensions.Create(Buffer);
                break;
            case "filelength":
                if (EpisodeBuffer is not null) EpisodeBuffer.RemoteDuration = Buffer.AsDurationInSeconds() ?? 0;
                break;
            case "filesize":
                if (EpisodeBuffer is not null) EpisodeBuffer.Size = Buffer.AsByteCount() ?? 0;
                break;
            case "art":
                if (EpisodeBuffer is not null) EpisodeBuffer.Artwork = ParseArtwork(Buffer);
                break;
            case "podcast_episode":
                ParsedCount += 1;
                ResetPlayableBuffer();
                if (EpisodeBuffer is not null) EpisodeBuffer.Rating = Rating;
                Rating = 0;
                if (EpisodeBuffer is { } episode) ParsedEpisodes.Add(episode);
                EpisodeBuffer = null;
                break;
        }
        base.DidEndElement(elementName);
    }

    private void ParsePubDate()
    {
        var buffer = Buffer;
        if (buffer.Contains('/'))
        {
            // "3/27/21, 3:30 AM" (Swift format "M-d-yy, h:mm a", parsed leniently)
            string[] formats = ["M/d/yy, h:mm tt", "M-d-yy, h:mm tt", "M/d/yy, h:mm:ss tt", "M/d/yyyy, h:mm tt"];
            var normalized = buffer.Replace(' ', ' ').Replace(' ', ' ');
            if (EpisodeBuffer is not null)
                EpisodeBuffer.PublishDate = DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
                    ? d
                    : DateTime.UnixEpoch;
        }
        else if (buffer.Length >= 21)
        {
            // "2011-02-03T14:46:43" (the time zone suffix is ignored, like in Swift)
            var dateWithoutTimeZoneString = buffer[..19];
            if (EpisodeBuffer is not null)
                EpisodeBuffer.PublishDate = DateTime.TryParseExact(dateWithoutTimeZoneString, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
                    ? d
                    : DateTime.UnixEpoch;
        }
        else
        {
            AmperfyLog.Error(LogCategory, $"Pubdate <{buffer}> could not be parsed of podcast episode");
        }
    }

    public override void PerformPostParseOperations()
    {
        foreach (var episode in ParsedEpisodes)
        {
            if (episode.TitleRaw is null) episode.Title = AmpacheHtml.Html2String(episode.TitleRawParsed);
            if (episode.Depiction is null) episode.Depiction = episode.DepictionRawParsed is { } d ? AmpacheHtml.Html2String(d) : null;
        }
    }
}
