namespace Amperfy.Core.Api.Subsonic;

public class SsAlbumParserDelegate : SsXmlLibWithArtworkParser
{
    private Artist? _guessedArtist;
    private Genre? _guessedGenre;
    public List<Album> ParsedAlbums { get; } = [];
    private Album? _albumBuffer;

    public SsAlbumParserDelegate(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(prefetch, account, library, parseNotifier)
    {
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        if (elementName != "album") return;
        if (StrAttr(attributes, "id") is not { } albumId) return;

        Album album;
        if (Prefetch.PrefetchedAlbumDict.TryGetValue(albumId, out var prefetchedAlbum))
        {
            album = prefetchedAlbum;
            _guessedArtist = prefetchedAlbum.Artist;
            _guessedGenre = prefetchedAlbum.Genre;
        }
        else
        {
            album = Library.CreateAlbum(Account);
            Prefetch.PrefetchedAlbumDict[albumId] = album;
            album.Id = albumId;
            _guessedArtist = null;
            _guessedGenre = null;
        }
        _albumBuffer = album;
        album.RemoteStatus = RemoteStatus.Available;

        if (StrAttr(attributes, "name") is { } albumName) album.Name = albumName;
        if (StrAttr(attributes, "coverArt") is { } coverArt) album.Artwork = ParseArtwork(coverArt);
        album.Rating = SwiftInt(StrAttr(attributes, "userRating") ?? "0") ?? 0;
        album.IsFavorite = StrAttr(attributes, "starred") is not null;
        if (SwiftInt(StrAttr(attributes, "year")) is { } year) album.Year = year;
        if (SwiftInt(StrAttr(attributes, "songCount")) is { } songCount) album.RemoteSongCount = songCount;
        if (SwiftInt(StrAttr(attributes, "duration")) is { } duration) album.RemoteDuration = duration;

        if (StrAttr(attributes, "artistId") is { } artistId)
        {
            if (_guessedArtist is { } guessedArtist && guessedArtist.Id == artistId)
            {
                album.Artist = guessedArtist;
            }
            else if (Prefetch.PrefetchedArtistDict.TryGetValue(artistId, out var prefetchedArtist))
            {
                album.Artist = prefetchedArtist;
            }
            else if (StrAttr(attributes, "artist") is { } artistName)
            {
                var artist = Library.CreateArtist(Account);
                Prefetch.PrefetchedArtistDict[artistId] = artist;
                artist.Id = artistId;
                artist.Name = artistName;
                AmperfyLog.Debug_("Parser", $"Artist <{artistName}> with id {artistId} has been created");
                album.Artist = artist;
            }
        }

        if (StrAttr(attributes, "genre") is { } genreName)
        {
            if (_guessedGenre is { } guessedGenre && guessedGenre.Name == genreName)
            {
                album.Genre = guessedGenre;
            }
            else if (Prefetch.PrefetchedGenreDict.TryGetValue(genreName, out var prefetchedGenre))
            {
                album.Genre = prefetchedGenre;
            }
            else
            {
                var genre = Library.CreateGenre(Account);
                Prefetch.PrefetchedGenreDict[genreName] = genre;
                genre.Name = genreName;
                AmperfyLog.Debug_("Parser", $"Genre <{genreName}> has been created");
                album.Genre = genre;
            }
        }
    }

    protected override void DidEndElement(string elementName)
    {
        if (elementName == "album")
        {
            ParsedCount += 1;
            if (_albumBuffer is { } album) ParsedAlbums.Add(album);
            _albumBuffer = null;
        }
        base.DidEndElement(elementName);
    }
}
