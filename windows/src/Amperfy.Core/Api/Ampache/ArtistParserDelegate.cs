namespace Amperfy.Core.Api.Ampache;

public sealed class ArtistParserDelegate : AmpacheXmlLibParser
{
    private const string LogCategory = "Ampache";

    public HashSet<Artist> ArtistsParsed { get; } = [];
    public Artist? ArtistBuffer { get; private set; }
    private string? _genreIdToCreate;
    private int _rating;

    public ArtistParserDelegate(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(prefetch, account, library, parseNotifier)
    {
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        if (elementName == "artist")
        {
            if (!attributes.TryGetValue("id", out var artistId))
            {
                AmperfyLog.Error(LogCategory, "Found artist with no id");
                return;
            }
            if (Prefetch.PrefetchedArtistDict.TryGetValue(artistId, out var prefetchedArtist))
            {
                ArtistBuffer = prefetchedArtist;
            }
            else
            {
                ArtistBuffer = Library.CreateArtist(Account);
                ArtistBuffer.Id = artistId;
            }
        }
        if (elementName == "genre" && ArtistBuffer is { } artist)
        {
            if (!attributes.TryGetValue("id", out var genreId)) return;
            if (Prefetch.PrefetchedGenreDict.TryGetValue(genreId, out var prefetchedGenre)) artist.Genre = prefetchedGenre;
            else _genreIdToCreate = genreId;
        }
    }

    protected override void DidEndElement(string elementName)
    {
        switch (elementName)
        {
            case "name":
                if (ArtistBuffer is not null) ArtistBuffer.Name = Buffer;
                break;
            case "rating":
                _rating = BufferIntOrZero;
                break;
            case "flag":
                if (ArtistBuffer is not null) ArtistBuffer.IsFavorite = BufferIntOrZero == 1;
                break;
            case "albumcount":
                if (ArtistBuffer is not null) ArtistBuffer.RemoteAlbumCount = BufferIntOrZero;
                break;
            case "time":
                if (ArtistBuffer is not null) ArtistBuffer.RemoteDuration = BufferIntOrZero;
                break;
            case "genre":
                if (_genreIdToCreate is { } genreId)
                {
                    AmperfyLog.Info(LogCategory, $"Genre <{Buffer}> with id {genreId} has been created");
                    var genre = Library.CreateGenre(Account);
                    Prefetch.PrefetchedGenreDict[genreId] = genre;
                    genre.Id = genreId;
                    genre.Name = Buffer;
                    if (ArtistBuffer is not null) ArtistBuffer.Genre = genre;
                    _genreIdToCreate = null;
                }
                break;
            case "art":
                if (ArtistBuffer is not null) ArtistBuffer.Artwork = ParseArtwork(Buffer);
                break;
            case "artist":
                ParsedCount += 1;
                ParseNotifier?.NotifyParsedObject(ParsedObjectType.Artist);
                if (ArtistBuffer is not null) ArtistBuffer.Rating = _rating;
                _rating = 0;
                if (ArtistBuffer is { } parsedArtist) ArtistsParsed.Add(parsedArtist);
                ArtistBuffer = null;
                break;
        }
        base.DidEndElement(elementName);
    }
}
