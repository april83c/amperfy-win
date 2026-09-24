namespace Amperfy.Core.Api.Ampache;

public class SongParserDelegate : PlayableParserDelegate
{
    public Song? SongBuffer { get; protected set; }
    public List<Song> ParsedSongs { get; } = [];
    private string? _artistIdToCreate;
    private string? _albumIdToCreate;
    private string? _genreIdToCreate;
    private Artist? _guessedArtist;
    private Album? _guessedAlbum;
    private Genre? _guessedGenre;

    public SongParserDelegate(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(prefetch, account, library, parseNotifier)
    {
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        switch (elementName)
        {
            case "song":
                {
                    if (!attributes.TryGetValue("id", out var songId))
                    {
                        AmperfyLog.Error(LogCategory, "Found song with no id");
                        return;
                    }
                    if (Prefetch.PrefetchedSongDict.TryGetValue(songId, out var prefetchedSong))
                    {
                        SongBuffer = prefetchedSong;
                        SongBuffer.RemoteStatus = RemoteStatus.Available;
                        _guessedArtist = prefetchedSong.Artist;
                        _guessedAlbum = prefetchedSong.Album;
                        _guessedGenre = prefetchedSong.Genre;
                    }
                    else
                    {
                        SongBuffer = Library.CreateSong(Account);
                        SongBuffer.Id = songId;
                        Prefetch.PrefetchedSongDict[songId] = SongBuffer;
                        _guessedArtist = null;
                        _guessedAlbum = null;
                        _guessedGenre = null;
                    }
                    PlayableBuffer = SongBuffer;
                    break;
                }
            case "artist":
                {
                    if (SongBuffer is not { } song || !attributes.TryGetValue("id", out var artistId)) return;
                    if (_guessedArtist is { } guessedArtist && guessedArtist.Id == artistId) song.Artist = guessedArtist;
                    else if (Prefetch.PrefetchedArtistDict.TryGetValue(artistId, out var prefetchedArtist)) song.Artist = prefetchedArtist;
                    else _artistIdToCreate = artistId;
                    break;
                }
            case "album":
                {
                    if (SongBuffer is not { } song || !attributes.TryGetValue("id", out var albumId)) return;
                    if (_guessedAlbum is { } guessedAlbum && guessedAlbum.Id == albumId) song.Album = guessedAlbum;
                    else if (Prefetch.PrefetchedAlbumDict.TryGetValue(albumId, out var prefetchedAlbum)) song.Album = prefetchedAlbum;
                    else _albumIdToCreate = albumId;
                    break;
                }
            case "genre":
                {
                    if (SongBuffer is not { } song || !attributes.TryGetValue("id", out var genreId)) return;
                    if (_guessedGenre is { } guessedGenre && guessedGenre.Id == genreId) song.Genre = guessedGenre;
                    else if (Prefetch.PrefetchedGenreDict.TryGetValue(genreId, out var prefetchedGenre)) song.Genre = prefetchedGenre;
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
                    if (SongBuffer is not null) SongBuffer.Artist = artist;
                    _artistIdToCreate = null;
                }
                break;
            case "album":
                if (_albumIdToCreate is { } albumId)
                {
                    AmperfyLog.Info(LogCategory, $"Album <{Buffer}> with id {albumId} has been created");
                    var album = Library.CreateAlbum(Account);
                    Prefetch.PrefetchedAlbumDict[albumId] = album;
                    album.Id = albumId;
                    album.Name = Buffer;
                    if (SongBuffer is not null) SongBuffer.Album = album;
                    _albumIdToCreate = null;
                }
                break;
            case "genre":
                if (_genreIdToCreate is { } genreId)
                {
                    AmperfyLog.Info(LogCategory, $"Genre <{Buffer}> with id {genreId} has been created");
                    var genre = Library.CreateGenre(Account);
                    Prefetch.PrefetchedGenreDict[genreId] = genre;
                    genre.Id = genreId;
                    genre.Name = Buffer;
                    if (SongBuffer is not null) SongBuffer.Genre = genre;
                    _genreIdToCreate = null;
                }
                break;
            case "song":
                ParsedCount += 1;
                ParseNotifier?.NotifyParsedObject(ParsedObjectType.Song);
                if (SongBuffer is not null) SongBuffer.Rating = Rating;
                Rating = 0;
                ResetPlayableBuffer();
                if (SongBuffer is { } parsedSong) ParsedSongs.Add(parsedSong);
                SongBuffer = null;
                break;
        }
        base.DidEndElement(elementName);
    }
}
