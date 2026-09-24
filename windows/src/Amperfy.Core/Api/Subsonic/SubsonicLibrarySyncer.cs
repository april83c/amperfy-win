using System.Runtime.ExceptionServices;

namespace Amperfy.Core.Api.Subsonic;

/// Library synchronisation with a Subsonic server (port of SubsonicLibrarySyncer.swift).
/// All methods run on the main thread. Network requests are awaited, the first "IDs" parser pass
/// runs on the thread pool, prefetching + the real parser run on the main thread and are
/// followed by a save (Swift: storage.async.perform).
public sealed class SubsonicLibrarySyncer : CommonLibrarySyncer, ILibrarySyncer
{
    private const string LogCategory = "LibrarySyncer";
    private const int MaxItemCountToPollAtOnce = 500;

    private readonly SubsonicServerApi _subsonicServerApi;

    public SubsonicLibrarySyncer(SubsonicServerApi subsonicServerApi, Account account, INetworkMonitor networkMonitor, LibraryStorage library, EventLogger eventLogger)
        : base(account, networkMonitor, library, eventLogger)
    {
        _subsonicServerApi = subsonicServerApi;
    }

    // --- storage helpers ----------------------------------------------------------------------

    /// Port of storage.async.perform: runs the body and saves the context afterwards. Like the
    /// Swift implementation, errors thrown inside the body are swallowed (logged only).
    private void Perform(Action body)
    {
        try
        {
            body();
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(LogCategory, $"Storage operation failed: {ex.Message}");
        }
        Library.SaveContext();
    }

    /// Port of storage.async.performAndGet: saves the context and rethrows errors of the body.
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

    /// First parser pass (ids only, no storage access) on the thread pool.
    private static Task<PrefetchIdContainer> ParseIdsAsync(ApiDataResponse response, bool isThrowingErrorsAllowed = false) =>
        Task.Run(() =>
        {
            var idParserDelegate = new SsIDsParserDelegate();
            idParserDelegate.Parse(response.Data);
            if (isThrowingErrorsAllowed && idParserDelegate.ParserError is not null) throw new ResponseError(ResponseErrorType.Xml, data: response.Data);
            if (isThrowingErrorsAllowed && idParserDelegate.Error is { SubsonicError: not null } subsonicError) throw subsonicError.CreateResponseError(null, response.Data);
            return idParserDelegate.PrefetchIDs;
        });

    /// IDs pass, prefetch and body (with the prefetched elements) followed by a save; errors of the
    /// body are swallowed like in Swift's storage.async.perform.
    private async Task PerformWithPrefetchAsync(ApiDataResponse response, Action<PrefetchElementContainer> body)
    {
        PrefetchIdContainer prefetchIds;
        try
        {
            prefetchIds = await ParseIdsAsync(response);
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(LogCategory, $"ID parsing failed: {ex.Message}");
            Library.SaveContext();
            return;
        }
        Perform(() => body(Library.GetElements(Account, prefetchIds)));
    }

    private static Exception Rethrow(Exception error)
    {
        ExceptionDispatchInfo.Capture(error).Throw();
        return error;
    }

    // --- sync ---------------------------------------------------------------------------------

    public async Task SyncInitialAsync(ISyncCallbacks? statusNotifier)
    {
        CreateCachedItemRepresentations(statusNotifier);

        statusNotifier?.NotifySyncStarted(ParsedObjectType.Genre, 0);
        var genreResponse = await _subsonicServerApi.RequestGenresAsync();
        await PerformWithPrefetchAsync(genreResponse, prefetch =>
        {
            var parserDelegate = new SsGenreParserDelegate(prefetch, Account, Library, statusNotifier);
            Parse(genreResponse, parserDelegate, isThrowingErrorsAllowed: false);
        });

        statusNotifier?.NotifySyncStarted(ParsedObjectType.Artist, 0);
        var artistsResponse = await _subsonicServerApi.RequestArtistsAsync();
        await PerformWithPrefetchAsync(artistsResponse, prefetch =>
        {
            var parserDelegate = new SsArtistParserDelegate(prefetch, Account, Library, statusNotifier);
            Parse(artistsResponse, parserDelegate, isThrowingErrorsAllowed: false);
        });

        var artists = Library.GetArtists(Account).Where(a => !string.IsNullOrEmpty(a.Id)).ToList();
        var albumCount = artists.Sum(a => a.RemoteAlbumCount);
        var pollCountArtist = Math.Max(1, (int)Math.Ceiling((double)albumCount / MaxItemCountToPollAtOnce));
        statusNotifier?.NotifySyncStarted(ParsedObjectType.Album, pollCountArtist);
        var albumTasks = Enumerable.Range(0, pollCountArtist + 1).Select(async index =>
        {
            var albumsResponse = await _subsonicServerApi.RequestAlbumsAsync(index * MaxItemCountToPollAtOnce, MaxItemCountToPollAtOnce);
            await PerformWithPrefetchAsync(albumsResponse, prefetch =>
            {
                var parserDelegate = new SsAlbumParserDelegate(prefetch, Account, Library, statusNotifier);
                Parse(albumsResponse, parserDelegate, isThrowingErrorsAllowed: false);
            });
            statusNotifier?.NotifyParsedObject(ParsedObjectType.Album);
        }).ToList();
        await Task.WhenAll(albumTasks);

        Perform(() =>
        {
            // Delete duplicated artists due to concurrence
            var allArtists = Library.GetArtists(Account);
            var uniqueArtists = new Dictionary<string, Artist>();
            foreach (var artist in allArtists)
            {
                // artists without id (created from song metadata) are identified by name -> keep them
                if (string.IsNullOrEmpty(artist.Id)) continue;
                if (uniqueArtists.TryGetValue(artist.Id, out var uniqueArtist))
                {
                    foreach (var album in artist.AlbumsRaw.ToList()) album.Artist = uniqueArtist;
                    AmperfyLog.Info(LogCategory, $"Delete multiple Artist <{artist.Name}> with id {artist.Id}");
                    Library.DeleteArtist(artist);
                }
                else
                {
                    uniqueArtists[artist.Id] = artist;
                }
            }
            // Delete duplicated albums due to concurrence
            var albums = Library.GetAlbums(Account);
            var uniqueAlbums = new Dictionary<string, Album>();
            foreach (var album in albums)
            {
                if (uniqueAlbums.ContainsKey(album.Id)) Library.DeleteAlbum(album);
                else uniqueAlbums[album.Id] = album;
            }
        });

        statusNotifier?.NotifySyncStarted(ParsedObjectType.Playlist, 0);
        var playlistsResponse = await _subsonicServerApi.RequestPlaylistsAsync();
        Perform(() =>
        {
            var parserDelegate = new SsPlaylistParserDelegate(Account, Library);
            Parse(playlistsResponse, parserDelegate, isThrowingErrorsAllowed: false);
        });

        var isSupported = await _subsonicServerApi.RequestServerPodcastSupportAsync();
        if (!isSupported) return;
        statusNotifier?.NotifySyncStarted(ParsedObjectType.Podcast, 0);
        var podcastsResponse = await _subsonicServerApi.RequestPodcastsAsync();
        await PerformWithPrefetchAsync(podcastsResponse, prefetch =>
        {
            var parserDelegate = new SsPodcastParserDelegate(prefetch, Account, Library, statusNotifier);
            Parse(podcastsResponse, parserDelegate, isThrowingErrorsAllowed: false);
            parserDelegate.PerformPostParseOperations();
        });
    }

    public async Task SyncAsync(Genre genre)
    {
        if (!IsSyncAllowed) return;
        await Task.WhenAll(genre.AlbumsRaw.ToList().Select(album => SyncAsync(album)));
    }

    public async Task SyncAsync(Artist artist)
    {
        if (!IsSyncAllowed || string.IsNullOrEmpty(artist.Id) || artist.RemoteStatus == RemoteStatus.Deleted) return;

        Exception HandleNotAvailableArtist(Exception error)
        {
            Perform(() =>
            {
                if (error is ResponseError responseError && responseError.AsSubsonicError() is { } subsonicError && !subsonicError.IsRemoteAvailable())
                {
                    var reportError = new ResponseError(ResponseErrorType.Resource, responseError.StatusCode,
                        $"Artist \"{artist.Name}\" is no longer available on the server.", responseError.CleansedUrl, responseError.ResponseData);
                    artist.RemoteStatus = RemoteStatus.Deleted;
                    throw reportError;
                }
            });
            return Rethrow(error);
        }

        ApiDataResponse artistResponse;
        try
        {
            artistResponse = await _subsonicServerApi.RequestArtistAsync(artist.Id);
        }
        catch (Exception error)
        {
            throw HandleNotAvailableArtist(error);
        }

        await PerformWithPrefetchAsync(artistResponse, prefetch =>
        {
            var parserDelegate = new SsArtistParserDelegate(prefetch, Account, Library);
            Parse(artistResponse, parserDelegate);
        });

        if (artist.RemoteStatus != RemoteStatus.Available) return;
        await Task.WhenAll(artist.AlbumsRaw.ToList().Select(album => SyncAsync(album)));
    }

    public async Task SyncAsync(Album album)
    {
        if (!IsSyncAllowed || album.RemoteStatus == RemoteStatus.Deleted) return;

        Exception HandleNotAvailableAlbum(Exception error)
        {
            Perform(() =>
            {
                if (error is ResponseError responseError && responseError.AsSubsonicError() is { } subsonicError && !subsonicError.IsRemoteAvailable())
                {
                    var reportError = new ResponseError(ResponseErrorType.Resource, responseError.StatusCode,
                        $"Album \"{album.Name}\" is no longer available on the server.", responseError.CleansedUrl, responseError.ResponseData);
                    album.MarkAsRemoteDeleted();
                    throw reportError;
                }
            });
            return Rethrow(error);
        }

        ApiDataResponse albumResponse;
        try
        {
            albumResponse = await _subsonicServerApi.RequestAlbumAsync(album.Id);
        }
        catch (Exception error)
        {
            throw HandleNotAvailableAlbum(error);
        }

        await PerformWithPrefetchAsync(albumResponse, prefetch =>
        {
            var parserDelegate = new SsAlbumParserDelegate(prefetch, Account, Library);
            Parse(albumResponse, parserDelegate);
        });

        if (album.RemoteStatus != RemoteStatus.Available) return;
        await PerformWithPrefetchAsync(albumResponse, prefetch =>
        {
            var oldSongs = album.SongsRaw.ToHashSet();
            var parserDelegate = new SsSongParserDelegate(prefetch, Account, Library);
            Parse(albumResponse, parserDelegate);
            oldSongs.ExceptWith(parserDelegate.ParsedSongs);
            foreach (var song in oldSongs)
            {
                AmperfyLog.Info(LogCategory, $"Song <{song.DisplayString}> is remote deleted");
                song.RemoteStatus = RemoteStatus.Deleted;
                album.SongsRaw.Remove(song);
            }
            album.IsCached = parserDelegate.IsCollectionCached;
            album.IsSongsMetaDataSynced = true;
        });
    }

    public async Task SyncAsync(Song song)
    {
        if (!IsSyncAllowed) return;
        var response = await _subsonicServerApi.RequestSongInfoAsync(song.Id);
        await PerformWithPrefetchAsync(response, prefetch =>
        {
            var parserDelegate = new SsSongParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
        });
        await SyncLyricsAsync(song);
    }

    private async Task SyncLyricsAsync(Song song)
    {
        try
        {
            var isSupported = await _subsonicServerApi.IsOpenSubsonicExtensionSupportedAsync(OpenSubsonicExtension.SongLyrics);
            if (!isSupported) return;
            var response = await _subsonicServerApi.RequestLyricsBySongIdAsync(song.Id);
            Perform(() =>
            {
                if (string.IsNullOrEmpty(song.Id) || song.Account is not { } songAccount) return;
                var lyricsRelFilePath = CacheFileManager.GetRelLyricsFilePath(songAccount.Info, song.Id);

                var parserDelegate = new SsLyricsParserDelegate();
                Parse(response, parserDelegate, isThrowingErrorsAllowed: false);
                // save xml response only if it contains valid lyrics
                if ((parserDelegate.LyricsList?.Lyrics.Count ?? 0) > 0)
                {
                    try
                    {
                        FileManager.WriteDataIntoCache(response.Data, lyricsRelFilePath, Account.Info);
                        song.LyricsRelFilePath = lyricsRelFilePath;
                        AmperfyLog.Info(LogCategory, $"Lyrics found for <{song.DisplayString}> and saved to: {lyricsRelFilePath}");
                    }
                    catch (Exception)
                    {
                        song.LyricsRelFilePath = null;
                    }
                }
                else
                {
                    AmperfyLog.Info(LogCategory, $"No lyrics available for <{song.DisplayString}>");
                }
            });
        }
        catch (Exception)
        {
            // do nothing
        }
    }

    public async Task SyncAsync(Podcast podcast)
    {
        if (!IsSyncAllowed || podcast.RemoteStatus == RemoteStatus.Deleted) return;
        var isSupported = await _subsonicServerApi.RequestServerPodcastSupportAsync();
        if (!isSupported) return;

        Exception HandleNotAvailablePodcast(Exception error)
        {
            Perform(() =>
            {
                if (error is ResponseError responseError && responseError.AsSubsonicError() is { } subsonicError && !subsonicError.IsRemoteAvailable())
                {
                    var reportError = new ResponseError(ResponseErrorType.Resource, responseError.StatusCode,
                        $"Podcast \"{podcast.Name}\" is no longer available on the server.", responseError.CleansedUrl, responseError.ResponseData);
                    podcast.RemoteStatus = RemoteStatus.Deleted;
                    throw reportError;
                }
            });
            return Rethrow(error);
        }

        ApiDataResponse podcastResponse;
        try
        {
            podcastResponse = await _subsonicServerApi.RequestPodcastEpisodesAsync(podcast.Id);
        }
        catch (Exception error)
        {
            throw HandleNotAvailablePodcast(error);
        }

        await PerformWithPrefetchAsync(podcastResponse, prefetch =>
        {
            var oldEpisodes = podcast.EpisodesRaw.ToHashSet();
            var parserDelegate = new SsPodcastEpisodeParserDelegate(podcast, prefetch, Account, Library);
            Parse(podcastResponse, parserDelegate);
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
        var isSupported = await _subsonicServerApi.RequestServerPodcastSupportAsync();
        if (!isSupported) return;
        await SyncDownPodcastsWithoutEpisodesAsync();
        var response = await _subsonicServerApi.RequestNewestPodcastsAsync();
        await PerformWithPrefetchAsync(response, prefetch =>
        {
            var parserDelegate = new SsPodcastEpisodeParserDelegate(null, prefetch, Account, Library);
            Parse(response, parserDelegate);
            parserDelegate.PerformPostParseOperations();
        });
    }

    public async Task SyncNewestAlbumsAsync(int offset, int count)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Sync newest albums: offset: {offset} count: {count}");
        var response = await _subsonicServerApi.RequestNewestAlbumsAsync(offset, count);
        await PerformWithPrefetchAsync(response, prefetch =>
        {
            var parserDelegate = new SsAlbumParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
            var oldNewestAlbums = Library.GetNewestAlbums(Account, offset, count);
            foreach (var album in oldNewestAlbums) album.MarkAsNotNewAnymore();
            for (var index = 0; index < parserDelegate.ParsedAlbums.Count; index++)
            {
                parserDelegate.ParsedAlbums[index].UpdateIsNewestInfo(index + 1 + offset);
            }
        });
    }

    public async Task SyncRecentAlbumsAsync(int offset, int count)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Sync recent albums: offset: {offset} count: {count}");
        var response = await _subsonicServerApi.RequestRecentAlbumsAsync(offset, count);
        await PerformWithPrefetchAsync(response, prefetch =>
        {
            var parserDelegate = new SsAlbumParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
            var oldRecentAlbums = Library.GetRecentAlbums(Account, offset, count);
            foreach (var album in oldRecentAlbums) album.MarkAsNotRecentAnymore();
            for (var index = 0; index < parserDelegate.ParsedAlbums.Count; index++)
            {
                parserDelegate.ParsedAlbums[index].UpdateIsRecentInfo(index + 1 + offset);
            }
        });
    }

    public async Task SyncFavoriteLibraryElementsAsync()
    {
        if (!IsSyncAllowed) return;
        var response = await _subsonicServerApi.RequestFavoriteElementsAsync();
        var prefetchIds = await ParseIdsAsync(response);
        Perform(() =>
        {
            AmperfyLog.Info(LogCategory, "Sync favorite artists");
            var oldFavoriteArtists = Library.GetFavoriteArtists(Account).ToHashSet();
            var prefetch = Library.GetElements(Account, prefetchIds);
            var parserDelegateArtist = new SsArtistParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegateArtist);
            oldFavoriteArtists.ExceptWith(parserDelegateArtist.ParsedArtists);
            foreach (var artist in oldFavoriteArtists)
            {
                artist.IsFavorite = false;
                artist.StarredDate = null;
            }

            AmperfyLog.Info(LogCategory, "Sync favorite albums");
            var oldFavoriteAlbums = Library.GetFavoriteAlbums(Account).ToHashSet();
            var parserDelegateAlbum = new SsAlbumParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegateAlbum);
            oldFavoriteAlbums.ExceptWith(parserDelegateAlbum.ParsedAlbums);
            foreach (var album in oldFavoriteAlbums)
            {
                album.IsFavorite = false;
                album.StarredDate = null;
            }

            AmperfyLog.Info(LogCategory, "Sync favorite songs");
            var oldFavoriteSongs = Library.GetFavoriteSongs(Account).ToHashSet();
            var parserDelegateSong = new SsSongParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegateSong);
            oldFavoriteSongs.ExceptWith(parserDelegateSong.ParsedSongs);
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
        var response = await _subsonicServerApi.RequestRadiosAsync();
        var prefetchIds = await ParseIdsAsync(response);
        Perform(() =>
        {
            var oldRadios = Library.GetRadios(Account).ToHashSet();
            var prefetch = Library.GetElements(Account, prefetchIds);
            var parserDelegate = new SsRadioParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);

            oldRadios.ExceptWith(parserDelegate.ParsedRadios);
            foreach (var radio in oldRadios)
            {
                AmperfyLog.Info(LogCategory, $"Radio <{radio.DisplayString}> is remote deleted");
                radio.RemoteStatus = RemoteStatus.Deleted;
            }
        });
    }

    public async Task SyncMusicFoldersAsync()
    {
        if (!IsSyncAllowed) return;
        var response = await _subsonicServerApi.RequestMusicFoldersAsync();
        await PerformWithPrefetchAsync(response, prefetch =>
        {
            var parserDelegate = new SsMusicFolderParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
        });
    }

    public async Task SyncIndexesAsync(MusicFolder musicFolder)
    {
        if (!IsSyncAllowed) return;
        var response = await _subsonicServerApi.RequestIndexesAsync(musicFolder.Id);
        await PerformWithPrefetchAsync(response, prefetch =>
        {
            var parserDelegate = new SsDirectoryParserDelegate(musicFolder, prefetch, Account, Library);
            Parse(response, parserDelegate);
            musicFolder.IsCached = parserDelegate.IsCollectionCached;
        });
    }

    public async Task SyncAsync(MusicDirectory directory)
    {
        if (!IsSyncAllowed) return;
        var response = await _subsonicServerApi.RequestMusicDirectoryAsync(directory.Id);
        await PerformWithPrefetchAsync(response, prefetch =>
        {
            var parserDelegate = new SsDirectoryParserDelegate(directory, prefetch, Account, Library);
            Parse(response, parserDelegate);
            directory.IsCached = parserDelegate.IsCollectionCached;
        });
    }

    public async Task RequestRandomSongsAsync(Playlist playlist, int count)
    {
        if (!IsSyncAllowed) return;
        var response = await _subsonicServerApi.RequestRandomSongsAsync(count);
        await PerformWithPrefetchAsync(response, prefetch =>
        {
            var parserDelegate = new SsSongParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
            playlist.Append(parserDelegate.ParsedSongs.Cast<AbstractPlayable>().ToList());
        });
    }

    public async Task<List<Song>> RequestSimilarSongsAsync(Song song, int count)
    {
        if (!IsSyncAllowed) return [];
        var response = await _subsonicServerApi.RequestSimilarSongsAsync(song.Id, count);
        var prefetchIds = await ParseIdsAsync(response);
        return PerformAndGet(() =>
        {
            var prefetch = Library.GetElements(Account, prefetchIds);
            var parserDelegate = new SsSongParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
            return parserDelegate.ParsedSongs.ToList();
        });
    }

    public async Task RequestPodcastEpisodeDeleteAsync(PodcastEpisode podcastEpisode)
    {
        if (!IsSyncAllowed) return;
        var response = await _subsonicServerApi.RequestPodcastEpisodeDeleteAsync(podcastEpisode.Id);
        ParseForError(response);
    }

    public async Task SyncDownPlaylistsWithoutSongsAsync()
    {
        if (!IsSyncAllowed) return;
        var response = await _subsonicServerApi.RequestPlaylistsAsync();
        Perform(() =>
        {
            var parserDelegate = new SsPlaylistParserDelegate(Account, Library);
            Parse(response, parserDelegate);
        });
    }

    public async Task SyncDownAsync(Playlist playlist)
    {
        if (!IsSyncAllowed || playlist.Id == "") return;
        AmperfyLog.Info(LogCategory, $"Playlist \"{playlist.Name}\": Download songs from server");
        var response = await _subsonicServerApi.RequestPlaylistSongsAsync(playlist.Id);

        AmperfyLog.Info(LogCategory, $"Playlist \"{playlist.Name}\": Parse songs start");
        PrefetchIdContainer prefetchIds;
        try
        {
            prefetchIds = await ParseIdsAsync(response, isThrowingErrorsAllowed: true);
        }
        catch (Exception ex)
        {
            // Swift: the error is thrown inside storage.async.perform and therefore swallowed
            AmperfyLog.Warning(LogCategory, $"Playlist \"{playlist.Name}\": {ex.Message}");
            Library.SaveContext();
            return;
        }
        Perform(() =>
        {
            var prefetch = Library.GetElements(Account, prefetchIds);
            var parserDelegate = new SsPlaylistSongsParserDelegate(playlist, Account, Library, prefetch);
            Parse(response, parserDelegate);
            playlist.IsCached = parserDelegate.IsCollectionCached;
            AmperfyLog.Info(LogCategory, $"Playlist \"{playlist.Name}\": Parse songs ({parserDelegate.ParsedCount}) done");
        });
    }

    private async Task ValidatePlaylistIdAsync(Playlist playlist)
    {
        if (playlist.Id == "") await CreatePlaylistRemoteAsync(playlist);
        if (playlist.Id == "")
        {
            AmperfyLog.Info(LogCategory, "Playlist id was not assigned after creation");
            throw new BackendError(BackendErrorKind.IncorrectServerBehavior, "Playlist id was not assigned after creation");
        }
    }

    public async Task SyncUploadPlaylistNameAsync(Playlist playlist)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Upload name on playlist to: \"{playlist.Name}\"");
        await ValidatePlaylistIdAsync(playlist);
        var response = await _subsonicServerApi.RequestPlaylistUpdateAsync(playlist.Id, playlist.Name, [], []);
        ParseForError(response);
    }

    public async Task SyncUploadPlaylistAddSongsAsync(Playlist playlist, IReadOnlyList<Song> songs)
    {
        if (!IsSyncAllowed || songs.Count == 0) return;
        AmperfyLog.Info(LogCategory, $"Upload SongsAdded on playlist \"{playlist.Name}\"");
        await ValidatePlaylistIdAsync(playlist);
        var response = await _subsonicServerApi.RequestPlaylistUpdateAsync(playlist.Id, playlist.Name, [], songs.Select(s => s.Id).ToList());
        ParseForError(response);
    }

    public async Task SyncUploadPlaylistDeleteSongAsync(Playlist playlist, int index)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Upload SongDelete on playlist \"{playlist.Name}\" at index: {index}");
        await ValidatePlaylistIdAsync(playlist);
        var response = await _subsonicServerApi.RequestPlaylistUpdateAsync(playlist.Id, playlist.Name, [index], []);
        ParseForError(response);
    }

    public async Task SyncUploadPlaylistOrderAsync(Playlist playlist)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Upload OrderChange on playlist \"{playlist.Name}\"");
        await ValidatePlaylistIdAsync(playlist);
        var songIdsToAdd = playlist.Playables.Select(p => p.Id).ToList();
        var songIndicesToRemove = Enumerable.Range(0, songIdsToAdd.Count).ToList();
        var response = await _subsonicServerApi.RequestPlaylistUpdateAsync(playlist.Id, playlist.Name, songIndicesToRemove, songIdsToAdd);
        ParseForError(response);
    }

    public async Task SyncUploadPlaylistDeleteAsync(string playlistId)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Upload Delete playlist \"{playlistId}\"");
        var response = await _subsonicServerApi.RequestPlaylistDeleteAsync(playlistId);
        ParseForError(response);
    }

    public async Task SyncDownPodcastsWithoutEpisodesAsync()
    {
        if (!IsSyncAllowed) return;
        var isSupported = await _subsonicServerApi.RequestServerPodcastSupportAsync();
        if (!isSupported) return;

        var response = await _subsonicServerApi.RequestPodcastsAsync();
        var prefetchIds = await ParseIdsAsync(response);
        Perform(() =>
        {
            var oldPodcasts = Library.GetRemoteAvailablePodcasts(Account).ToHashSet();
            var prefetch = Library.GetElements(Account, prefetchIds);
            var parserDelegate = new SsPodcastParserDelegate(prefetch, Account, Library);
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

    public async Task SyncNowPlayingAsync(Song song, NowPlayingSongPosition songPosition)
    {
        if (!IsSyncAllowed) return;
        switch (songPosition)
        {
            case NowPlayingSongPosition.Start:
                await ScrobbleAsync(song, submission: false);
                break;
            case NowPlayingSongPosition.End:
                await ScrobbleAsync(song, submission: true);
                break;
        }
    }

    public Task ScrobbleAsync(Song song, DateTime? date) => ScrobbleAsync(song, submission: true, date);

    private async Task ScrobbleAsync(Song song, bool submission, DateTime? date = null)
    {
        if (!IsSyncAllowed) return;
        if (!submission) AmperfyLog.Info(LogCategory, $"Now Playing Begin: {song.DisplayString}");
        else if (date is { } d) AmperfyLog.Info(LogCategory, $"Scrobbled at {d.AsIso8601String()}: {song.DisplayString}");
        else AmperfyLog.Info(LogCategory, $"Now Playing End (Scrobble): {song.DisplayString}");
        var response = await _subsonicServerApi.RequestScrobbleAsync(song.Id, submission, date);
        ParseForError(response);
    }

    public async Task SetRatingAsync(Song song, int rating)
    {
        if (!IsSyncAllowed || rating < 0 || rating > 5) return;
        AmperfyLog.Info(LogCategory, $"Rate {rating} stars: {song.DisplayString}");
        var response = await _subsonicServerApi.RequestRatingAsync(song.Id, rating);
        ParseForError(response);
    }

    public async Task SetRatingAsync(Album album, int rating)
    {
        if (!IsSyncAllowed || rating < 0 || rating > 5) return;
        AmperfyLog.Info(LogCategory, $"Rate {rating} stars: {album.Name}");
        var response = await _subsonicServerApi.RequestRatingAsync(album.Id, rating);
        ParseForError(response);
    }

    public async Task SetRatingAsync(Artist artist, int rating)
    {
        if (!IsSyncAllowed || rating < 0 || rating > 5) return;
        AmperfyLog.Info(LogCategory, $"Rate {rating} stars: {artist.Name}");
        var response = await _subsonicServerApi.RequestRatingAsync(artist.Id, rating);
        ParseForError(response);
    }

    public async Task SetFavoriteAsync(Song song, bool isFavorite)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Set Favorite {(isFavorite ? "TRUE" : "FALSE")}: {song.DisplayString}");
        var response = await _subsonicServerApi.RequestSetFavoriteSongAsync(song.Id, isFavorite);
        ParseForError(response);
    }

    public async Task SetFavoriteAsync(Album album, bool isFavorite)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Set Favorite {(isFavorite ? "TRUE" : "FALSE")}: {album.Name}");
        var response = await _subsonicServerApi.RequestSetFavoriteAlbumAsync(album.Id, isFavorite);
        ParseForError(response);
    }

    public async Task SetFavoriteAsync(Artist artist, bool isFavorite)
    {
        if (!IsSyncAllowed) return;
        AmperfyLog.Info(LogCategory, $"Set Favorite {(isFavorite ? "TRUE" : "FALSE")}: {artist.Name}");
        var response = await _subsonicServerApi.RequestSetFavoriteArtistAsync(artist.Id, isFavorite);
        ParseForError(response);
    }

    public async Task SearchArtistsAsync(string searchText)
    {
        if (!IsSyncAllowed || string.IsNullOrEmpty(searchText)) return;
        AmperfyLog.Info(LogCategory, $"Search artists via API: \"{searchText}\"");
        var response = await _subsonicServerApi.RequestSearchArtistsAsync(searchText);
        await PerformWithPrefetchAsync(response, prefetch =>
        {
            var parserDelegate = new SsArtistParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
        });
    }

    public async Task SearchAlbumsAsync(string searchText)
    {
        if (!IsSyncAllowed || string.IsNullOrEmpty(searchText)) return;
        AmperfyLog.Info(LogCategory, $"Search albums via API: \"{searchText}\"");
        var response = await _subsonicServerApi.RequestSearchAlbumsAsync(searchText);
        await PerformWithPrefetchAsync(response, prefetch =>
        {
            var parserDelegate = new SsAlbumParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
        });
    }

    public async Task SearchSongsAsync(string searchText)
    {
        if (!IsSyncAllowed || string.IsNullOrEmpty(searchText)) return;
        AmperfyLog.Info(LogCategory, $"Search songs via API: \"{searchText}\"");
        var response = await _subsonicServerApi.RequestSearchSongsAsync(searchText);
        await PerformWithPrefetchAsync(response, prefetch =>
        {
            var parserDelegate = new SsSongParserDelegate(prefetch, Account, Library);
            Parse(response, parserDelegate);
        });
    }

    public async Task<LyricsList> ParseLyricsAsync(string relFilePath)
    {
        var parserDelegate = new SsLyricsParserDelegate();
        var absFilePath = FileManager.GetAbsolutePath(relFilePath);
        byte[] data;
        try
        {
            data = await File.ReadAllBytesAsync(absFilePath);
        }
        catch (Exception)
        {
            throw new ResponseError(ResponseErrorType.Xml);
        }
        parserDelegate.Parse(data);
        return parserDelegate.LyricsList ?? throw new ResponseError(ResponseErrorType.Xml);
    }

    private async Task CreatePlaylistRemoteAsync(Playlist playlist)
    {
        AmperfyLog.Info(LogCategory, "Create playlist on server");
        var response = await _subsonicServerApi.RequestPlaylistCreateAsync(playlist.Name);
        await PerformWithPrefetchAsync(response, prefetch =>
        {
            var parserDelegate = new SsPlaylistSongsParserDelegate(playlist, Account, Library, prefetch);
            Parse(response, parserDelegate);
        });
        // Old api version -> need to match the created playlist via name
        if (playlist.Id == "") await UpdatePlaylistIdViaItsNameAsync(playlist);
    }

    private async Task UpdatePlaylistIdViaItsNameAsync(Playlist playlist)
    {
        await SyncDownPlaylistsWithoutSongsAsync();
        Perform(() =>
        {
            var playlists = Library.GetPlaylists(Account);
            var firstMatch = playlists.FirstOrDefault(p => p.Name == playlist.Name && p.Id != "");
            if (firstMatch is null) return;
            var matchedId = firstMatch.Id;
            Library.DeletePlaylist(firstMatch);
            playlist.Id = matchedId;
        });
    }

    private void ParseForError(ApiDataResponse response)
    {
        var parserDelegate = new SsPingParserDelegate();
        Parse(response, parserDelegate);
    }

    private void Parse(ApiDataResponse response, SsXmlParser parserDelegate, bool isThrowingErrorsAllowed = true)
    {
        var success = parserDelegate.Parse(response.Data);
        if (!success && parserDelegate.ParserError is { } error && isThrowingErrorsAllowed)
        {
            AmperfyLog.Error(LogCategory, $"Error during response parsing: {error.Message}");
            throw new ResponseError(ResponseErrorType.Xml,
                cleansedUrl: response.Url is null ? null : _subsonicServerApi.Cleanse(response.Url), data: response.Data);
        }
        if (parserDelegate.Error is { SubsonicError: not null } subsonicError && isThrowingErrorsAllowed)
        {
            throw subsonicError.CreateResponseError(response.Url is null ? null : _subsonicServerApi.Cleanse(response.Url), response.Data);
        }
    }
}
