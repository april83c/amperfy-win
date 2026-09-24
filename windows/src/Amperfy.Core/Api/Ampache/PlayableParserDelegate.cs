namespace Amperfy.Core.Api.Ampache;

/// Common parsing of playable (song / podcast episode) elements.
public class PlayableParserDelegate : AmpacheXmlLibParser
{
    protected const string LogCategory = "Ampache";

    public AbstractPlayable? PlayableBuffer { get; set; }
    public int Rating { get; set; }
    private bool _isCached = true;
    private int _duration;

    public PlayableParserDelegate(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(prefetch, account, library, parseNotifier)
    {
    }

    public bool IsCollectionCached => ParsedCount > 0 && _isCached;

    public int CollectionDuration => ParsedCount > 0 ? _duration : 0;

    protected override void DidEndElement(string elementName)
    {
        switch (elementName)
        {
            case "title":
                if (PlayableBuffer is PodcastEpisode episode) episode.TitleRawParsed = Buffer;
                else if (PlayableBuffer is not null) PlayableBuffer.Title = Buffer;
                break;
            case "rating":
                Rating = BufferIntOrZero;
                break;
            case "flag":
                if (PlayableBuffer is not null) PlayableBuffer.IsFavorite = BufferIntOrZero == 1;
                break;
            case "track":
                if (PlayableBuffer is not null) PlayableBuffer.Track = BufferIntOrZero;
                break;
            case "url":
                if (PlayableBuffer is not null) PlayableBuffer.Url = Buffer;
                break;
            case "year":
                if (PlayableBuffer is not null) PlayableBuffer.Year = BufferIntOrZero;
                break;
            case "time":
                if (PlayableBuffer is not null) PlayableBuffer.RemoteDuration = BufferIntOrZero;
                break;
            case "art":
                if (PlayableBuffer is not null) PlayableBuffer.Artwork = ParseArtwork(Buffer);
                break;
            case "size":
                if (PlayableBuffer is not null) PlayableBuffer.Size = BufferAsLong ?? 0;
                break;
            case "bitrate":
                if (PlayableBuffer is not null) PlayableBuffer.Bitrate = BufferIntOrZero;
                break;
            case "mime":
                if (PlayableBuffer is not null) PlayableBuffer.ContentType = Buffer;
                break;
            case "disk":
                if (PlayableBuffer is not null) PlayableBuffer.Disk = Buffer;
                break;
            case "replaygain_album_gain":
                if (PlayableBuffer is not null) PlayableBuffer.ReplayGainAlbumGain = BufferAsFloat ?? 0f;
                break;
            case "replaygain_album_peak":
                if (PlayableBuffer is not null) PlayableBuffer.ReplayGainAlbumPeak = BufferAsFloat ?? 0f;
                break;
            case "replaygain_track_gain":
                if (PlayableBuffer is not null) PlayableBuffer.ReplayGainTrackGain = BufferAsFloat ?? 0f;
                break;
            case "replaygain_track_peak":
                if (PlayableBuffer is not null) PlayableBuffer.ReplayGainTrackPeak = BufferAsFloat ?? 0f;
                break;
        }
        base.DidEndElement(elementName);
    }

    public void ResetPlayableBuffer()
    {
        if (PlayableBuffer is { } playable)
        {
            _isCached = _isCached && playable.IsCached;
            _duration += playable.Duration;
        }
        PlayableBuffer = null;
    }
}
