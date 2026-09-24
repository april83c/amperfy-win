namespace Amperfy.Core.Api.Subsonic;

public class SsSongParserDelegate : SsPlayableParserDelegate
{
    protected Song? SongBuffer { get; set; }
    public List<Song> ParsedSongs { get; } = [];
    private Artist? _guessedArtist;
    private Album? _guessedAlbum;
    private Genre? _guessedGenre;

    public SsSongParserDelegate(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(prefetch, account, library, parseNotifier)
    {
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        if (IsPlayableElement(elementName))
        {
            ParseSong(attributes);
        }
        base.DidStartElement(elementName, attributes);
    }

    private void ParseSong(IReadOnlyDictionary<string, string> attributes)
    {
        if (StrAttr(attributes, "id") is not { } songId) return;
        if (SwiftBool(StrAttr(attributes, "isDir") ?? "false") != false) return;

        if (Prefetch.PrefetchedSongDict.TryGetValue(songId, out var prefetchedSong))
        {
            SongBuffer = prefetchedSong;
            prefetchedSong.RemoteStatus = RemoteStatus.Available;
            _guessedArtist = prefetchedSong.Artist;
            _guessedAlbum = prefetchedSong.Album;
            _guessedGenre = prefetchedSong.Genre;
        }
        else
        {
            var created = Library.CreateSong(Account);
            Prefetch.PrefetchedSongDict[songId] = created;
            created.Id = songId;
            SongBuffer = created;
            _guessedArtist = null;
            _guessedAlbum = null;
            _guessedGenre = null;
        }
        var song = SongBuffer;
        PlayableBuffer = song;

        if (StrAttr(attributes, "artistId") is { } artistId)
        {
            if (_guessedArtist is { } guessedArtist && guessedArtist.Id == artistId)
            {
                song.Artist = guessedArtist;
                guessedArtist.RemoteStatus = RemoteStatus.Available;
            }
            else if (Prefetch.PrefetchedArtistDict.TryGetValue(artistId, out var prefetchedArtist))
            {
                song.Artist = prefetchedArtist;
                prefetchedArtist.RemoteStatus = RemoteStatus.Available;
            }
            else if (StrAttr(attributes, "artist") is { } artistName)
            {
                var artist = Library.CreateArtist(Account);
                Prefetch.PrefetchedArtistDict[artistId] = artist;
                artist.Id = artistId;
                artist.Name = artistName;
                AmperfyLog.Debug_("Parser", $"Artist <{artistName}> with id {artistId} has been created");
                song.Artist = artist;
            }
        }
        else if (StrAttr(attributes, "artist") is { } artistName)
        {
            if (_guessedArtist is { } guessedArtist && guessedArtist.Name == artistName)
            {
                song.Artist = guessedArtist;
            }
            else if (Prefetch.PrefetchedLocalArtistDict.TryGetValue(artistName, out var prefetchedArtist))
            {
                song.Artist = prefetchedArtist;
            }
            else
            {
                var artist = Library.CreateArtist(Account);
                Prefetch.PrefetchedLocalArtistDict[artistName] = artist;
                artist.Name = artistName;
                song.Artist = artist;
                AmperfyLog.Debug_("Parser", $"Local Artist <{artistName}> has been created (no id)");
            }
        }

        if (StrAttr(attributes, "albumId") is { } albumId)
        {
            if (_guessedAlbum is { } guessedAlbum && guessedAlbum.Id == albumId)
            {
                song.Album = guessedAlbum;
                guessedAlbum.RemoteStatus = RemoteStatus.Available;
            }
            else if (Prefetch.PrefetchedAlbumDict.TryGetValue(albumId, out var prefetchedAlbum))
            {
                song.Album = prefetchedAlbum;
                prefetchedAlbum.RemoteStatus = RemoteStatus.Available;
            }
            else if (StrAttr(attributes, "album") is { } albumName)
            {
                var album = Library.CreateAlbum(Account);
                Prefetch.PrefetchedAlbumDict[albumId] = album;
                album.Id = albumId;
                album.Name = albumName;
                AmperfyLog.Debug_("Parser", $"Album <{albumName}> with id {albumId} has been created");
                song.Album = album;
            }
        }

        if (StrAttr(attributes, "genre") is { } genreName)
        {
            if (_guessedGenre is { } guessedGenre && guessedGenre.Name == genreName)
            {
                song.Genre = guessedGenre;
            }
            else if (Prefetch.PrefetchedGenreDict.TryGetValue(genreName, out var prefetchedGenre))
            {
                song.Genre = prefetchedGenre;
            }
            else
            {
                var genre = Library.CreateGenre(Account);
                Prefetch.PrefetchedGenreDict[genreName] = genre;
                genre.Name = genreName;
                AmperfyLog.Debug_("Parser", $"Genre <{genreName}> has been created");
                song.Genre = genre;
            }
        }
        if (StrAttr(attributes, "created") is { } createdTag)
        {
            song.AddedDate = SubsonicDateParser.ParseIso8601WithFractionalSeconds(createdTag);
        }
    }

    protected override void DidEndElement(string elementName)
    {
        if (IsPlayableElement(elementName) && SongBuffer is not null)
        {
            ParsedCount += 1;
            ResetPlayableBuffer();
            ParsedSongs.Add(SongBuffer);
            SongBuffer = null;
        }
        base.DidEndElement(elementName);
    }
}
