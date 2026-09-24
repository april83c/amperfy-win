namespace Amperfy.Core.Api.Ampache;

public sealed class AlbumParserDelegate : AmpacheXmlLibParser
{
    private const string LogCategory = "Ampache";

    public Album? AlbumBuffer { get; private set; }
    public List<Album> AlbumsParsedArray { get; } = [];
    public HashSet<Album> AlbumsParsedSet => [.. AlbumsParsedArray];
    private string? _artistIdToCreate;
    private string? _genreIdToCreate;
    private int _rating;

    public AlbumParserDelegate(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(prefetch, account, library, parseNotifier)
    {
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        switch (elementName)
        {
            case "album":
                {
                    if (!attributes.TryGetValue("id", out var albumId))
                    {
                        AmperfyLog.Error(LogCategory, "Found album with no id");
                        return;
                    }
                    if (Prefetch.PrefetchedAlbumDict.TryGetValue(albumId, out var prefetchedAlbum))
                    {
                        AlbumBuffer = prefetchedAlbum;
                    }
                    else
                    {
                        AlbumBuffer = Library.CreateAlbum(Account);
                        AlbumBuffer.Id = albumId;
                    }
                    break;
                }
            case "artist":
                {
                    if (AlbumBuffer is not { } album || !attributes.TryGetValue("id", out var artistId)) return;
                    if (Prefetch.PrefetchedArtistDict.TryGetValue(artistId, out var prefetchedArtist)) album.Artist = prefetchedArtist;
                    else _artistIdToCreate = artistId;
                    break;
                }
            case "genre":
                {
                    if (AlbumBuffer is not { } album) break;
                    if (!attributes.TryGetValue("id", out var genreId)) return;
                    if (Prefetch.PrefetchedGenreDict.TryGetValue(genreId, out var prefetchedGenre)) album.Genre = prefetchedGenre;
                    else _genreIdToCreate = genreId;
                    break;
                }
        }
    }

    protected override void DidEndElement(string elementName)
    {
        switch (elementName)
        {
            case "artist":
                if (_artistIdToCreate is { } artistId)
                {
                    AmperfyLog.Info(LogCategory, $"Artist <{Buffer}> with id {artistId} has been created");
                    var artist = Library.CreateArtist(Account);
                    Prefetch.PrefetchedArtistDict[artistId] = artist;
                    artist.Id = artistId;
                    artist.Name = Buffer;
                    if (AlbumBuffer is not null) AlbumBuffer.Artist = artist;
                    _artistIdToCreate = null;
                }
                break;
            case "name":
                if (AlbumBuffer is not null) AlbumBuffer.Name = Buffer;
                break;
            case "album":
                ParsedCount += 1;
                if (AlbumBuffer is not null) AlbumBuffer.Rating = _rating;
                _rating = 0;
                if (AlbumBuffer is { } parsedAlbum) AlbumsParsedArray.Add(parsedAlbum);
                ParseNotifier?.NotifyParsedObject(ParsedObjectType.Album);
                AlbumBuffer = null;
                break;
            case "rating":
                _rating = BufferIntOrZero;
                break;
            case "flag":
                if (AlbumBuffer is not null) AlbumBuffer.IsFavorite = BufferIntOrZero == 1;
                break;
            case "year":
                if (AlbumBuffer is not null) AlbumBuffer.Year = BufferIntOrZero;
                break;
            case "time":
                if (AlbumBuffer is not null) AlbumBuffer.RemoteDuration = BufferIntOrZero;
                break;
            case "songcount":
                if (AlbumBuffer is not null) AlbumBuffer.RemoteSongCount = BufferIntOrZero;
                break;
            case "art":
                if (AlbumBuffer is not null) AlbumBuffer.Artwork = ParseArtwork(Buffer);
                break;
            case "genre":
                if (_genreIdToCreate is { } genreId)
                {
                    AmperfyLog.Info(LogCategory, $"Genre <{Buffer}> with id {genreId} has been created");
                    var genre = Library.CreateGenre(Account);
                    Prefetch.PrefetchedGenreDict[genreId] = genre;
                    genre.Id = genreId;
                    genre.Name = Buffer;
                    if (AlbumBuffer is not null) AlbumBuffer.Genre = genre;
                    _genreIdToCreate = null;
                }
                break;
        }
        base.DidEndElement(elementName);
    }
}
