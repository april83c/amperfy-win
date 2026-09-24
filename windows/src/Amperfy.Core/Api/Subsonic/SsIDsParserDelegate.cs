namespace Amperfy.Core.Api.Subsonic;

/// First parser pass: collects all ids of a response (no storage access, may run on a
/// background thread) so that the real parser can prefetch the entities in bulk.
public class SsIDsParserDelegate : SsNotifiableXmlParser
{
    public PrefetchIdContainer PrefetchIDs { get; } = new();
    private bool _isIndex;

    public SsIDsParserDelegate(IParsedObjectNotifiable? parseNotifier = null) : base(parseNotifier) { }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);

        if (elementName == "indexes") _isIndex = true;

        var isDirectory = elementName == "child" && SwiftBool(StrAttr(attributes, "isDir")) == true;

        if (StrAttr(attributes, "id") is { } id)
        {
            if (elementName is "song" or "entry" or "child" or "episode")
            {
                if (isDirectory) PrefetchIDs.DirectoryIDs.Add(id);
                else if (elementName == "episode") PrefetchIDs.PodcastEpisodeIDs.Add(id);
                else PrefetchIDs.SongIDs.Add(id);
            }
            else if (elementName == "album")
            {
                PrefetchIDs.AlbumIDs.Add(id);
            }
            else if (elementName == "artist")
            {
                if (_isIndex) PrefetchIDs.DirectoryIDs.Add(id);
                else PrefetchIDs.ArtistIDs.Add(id);
            }
            else if (elementName == "musicFolder")
            {
                PrefetchIDs.MusicFolderIDs.Add(id);
            }
            else if (elementName == "channel")
            {
                PrefetchIDs.PodcastIDs.Add(id);
            }
            else if (elementName == "internetRadioStation")
            {
                PrefetchIDs.RadioIDs.Add(id);
            }
        }

        if (StrAttr(attributes, "artistId") is { } artistId)
        {
            PrefetchIDs.ArtistIDs.Add(artistId);
        }
        else if ((!_isIndex || elementName == "child") && !isDirectory && StrAttr(attributes, "artist") is { } artistName)
        {
            PrefetchIDs.LocalArtistNames.Add(artistName);
        }

        if (StrAttr(attributes, "albumId") is { } albumId) PrefetchIDs.AlbumIDs.Add(albumId);

        if (StrAttr(attributes, "coverArt") is { } coverArtId) PrefetchIDs.ArtworkIDs.Add(new ArtworkRemoteInfo(coverArtId, ""));

        // ignore podcast episode genre
        if (elementName != "episode" && StrAttr(attributes, "genre") is { } genreName) PrefetchIDs.GenreNames.Add(genreName);
    }

    protected override void DidEndElement(string elementName)
    {
        if (elementName == "indexes") _isIndex = false;
        else if (elementName == "genre") PrefetchIDs.GenreNames.Add(Buffer);
        base.DidEndElement(elementName);
    }
}
