namespace Amperfy.Core.Api.Subsonic;

/// Common attributes of songs and podcast episodes.
public class SsPlayableParserDelegate : SsXmlLibWithArtworkParser
{
    protected AbstractPlayable? PlayableBuffer { get; set; }
    private bool _isCached = true;

    /// True if all parsed playables are cached (false if nothing was parsed).
    public bool IsCollectionCached => ParsedCount > 0 && _isCached;

    public SsPlayableParserDelegate(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(prefetch, account, library, parseNotifier)
    {
    }

    protected static bool IsPlayableElement(string elementName) => elementName is "song" or "entry" or "child" or "episode";

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);

        if (elementName == "replayGain" && PlayableBuffer is { } p)
        {
            p.ReplayGainAlbumGain = SwiftFloat(StrAttr(attributes, "albumGain") ?? "0.0") ?? 0f;
            p.ReplayGainAlbumPeak = SwiftFloat(StrAttr(attributes, "albumPeak") ?? "0.0") ?? 0f;
            p.ReplayGainTrackGain = SwiftFloat(StrAttr(attributes, "trackGain") ?? "0.0") ?? 0f;
            p.ReplayGainTrackPeak = SwiftFloat(StrAttr(attributes, "trackPeak") ?? "0.0") ?? 0f;
        }

        if (!IsPlayableElement(elementName)) return;
        if (SwiftBool(StrAttr(attributes, "isDir") ?? "false") != false) return;
        var playable = PlayableBuffer;
        if (playable is null) return;

        if (StrAttr(attributes, "title") is { } attributeTitle)
        {
            if (elementName == "episode")
            {
                if (playable is PodcastEpisode episode) episode.TitleRawParsed = attributeTitle;
            }
            else
            {
                playable.Title = attributeTitle;
            }
        }
        if (SwiftInt(StrAttr(attributes, "track")) is { } track) playable.Track = track;
        if (SwiftInt(StrAttr(attributes, "year")) is { } year) playable.Year = year;
        if (SwiftInt(StrAttr(attributes, "duration")) is { } duration) playable.RemoteDuration = duration;
        if (SwiftLong(StrAttr(attributes, "size")) is { } size) playable.Size = size;
        // kb per second -> save as byte per second
        if (SwiftInt(StrAttr(attributes, "bitRate")) is { } bitrate) playable.Bitrate = bitrate * 1000;
        if (StrAttr(attributes, "contentType") is { } contentType) playable.ContentType = contentType;
        if (StrAttr(attributes, "discNumber") is { } disk) playable.Disk = disk;
        playable.Rating = SwiftInt(StrAttr(attributes, "userRating") ?? "0") ?? 0;
        if (StrAttr(attributes, "starred") is { } starredDate)
        {
            playable.IsFavorite = true;
            playable.StarredDate = SubsonicDateParser.ParseIso8601WithFractionalSeconds(starredDate);
        }
        else
        {
            playable.IsFavorite = false;
            playable.StarredDate = null;
        }
        if (StrAttr(attributes, "coverArt") is { } coverArtId) playable.Artwork = ParseArtwork(coverArtId);
    }

    protected override void DidEndElement(string elementName)
    {
        if (IsPlayableElement(elementName) && PlayableBuffer is not null) ResetPlayableBuffer();
        base.DidEndElement(elementName);
    }

    protected void ResetPlayableBuffer()
    {
        if (PlayableBuffer is { } playable) _isCached = _isCached && playable.IsCached;
        PlayableBuffer = null;
    }
}
