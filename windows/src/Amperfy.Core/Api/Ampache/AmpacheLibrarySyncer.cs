namespace Amperfy.Core.Api.Ampache;

/// Library synchronisation with an Ampache server (port of AmpacheLibrarySyncer.swift).
///
/// Swift ran the parsing in `storage.async.perform { }` blocks on a background context. Here the
/// blocks run on the main thread with <see cref="CommonLibrarySyncer.Library"/> (see ARCHITECTURE.md).
/// Like Swift's `perform`, errors thrown inside such a block are logged and swallowed and the
/// context is saved afterwards (`performAndGet` rethrows).
public sealed class AmpacheLibrarySyncer : CommonLibrarySyncer, ILibrarySyncer
{
    private const string LogCategory = "Ampache";

    private readonly AmpacheXmlServerApi _ampacheXmlServerApi;

    public AmpacheLibrarySyncer(AmpacheXmlServerApi ampacheXmlServerApi, Account account, INetworkMonitor networkMonitor, LibraryStorage library, EventLogger eventLogger)
        : base(account, networkMonitor, library, eventLogger)
    {
        _ampacheXmlServerApi = ampacheXmlServerApi;
    }

    // --- helpers -------------------------------------------------------------------------------

    /// Swift `storage.async.perform`: runs the body, swallows (logs) errors and saves the context.
    private void Perform(Action body)
    {
        try
        {
            body();
        }
        catch (Exception ex)
        {
            AmperfyLog.Error(LogCategory, $"Error during storage operation: {ex.Message}");
        }
        finally
        {
            Library.SaveContext();
        }
    }

    /// Swift `storage.async.performAndGet`: runs the body, saves the context and rethrows errors.
    private T PerformAndGet<T>(Func<T> body)
    {
        try
        {
            return body();
        }
        finally
        {
            Library.SaveContext();
        }
    }

    /// First "IDs only" parser pass (no storage access -> runs on the thread pool).
    private async Task<PrefetchIdContainer> ParseIdsAsync(ApiDataResponse response)
    {
        var idParserDelegate = await Task.Run(() =>
        {
            var parser = new IDsParserDelegate();
            Parse(response, parser, isThrowingErrorsAllowed: false);
            return parser;
        });
        return idParserDelegate.PrefetchIDs;
    }

    private PrefetchElementContainer GetPrefetch(PrefetchIdContainer prefetchIDs) => Library.GetElements(Account, prefetchIDs);

    private void ParseForError(ApiDataResponse response)
    {
        var parserDelegate = new AmpacheXmlParser();
        Parse(response, parserDelegate);
    }

    private void Parse(ApiDataResponse response, AmpacheXmlParser parserDelegate, bool throwForNotFoundErrors = false, bool isThrowingErrorsAllowed = true)
    {
        parserDelegate.Parse(response.Data);
        if (parserDelegate.ParserError is { } error && isThrowingErrorsAllowed)
        {
            AmperfyLog.Error(LogCategory, $"Error during response parsing: {error.Message}");
            throw new ResponseError(ResponseErrorType.Xml, cleansedUrl: response.Url is null ? null : _ampacheXmlServerApi.Cleanse(response.Url), data: response.Data);
        }
        if (parserDelegate.Error is { } apiError && apiError.AmpacheError is { } ampacheError && isThrowingErrorsAllowed &&
            (ampacheError.ShouldErrorBeDisplayedToUser() || throwForNotFoundErrors))
        {
            throw apiError.ToResponseError(response.Url is null ? null : _ampacheXmlServerApi.Cleanse(response.Url), response.Data);
        }
    }

    // --- initial sync --------------------------------------------------------------------------

    public async Task SyncInitialAsync(ISyncCallbacks? statusNotifier)
    {
        CreateCachedItemRepresentations(statusNotifier);
        var auth = await _ampacheXmlServerApi.RequestLibraryMetaDataAsync();

        statusNotifier?.NotifySyncStarted(ParsedObjectType.Genre, auth.GenreCount);
        var genreResponse = await _ampacheXmlServerApi.RequestGenresAsync();
        var genreIds = await ParseIdsAsync(genreResponse);
        Perform(() =>
        {
            var prefetch = GetPrefetch(genreIds);
            var parserDelegate = new GenreParserDelegate(prefetch, Account, Library, statusNotifier);
            Parse(genreResponse, parserDelegate, isThrowingErrorsAllowed: false);
        });

        statusNotifier?.NotifySyncStarted(ParsedObjectType.Artist, auth.ArtistCount);
        var pollCountArtist = Math.Max(1, (int)Math.Ceiling((double)auth.ArtistCount / AmpacheXmlServerApi.MaxItemCountToPollAtOnce));
        await Task.WhenAll(Enumerable.Range(0, pollCountArtist + 1).Select(async index =>
        {
            var artistsResponse = await _ampacheXmlServerApi.RequestArtistsAsync(index * AmpacheXmlServerApi.MaxItemCountToPollAtOnce);
            var ids = await ParseIdsAsync(artistsResponse);
            Perform(() =>
            {
                var prefetch = GetPrefetch(ids);
                var parserDelegate = new ArtistParserDelegate(prefetch, Account, Library, statusNotifier);
                Parse(artistsResponse, parserDelegate, isThrowingErrorsAllowed: false);
            });
        }));

        statusNotifier?.NotifySyncStarted(ParsedObjectType.Album, auth.AlbumCount);
        var pollCountAlbum = Math.Max(1, (int)Math.Ceiling((double)auth.AlbumCount / AmpacheXmlServerApi.MaxItemCountToPollAtOnce));
        await Task.WhenAll(Enumerable.Range(0, pollCountAlbum + 1).Select(async index =>
        {
            var albumsResponse = await _ampacheXmlServerApi.RequestAlbumsAsync(index * AmpacheXmlServerApi.MaxItemCountToPollAtOnce);
            var ids = await ParseIdsAsync(albumsResponse);
            Perform(() =>
            {
                var prefetch = GetPrefetch(ids);
                var parserDelegate = new AlbumParserDelegate(prefetch, Account, Library, statusNotifier);
                Parse(albumsResponse, parserDelegate, isThrowingErrorsAllowed: false);
            });
        }));

        statusNotifier?.NotifySyncStarted(ParsedObjectType.Playlist, auth.PlaylistCount);
        var playlistsResponse = await _ampacheXmlServerApi.RequestPlaylistsAsync();
        Perform(() =>
        {
            var parserDelegate = new PlaylistParserDelegate(Account, Library, statusNotifier);
            Parse(playlistsResponse, parserDelegate, isThrowingErrorsAllowed: false);
        });

        var isSupported = await _ampacheXmlServerApi.RequestServerPodcastSupportAsync();
        if (!isSupported) return;
        statusNotifier?.NotifySyncStarted(ParsedObjectType.Podcast, auth.PodcastCount);
        var podcastsResponse = await _ampacheXmlServerApi.RequestPodcastsAsync();
        var podcastIds = await ParseIdsAsync(podcastsResponse);
        Perform(() =>
        {
            var prefetch = GetPrefetch(podcastIds);
            var parserDelegate = new PodcastParserDelegate(prefetch, Account, Library, statusNotifier);
            Parse(podcastsResponse, parserDelegate, isThrowingErrorsAllowed: false);
            parserDelegate.PerformPostParseOperations();
        });
    }

    // --- library elements ----------------------------------------------------------------------

    public async Task SyncAsync(Genre genre)
    {
        if (!IsSyncAllowed) return;
        await Task.WhenAll(genre.Albums.Select(album => SyncAsync(album)));
    }

    public async Task SyncAsync(Artist artist)
    {
        if (!IsSyncAllowed) return;
        var artistResponse = await _ampacheXmlServerApi.RequestArtistInfoAsync(artist.Id);
        var artistIds = await ParseIdsAsync(artistResponse);
        Perform(() =>
        {
            try
            {
                var prefetch = GetPrefetch(artistIds);
                var parserDelegate = new ArtistParserDelegate(prefetch, Account, Library);
                Parse(artistResponse, parserDelegate, throwForNotFoundErrors: true);
            }
            catch (ResponseError responseError) when (responseError.AsAmpacheError() is { } ampacheError && !ampacheError.IsRemoteAvailable())
            {
                var reportError = new ResponseError(ResponseErrorType.Resource, responseError.StatusCode,
                    $"Artist \"{artist.Name}\" is no longer available on the server.",
                    artistResponse.Url is null ? null : _ampacheXmlServerApi.Cleanse(artistResponse.Url), artistResponse.Data);
                artist.RemoteStatus = RemoteStatus.Deleted;
                throw reportError;
            }
        });

        if (artist.RemoteStatus != RemoteStatus.Available) return;
        var artistAlbumsResponse = await _ampacheXmlServerApi.RequestArtistAlbumsAsync(artist.Id);
        var albumIds = await ParseIdsAsync(artistAlbumsResponse);
        Perform(() =>
        {
            var oldAlbums = artist.Albums.ToHashSet();
            var prefetch = GetPrefetch(albumIds);
            var parserDelegate = new AlbumParserDelegate(prefetch, Account, Library);
            Parse(artistAlbumsResponse, parserDelegate);
            oldAlbums.ExceptWith(parserDelegate.AlbumsParsedSet);
            foreach (var album in oldAlbums)
            {
                AmperfyLog.Info(LogCategory, $"Album <{album.Name}> is remote deleted");
                album.RemoteStatus = RemoteStatus.Deleted;
                foreach (var song in album.Songs)
                {
                    AmperfyLog.Info(LogCategory, $"Song <{song.DisplayString}> is remote deleted");
                    song.RemoteStatus = RemoteStatus.Deleted;
                }
            }
        });

        var artistSongsResponse = await _ampacheXmlServerApi.RequestArtistSongsAsync(artist.Id);
        var songIds = await ParseIdsAsync(artistSongsResponse);
        Perform(() =>
        {
            var oldSongs = artist.Songs.ToHashSet();
            var prefetch = GetPrefetch(songIds);
            var parserDelegate = new SongParserDelegate(prefetch, Account, Library);
            Parse(artistSongsResponse, parserDelegate);
            oldSongs.ExceptWith(parserDelegate.ParsedSongs);
            foreach (var song in oldSongs)
            {
                AmperfyLog.Info(LogCategory, $"Song <{song.DisplayString}> is remote deleted");
                song.RemoteStatus = RemoteStatus.Deleted;
            }
        });
    }

    public async Task SyncAsync(Album album)
    {
        if (!IsSyncAllowed) return;
        var albumResponse = await _ampacheXmlServerApi.RequestAlbumInfoAsync(album.Id);
        var albumIds = await ParseIdsAsync(albumResponse);
        Perform(() =>
        {
            try
            {
                var prefetch = GetPrefetch(albumIds);
                var parserDelegate = new AlbumParserDelegate(prefetch, Account, Library);
                Parse(albumResponse, parserDelegate, throwForNotFoundErrors: true);
            }
            catch (ResponseError responseError) when (responseError.AsAmpacheError() is { } ampacheError && !ampacheError.IsRemoteAvailable())
            {
                var reportError = new ResponseError(ResponseErrorType.Resource, responseError.StatusCode,
                    $"Album \"{album.Name}\" is no longer available on the server.",
                    albumResponse.Url is null ? null : _ampacheXmlServerApi.Cleanse(albumResponse.Url), albumResponse.Data);
                album.MarkAsRemoteDeleted();
                throw reportError;
            }
        });

        if (album.RemoteStatus != RemoteStatus.Available) return;
        var albumSongsResponse = await _ampacheXmlServerApi.RequestAlbumSongsAsync(album.Id);
        var songIds = await ParseIdsAsync(albumSongsResponse);
        Perform(() =>
        {
            var oldSongs = album.Songs.ToHashSet();
            var prefetch = GetPrefetch(songIds);
            var parserDelegate = new SongParserDelegate(prefetch, Account, Library);
            Parse(albumSongsResponse, parserDelegate);
            oldSongs.ExceptWith(parserDelegate.ParsedSongs);
            foreach (var song in oldSongs)
            {
                AmperfyLog.Info(LogCategory, $"Song <{song.DisplayString}> is remote deleted");
                song.RemoteStatus = RemoteStatus.Deleted;
                album.SongsRaw.Remove(song);
            }
            album.IsSongsMetaDataSynced = true;
            album.IsCached = parserDelegate.IsCollectionCached;
        });
    }

    public async Task SyncAsync(Song song)
    {
        if (!IsSyncAllowed) return;
        var response = await _ampacheXmlServerApi.RequestSongInfoAsync(song.Id);
        var ids = await ParseIdsAsync(response);
        Perform(() =>
        {
            var prefetch = GetPrefetch(ids);
            var parserDelegate = new SongParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
        });
    }

    public async Task SyncAsync(Podcast podcast)
    {
        if (!IsSyncAllowed) return;
        var isSupported = await _ampacheXmlServerApi.RequestServerPodcastSupportAsync();
        if (!isSupported) return;
        var response = await _ampacheXmlServerApi.RequestPodcastEpisodesAsync(podcast.Id);
        var ids = await ParseIdsAsync(response);
        Perform(() =>
        {
            var oldEpisodes = podcast.Episodes.ToHashSet();
            var prefetch = GetPrefetch(ids);
            var parserDelegate = new PodcastEpisodeParserDelegate(podcast, prefetch, Account, Library);
            Parse(response, parserDelegate);
            parserDelegate.PerformPostParseOperations();

            oldEpisodes.ExceptWith(parserDelegate.ParsedEpisodes);
            foreach (var episode in oldEpisodes)
            {
                AmperfyLog.Info(LogCategory, $"Podcast Episode <{episode.DisplayString}> is remote deleted");
                episode.PodcastStatus = PodcastEpisodeRemoteStatus.Deleted;
            }
            podcast.IsCached = parserDelegate.IsCollectionCached;
        });
    }

    public async Task SyncNewestPodcastEpisodesAsync()
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, "Sync newest podcast episodes");
        var isSupported = await _ampacheXmlServerApi.RequestServerPodcastSupportAsync();
        if (!isSupported) return;
        await SyncDownPodcastsWithoutEpisodesAsync();

        var podcasts = Library.GetPodcasts(Account).Where(p => p.RemoteStatus == RemoteStatus.Available).ToList();
        await Task.WhenAll(podcasts.Select(async podcast =>
        {
            var response = await _ampacheXmlServerApi.RequestPodcastEpisodesAsync(podcast.Id, limit: 5);
            var ids = await ParseIdsAsync(response);
            Perform(() =>
            {
                var prefetch = GetPrefetch(ids);
                var parserDelegate = new PodcastEpisodeParserDelegate(podcast, prefetch, Account, Library);
                Parse(response, parserDelegate);
                parserDelegate.PerformPostParseOperations();
            });
        }));
    }

    // --- music folders / directories -----------------------------------------------------------

    public async Task SyncMusicFoldersAsync()
    {
        if (!IsSyncAllowed) return;
        var response = await _ampacheXmlServerApi.RequestCatalogsAsync();
        var ids = await ParseIdsAsync(response);
        Perform(() =>
        {
            var prefetch = GetPrefetch(ids);
            var parserDelegate = new CatalogParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
        });
    }

    public async Task SyncIndexesAsync(MusicFolder musicFolder)
    {
        if (!IsSyncAllowed) return;
        var response = await _ampacheXmlServerApi.RequestArtistWithinCatalogAsync(musicFolder.Id);
        var ids = await ParseIdsAsync(response);
        Perform(() =>
        {
            var prefetch = GetPrefetch(ids);
            var parserDelegate = new ArtistParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);

            var directoriesBeforeFetch = musicFolder.Directories.ToHashSet();
            var directoriesAfterFetch = new HashSet<MusicDirectory>();

            var artistDirectoryIdsSet = parserDelegate.ArtistsParsed.Select(a => $"artist-{a.Id}").ToHashSet();
            var directoriesForArtists = Library.GetDirectories(Account, artistDirectoryIdsSet);

            foreach (var artist in parserDelegate.ArtistsParsed)
            {
                var artistDirId = $"artist-{artist.Id}";
                if (!directoriesForArtists.TryGetValue(artistDirId, out var curDir))
                {
                    curDir = Library.CreateDirectory(Account);
                    curDir.Id = artistDirId;
                    directoriesForArtists[artistDirId] = curDir;
                }
                curDir.Name = artist.Name;
                musicFolder.DirectoriesRaw.Add(curDir);
                directoriesAfterFetch.Add(curDir);
            }

            directoriesBeforeFetch.ExceptWith(directoriesAfterFetch);
            foreach (var removed in directoriesBeforeFetch) Library.DeleteDirectory(removed);
        });
    }

    public async Task SyncAsync(MusicDirectory directory)
    {
        if (!IsSyncAllowed) return;
        if (directory.Id.StartsWith("album-", StringComparison.Ordinal))
        {
            var albumId = directory.Id["album-".Length..];
            await SyncAlbumDirectoryAsync(directory, albumId);
        }
        else if (directory.Id.StartsWith("artist-", StringComparison.Ordinal))
        {
            var artistId = directory.Id["artist-".Length..];
            await SyncArtistDirectoryAsync(directory, artistId);
        }
        // else: do nothing
    }

    private async Task SyncAlbumDirectoryAsync(MusicDirectory directory, string thatIsAlbumId)
    {
        if (Library.GetAlbum(Account, thatIsAlbumId) is not { } album) return;
        var songsBeforeFetch = directory.Songs.ToHashSet();

        await SyncAsync(album);
        Perform(() =>
        {
            foreach (var song in directory.SongsRaw.ToList()) directory.SongsRaw.Remove(song);
            var albumSongs = album.Songs;
            songsBeforeFetch.ExceptWith(albumSongs);
            foreach (var song in songsBeforeFetch) directory.SongsRaw.Remove(song);
            foreach (var song in albumSongs) directory.SongsRaw.Add(song);
            directory.IsCached = album.IsCached;
        });
    }

    private async Task SyncArtistDirectoryAsync(MusicDirectory directory, string thatIsArtistId)
    {
        if (Library.GetArtist(Account, thatIsArtistId) is not { } artist) return;
        var directoriesBeforeFetch = directory.Subdirectories.ToHashSet();

        await SyncAsync(artist);
        Perform(() =>
        {
            var directoriesAfterFetch = new HashSet<MusicDirectory>();
            var artistAlbums = Library.GetAlbums(Account, artist);

            var albumDirectoryIdsSet = artistAlbums.Select(a => $"album-{a.Id}").ToHashSet();
            var directoriesForAlbums = Library.GetDirectories(Account, albumDirectoryIdsSet);

            foreach (var album in artistAlbums)
            {
                var albumDirId = $"album-{album.Id}";
                if (!directoriesForAlbums.TryGetValue(albumDirId, out var albumDir))
                {
                    albumDir = Library.CreateDirectory(Account);
                    albumDir.Id = albumDirId;
                    directoriesForAlbums[albumDirId] = albumDir;
                }
                albumDir.Name = album.Name;
                albumDir.Artwork = album.Artwork;
                directory.SubdirectoriesRaw.Add(albumDir);
                directoriesAfterFetch.Add(albumDir);
            }

            directoriesBeforeFetch.ExceptWith(directoriesAfterFetch);
            foreach (var removed in directoriesBeforeFetch) directory.SubdirectoriesRaw.Remove(removed);
        });
    }

    // --- newest / recent / favorites -----------------------------------------------------------

    public async Task SyncNewestAlbumsAsync(int offset, int count)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Sync newest albums: offset: {offset} count: {count}");
        var response = await _ampacheXmlServerApi.RequestNewestAlbumsAsync(offset, count);
        var ids = await ParseIdsAsync(response);
        Perform(() =>
        {
            var prefetch = GetPrefetch(ids);
            var parserDelegate = new AlbumParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
            var oldNewestAlbums = Library.GetNewestAlbums(Account, offset, count);
            foreach (var album in oldNewestAlbums) album.MarkAsNotNewAnymore();
            for (var index = 0; index < parserDelegate.AlbumsParsedArray.Count; index++)
                parserDelegate.AlbumsParsedArray[index].UpdateIsNewestInfo(index + 1 + offset);
        });
    }

    public async Task SyncRecentAlbumsAsync(int offset, int count)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Sync recent albums: offset: {offset} count: {count}");
        var response = await _ampacheXmlServerApi.RequestRecentAlbumsAsync(offset, count);
        var ids = await ParseIdsAsync(response);
        Perform(() =>
        {
            var prefetch = GetPrefetch(ids);
            var parserDelegate = new AlbumParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
            var oldRecentAlbums = Library.GetRecentAlbums(Account, offset, count);
            foreach (var album in oldRecentAlbums) album.MarkAsNotRecentAnymore();
            for (var index = 0; index < parserDelegate.AlbumsParsedArray.Count; index++)
                parserDelegate.AlbumsParsedArray[index].UpdateIsRecentInfo(index + 1 + offset);
        });
    }

    public async Task SyncFavoriteLibraryElementsAsync()
    {
        if (!IsSyncAllowed) return;
        var artistsResponse = await _ampacheXmlServerApi.RequestFavoriteArtistsAsync();
        var artistIds = await ParseIdsAsync(artistsResponse);
        Perform(() =>
        {
            AmperfyLog.Info(LogCategory, "Sync favorite artists");
            var oldFavoriteArtists = Library.GetFavoriteArtists(Account).ToHashSet();
            var prefetch = GetPrefetch(artistIds);
            var parserDelegate = new ArtistParserDelegate(prefetch, Account, Library);
            Parse(artistsResponse, parserDelegate);
            oldFavoriteArtists.ExceptWith(parserDelegate.ArtistsParsed);
            foreach (var artist in oldFavoriteArtists)
            {
                artist.IsFavorite = false;
                artist.StarredDate = null;
            }
        });

        var albumsResponse = await _ampacheXmlServerApi.RequestFavoriteAlbumsAsync();
        var albumIds = await ParseIdsAsync(albumsResponse);
        Perform(() =>
        {
            AmperfyLog.Info(LogCategory, "Sync favorite albums");
            var oldFavoriteAlbums = Library.GetFavoriteAlbums(Account).ToHashSet();
            var prefetch = GetPrefetch(albumIds);
            var parserDelegate = new AlbumParserDelegate(prefetch, Account, Library);
            Parse(albumsResponse, parserDelegate);
            oldFavoriteAlbums.ExceptWith(parserDelegate.AlbumsParsedSet);
            foreach (var album in oldFavoriteAlbums)
            {
                album.IsFavorite = false;
                album.StarredDate = null;
            }
        });

        var songsResponse = await _ampacheXmlServerApi.RequestFavoriteSongsAsync();
        var songIds = await ParseIdsAsync(songsResponse);
        Perform(() =>
        {
            AmperfyLog.Info(LogCategory, "Sync favorite songs");
            var oldFavoriteSongs = Library.GetFavoriteSongs(Account).ToHashSet();
            var prefetch = GetPrefetch(songIds);
            var parserDelegate = new SongParserDelegate(prefetch, Account, Library);
            Parse(songsResponse, parserDelegate);
            oldFavoriteSongs.ExceptWith(parserDelegate.ParsedSongs);
            foreach (var song in oldFavoriteSongs)
            {
                song.IsFavorite = false;
                song.StarredDate = null;
            }
        });
    }

    public async Task SyncRadiosAsync()
    {
        if (!IsSyncAllowed) return;
        var response = await _ampacheXmlServerApi.RequestRadiosAsync();
        var ids = await ParseIdsAsync(response);
        Perform(() =>
        {
            var oldRadios = Library.GetRadios(Account).ToHashSet();
            var prefetch = GetPrefetch(ids);
            var parserDelegate = new RadioParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
            oldRadios.ExceptWith(parserDelegate.ParsedRadios);
            foreach (var radio in oldRadios)
            {
                AmperfyLog.Info(LogCategory, $"Radio <{radio.DisplayString}> is remote deleted");
                radio.RemoteStatus = RemoteStatus.Deleted;
            }
        });
    }

    public async Task RequestRandomSongsAsync(Playlist playlist, int count)
    {
        if (!IsSyncAllowed) return;
        var response = await _ampacheXmlServerApi.RequestRandomSongsAsync(count);
        var ids = await ParseIdsAsync(response);
        Perform(() =>
        {
            var prefetch = GetPrefetch(ids);
            var parserDelegate = new SongParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
            playlist.Append(parserDelegate.ParsedSongs.Cast<AbstractPlayable>().ToList());
        });
    }

    public async Task<List<Song>> RequestSimilarSongsAsync(Song song, int count)
    {
        if (!IsSyncAllowed) return [];
        var response = await _ampacheXmlServerApi.RequestSimilarSongsAsync(song.Id, count);
        var ids = await ParseIdsAsync(response);
        return PerformAndGet(() =>
        {
            var prefetch = GetPrefetch(ids);
            var parserDelegate = new SongParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
            return parserDelegate.ParsedSongs.ToList();
        });
    }

    public async Task RequestPodcastEpisodeDeleteAsync(PodcastEpisode podcastEpisode)
    {
        if (!IsSyncAllowed) return;
        var response = await _ampacheXmlServerApi.RequestPodcastEpisodeDeleteAsync(podcastEpisode.Id);
        ParseForError(response);
    }

    // --- playlists -----------------------------------------------------------------------------

    public async Task SyncDownPlaylistsWithoutSongsAsync()
    {
        if (!IsSyncAllowed) return;
        var response = await _ampacheXmlServerApi.RequestPlaylistsAsync();
        Perform(() =>
        {
            var parserDelegate = new PlaylistParserDelegate(Account, Library, parseNotifier: null);
            Parse(response, parserDelegate);
        });
    }

    public async Task SyncDownAsync(Playlist playlist)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Playlist \"{playlist.Name}\": Validate from server");
        await ValidatePlaylistIdAsync(playlist);
        AmperfyLog.Info(LogCategory, $"Playlist \"{playlist.Name}\": Download songs from server");
        var response = await _ampacheXmlServerApi.RequestPlaylistSongsAsync(playlist.Id);

        AmperfyLog.Info(LogCategory, $"Playlist \"{playlist.Name}\": Parse songs start");
        var ids = await ParseIdsAsync(response);
        Perform(() =>
        {
            var prefetch = GetPrefetch(ids);
            var parserDelegate = new PlaylistSongsParserDelegate(playlist, prefetch, Account, Library);
            Parse(response, parserDelegate);
            playlist.IsCached = parserDelegate.IsCollectionCached;
            playlist.RemoteDuration = parserDelegate.CollectionDuration;
            AmperfyLog.Info(LogCategory, $"Playlist \"{playlist.Name}\": Parse songs ({parserDelegate.ParsedElementCount}) done");
        });
    }

    public async Task SyncUploadPlaylistAddSongsAsync(Playlist playlist, IReadOnlyList<Song> songs)
    {
        if (!IsSyncAllowed || songs.Count == 0) return;
        AmperfyLog.Info(LogCategory, $"Upload SongsAdded on playlist \"{playlist.Name}\"");
        await ValidatePlaylistIdAsync(playlist);
        foreach (var song in songs)
        {
            var response = await _ampacheXmlServerApi.RequestPlaylistAddSongAsync(playlist.Id, song.Id);
            ParseForError(response);
        }
    }

    public async Task SyncUploadPlaylistDeleteSongAsync(Playlist playlist, int index)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Upload SongDelete on playlist \"{playlist.Name}\" at index: {index}");
        await ValidatePlaylistIdAsync(playlist);
        var response = await _ampacheXmlServerApi.RequestPlaylistDeleteItemAsync(playlist.Id, index);
        ParseForError(response);
    }

    public async Task SyncUploadPlaylistNameAsync(Playlist playlist)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Upload name on playlist to: \"{playlist.Name}\"");
        var response = await _ampacheXmlServerApi.RequestPlaylistEditOnlyNameAsync(playlist.Id, playlist.Name);
        ParseForError(response);
    }

    public async Task SyncUploadPlaylistOrderAsync(Playlist playlist)
    {
        if (!IsSyncAllowed || playlist.SongCount <= 0) return;
        AmperfyLog.Info(LogCategory, $"Upload OrderChange on playlist \"{playlist.Name}\"");
        var songIds = playlist.Playables.Select(p => p.Id).ToList();
        if (songIds.Count == 0) return;
        var response = await _ampacheXmlServerApi.RequestPlaylistEditAsync(playlist.Id, songIds);
        ParseForError(response);
    }

    public async Task SyncUploadPlaylistDeleteAsync(string playlistId)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Upload Delete playlist \"{playlistId}\"");
        var response = await _ampacheXmlServerApi.RequestPlaylistDeleteAsync(playlistId);
        ParseForError(response);
    }

    private async Task ValidatePlaylistIdAsync(Playlist playlist)
    {
        var playlistResponse = await _ampacheXmlServerApi.RequestPlaylistAsync(playlist.Id);
        Perform(() =>
        {
            var parserDelegate = new PlaylistParserDelegate(Account, Library, parseNotifier: null, playlistToValidate: playlist);
            Parse(playlistResponse, parserDelegate);
        });
        if (playlist.Id != "") return;
        AmperfyLog.Info(LogCategory, "Create playlist on server");

        var playlistCreateResponse = await _ampacheXmlServerApi.RequestPlaylistCreateAsync(playlist.Name);
        Perform(() =>
        {
            var parserDelegate = new PlaylistParserDelegate(Account, Library, parseNotifier: null, playlistToValidate: playlist);
            Parse(playlistCreateResponse, parserDelegate);
        });
        if (playlist.Id == "")
        {
            AmperfyLog.Info(LogCategory, "Playlist id was not assigned after creation");
            throw new BackendError(BackendErrorKind.IncorrectServerBehavior, "Playlist id was not assigned after creation");
        }
    }

    // --- podcasts ------------------------------------------------------------------------------

    public async Task SyncDownPodcastsWithoutEpisodesAsync()
    {
        if (!IsSyncAllowed) return;
        var isSupported = await _ampacheXmlServerApi.RequestServerPodcastSupportAsync();
        if (!isSupported) return;
        var response = await _ampacheXmlServerApi.RequestPodcastsAsync();
        var ids = await ParseIdsAsync(response);
        Perform(() =>
        {
            var oldPodcasts = Library.GetRemoteAvailablePodcasts(Account).ToHashSet();
            var prefetch = GetPrefetch(ids);
            var parserDelegate = new PodcastParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
            parserDelegate.PerformPostParseOperations();

            oldPodcasts.ExceptWith(parserDelegate.ParsedPodcasts);
            foreach (var podcast in oldPodcasts)
            {
                AmperfyLog.Info(LogCategory, $"Podcast <{podcast.Title}> is remote deleted");
                podcast.RemoteStatus = RemoteStatus.Deleted;
            }
        });
    }

    // --- scrobble / rating / favorites ---------------------------------------------------------

    /// Ampache has no equivalent to Subsonic's NowPlaying
    public async Task SyncNowPlayingAsync(Song song, NowPlayingSongPosition songPosition)
    {
        switch (songPosition)
        {
            case NowPlayingSongPosition.Start:
                break; // Do nothing
            case NowPlayingSongPosition.End:
                await ScrobbleAsync(song, null);
                break;
        }
    }

    public async Task ScrobbleAsync(Song song, DateTime? date)
    {
        if (!IsSyncAllowed) return;
        if (date is { } d) AmperfyLog.Info(LogCategory, $"Scrobbled at {d:O}: {song.DisplayString}");
        else AmperfyLog.Info(LogCategory, $"Scrobble now: {song.DisplayString}");
        var response = await _ampacheXmlServerApi.RequestRecordPlayAsync(song.Id, date);
        ParseForError(response);
    }

    public async Task SetRatingAsync(Song song, int rating)
    {
        if (!IsSyncAllowed || rating < 0 || rating > 5) return;
        AmperfyLog.Info(LogCategory, $"Rate {rating} stars: {song.DisplayString}");
        var response = await _ampacheXmlServerApi.RequestRateSongAsync(song.Id, rating);
        ParseForError(response);
    }

    public async Task SetRatingAsync(Album album, int rating)
    {
        if (!IsSyncAllowed || rating < 0 || rating > 5) return;
        AmperfyLog.Info(LogCategory, $"Rate {rating} stars: {album.Name}");
        var response = await _ampacheXmlServerApi.RequestRateAlbumAsync(album.Id, rating);
        ParseForError(response);
    }

    public async Task SetRatingAsync(Artist artist, int rating)
    {
        if (!IsSyncAllowed || rating < 0 || rating > 5) return;
        AmperfyLog.Info(LogCategory, $"Rate {rating} stars: {artist.Name}");
        var response = await _ampacheXmlServerApi.RequestRateArtistAsync(artist.Id, rating);
        ParseForError(response);
    }

    public async Task SetFavoriteAsync(Song song, bool isFavorite)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Set Favorite {(isFavorite ? "TRUE" : "FALSE")}: {song.DisplayString}");
        var response = await _ampacheXmlServerApi.RequestSetFavoriteSongAsync(song.Id, isFavorite);
        ParseForError(response);
    }

    public async Task SetFavoriteAsync(Album album, bool isFavorite)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Set Favorite {(isFavorite ? "TRUE" : "FALSE")}: {album.Name}");
        var response = await _ampacheXmlServerApi.RequestSetFavoriteAlbumAsync(album.Id, isFavorite);
        ParseForError(response);
    }

    public async Task SetFavoriteAsync(Artist artist, bool isFavorite)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Set Favorite {(isFavorite ? "TRUE" : "FALSE")}: {artist.Name}");
        var response = await _ampacheXmlServerApi.RequestSetFavoriteArtistAsync(artist.Id, isFavorite);
        ParseForError(response);
    }

    // --- search --------------------------------------------------------------------------------

    public async Task SearchArtistsAsync(string searchText)
    {
        if (!IsSyncAllowed || searchText.Length == 0) return;
        AmperfyLog.Info(LogCategory, $"Search artists via API: \"{searchText}\"");
        var response = await _ampacheXmlServerApi.RequestSearchArtistsAsync(searchText);
        var ids = await ParseIdsAsync(response);
        Perform(() =>
        {
            var prefetch = GetPrefetch(ids);
            var parserDelegate = new ArtistParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
        });
    }

    public async Task SearchAlbumsAsync(string searchText)
    {
        if (!IsSyncAllowed || searchText.Length == 0) return;
        AmperfyLog.Info(LogCategory, $"Search albums via API: \"{searchText}\"");
        var response = await _ampacheXmlServerApi.RequestSearchAlbumsAsync(searchText);
        var ids = await ParseIdsAsync(response);
        Perform(() =>
        {
            var prefetch = GetPrefetch(ids);
            var parserDelegate = new AlbumParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
        });
    }

    public async Task SearchSongsAsync(string searchText)
    {
        if (!IsSyncAllowed || searchText.Length == 0) return;
        AmperfyLog.Info(LogCategory, $"Search songs via API: \"{searchText}\"");
        var response = await _ampacheXmlServerApi.RequestSearchSongsAsync(searchText);
        var ids = await ParseIdsAsync(response);
        Perform(() =>
        {
            var prefetch = GetPrefetch(ids);
            var parserDelegate = new SongParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
        });
    }

    /// Ampache provides no lyrics: like Swift this always fails with an XML response error.
    public Task<LyricsList> ParseLyricsAsync(string relFilePath) =>
        Task.FromException<LyricsList>(new ResponseError(ResponseErrorType.Xml));
}
