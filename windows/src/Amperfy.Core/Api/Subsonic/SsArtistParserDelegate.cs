namespace Amperfy.Core.Api.Subsonic;

public class SsArtistParserDelegate : SsXmlLibWithArtworkParser
{
    private Artist? _artistBuffer;
    public List<Artist> ParsedArtists { get; } = [];

    public SsArtistParserDelegate(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(prefetch, account, library, parseNotifier)
    {
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        if (elementName != "artist") return;
        if (StrAttr(attributes, "id") is not { } artistId) return;

        if (!Prefetch.PrefetchedArtistDict.TryGetValue(artistId, out var artist))
        {
            artist = Library.CreateArtist(Account);
            Prefetch.PrefetchedArtistDict[artistId] = artist;
            artist.Id = artistId;
        }
        _artistBuffer = artist;
        artist.RemoteStatus = RemoteStatus.Available;
        if (SwiftInt(StrAttr(attributes, "albumCount")) is { } albumCount) artist.RemoteAlbumCount = albumCount;
        if (StrAttr(attributes, "name") is { } artistName) artist.Name = artistName;
        if (StrAttr(attributes, "coverArt") is { } coverArtId) artist.Artwork = ParseArtwork(coverArtId);
        artist.Rating = SwiftInt(StrAttr(attributes, "userRating") ?? "0") ?? 0;
        artist.IsFavorite = StrAttr(attributes, "starred") is not null;
    }

    protected override void DidEndElement(string elementName)
    {
        if (elementName == "artist")
        {
            ParsedCount += 1;
            if (_artistBuffer is { } artist) ParsedArtists.Add(artist);
            _artistBuffer = null;
        }
        base.DidEndElement(elementName);
    }
}
