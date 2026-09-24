using Microsoft.EntityFrameworkCore;

namespace Amperfy.Core.Storage;

public sealed partial class LibraryStorage
{
    // --- base queries --------------------------------------------------------------------------

    /// Songs that are available to the user: available on the server (size &gt; 0 and album
    /// available) or cached. See Song.IsAvailableToUser.
    public IQueryable<Song> AvailableSongs() =>
        Context.Songs.Where(s => (s.Size > 0 && s.Album != null && s.Album.RemoteStatus == RemoteStatus.Available) || s.RelFilePath != null);

    /// Playlists that are not player queues.
    public IQueryable<Playlist> UserPlaylists() =>
        Context.Playlists.Where(p => !Context.PlayerStates.Any(s =>
            s.ContextPlaylistPk == p.Pk || s.ShuffledContextPlaylistPk == p.Pk || s.UserQueuePlaylistPk == p.Pk || s.PodcastPlaylistPk == p.Pk));

    public static IQueryable<T> SortedByIdentifier<T>(IQueryable<T> q) where T : AbstractLibraryEntity => q switch
    {
        IQueryable<Album> a => (IQueryable<T>)a.OrderBy(x => x.NameRaw).ThenBy(x => x.Id),
        IQueryable<Artist> a => (IQueryable<T>)a.OrderBy(x => x.NameRaw).ThenBy(x => x.Id),
        IQueryable<Genre> a => (IQueryable<T>)a.OrderBy(x => x.NameRaw).ThenBy(x => x.Id),
        IQueryable<Podcast> a => (IQueryable<T>)a.OrderBy(x => x.TitleRaw).ThenBy(x => x.Id),
        IQueryable<MusicDirectory> a => (IQueryable<T>)a.OrderBy(x => x.NameRaw).ThenBy(x => x.Id),
        IQueryable<AbstractPlayable> a => (IQueryable<T>)a.OrderBy(x => x.TitleRaw).ThenBy(x => x.Id),
        _ => q.OrderBy(x => x.Id),
    };

    private static IQueryable<Song> SortSongsByTitle(IQueryable<Song> q) => q.OrderBy(x => x.TitleRaw).ThenBy(x => x.Id);
    private static IQueryable<PodcastEpisode> SortEpisodesByTitle(IQueryable<PodcastEpisode> q) => q.OrderBy(x => x.TitleRaw).ThenBy(x => x.Id);
    private static IQueryable<Radio> SortRadiosByTitle(IQueryable<Radio> q) => q.OrderBy(x => x.TitleRaw).ThenBy(x => x.Id);

    // --- genres --------------------------------------------------------------------------------

    public List<Genre> GetAllGenres() => Context.Genres.OrderBy(x => x.NameRaw).ThenBy(x => x.Id).ToList();
    public List<Genre> GetGenres(Account account) => Context.Genres.Where(x => x.AccountPk == account.Pk).OrderBy(x => x.NameRaw).ThenBy(x => x.Id).ToList();

    public List<Genre> GetRandomGenres(Account account, int count) =>
        Context.Genres.Where(x => x.AccountPk == account.Pk && x.RemoteStatus == RemoteStatus.Available)
            .OrderBy(_ => EF.Functions.Random()).Take(count).ToList();

    public Genre? GetGenre(Account account, string id) => Context.Genres.FirstOrDefault(x => x.AccountPk == account.Pk && x.Id == id);
    public Genre? GetGenreByName(Account account, string name) => Context.Genres.FirstOrDefault(x => x.AccountPk == account.Pk && x.NameRaw == name);

    // --- artists -------------------------------------------------------------------------------

    public List<Artist> GetAllArtists() => Context.Artists.OrderBy(x => x.NameRaw).ThenBy(x => x.Id).ToList();
    public List<Artist> GetArtists(Account account) => Context.Artists.Where(x => x.AccountPk == account.Pk).OrderBy(x => x.NameRaw).ThenBy(x => x.Id).ToList();

    public List<Artist> GetRandomArtists(Account account, int count, bool onlyCached)
    {
        var q = Context.Artists.Where(x => x.AccountPk == account.Pk);
        q = onlyCached
            ? q.Where(a => a.SongsRaw.Any(s => s.RelFilePath != null))
            : q.Where(a => a.RemoteStatus == RemoteStatus.Available || a.SongsRaw.Any(s => s.RelFilePath != null));
        return q.OrderBy(_ => EF.Functions.Random()).Take(count).ToList();
    }

    public List<Artist> GetFavoriteArtists(Account account) =>
        Context.Artists.Where(x => x.AccountPk == account.Pk && x.IsFavorite).OrderBy(x => x.NameRaw).ThenBy(x => x.Id).ToList();

    public List<Artist> GetAlbumArtists(Account account) =>
        Context.Artists.Where(x => x.AccountPk == account.Pk && x.AlbumsRaw.Any()).OrderBy(x => x.NameRaw).ThenBy(x => x.Id).ToList();

    public Artist? GetArtist(Account account, string id) => Context.Artists.FirstOrDefault(x => x.AccountPk == account.Pk && x.Id == id);

    /// Artist without server id identified by name (created from song metadata).
    public Artist? GetArtistLocal(Account account, string name) =>
        Context.Artists.FirstOrDefault(x => x.AccountPk == account.Pk && x.Id == "" && x.NameRaw == name);

    // --- albums --------------------------------------------------------------------------------

    public List<Album> GetAllAlbums() => Context.Albums.OrderBy(x => x.NameRaw).ThenBy(x => x.Id).ToList();
    public List<Album> GetAlbums(Account account) => Context.Albums.Where(x => x.AccountPk == account.Pk).OrderBy(x => x.NameRaw).ThenBy(x => x.Id).ToList();

    public List<Album> GetNewestAlbums(Account account, int offset = 0, int count = 50) =>
        Context.Albums.Where(x => x.AccountPk == account.Pk && x.NewestIndex > 0).OrderBy(x => x.NewestIndex).Skip(offset).Take(count).ToList();

    public List<Album> GetRecentAlbums(Account account, int offset = 0, int count = 50) =>
        Context.Albums.Where(x => x.AccountPk == account.Pk && x.RecentIndex > 0).OrderBy(x => x.RecentIndex).Skip(offset).Take(count).ToList();

    public List<Album> GetRandomAlbums(Account account, int count, bool onlyCached)
    {
        var q = Context.Albums.Where(x => x.AccountPk == account.Pk);
        q = onlyCached
            ? q.Where(a => a.SongsRaw.Any(s => s.RelFilePath != null))
            : q.Where(a => a.RemoteStatus == RemoteStatus.Available || a.SongsRaw.Any(s => s.RelFilePath != null));
        return q.OrderBy(_ => EF.Functions.Random()).Take(count).ToList();
    }

    public List<Album> GetFavoriteAlbums(Account account) =>
        Context.Albums.Where(x => x.AccountPk == account.Pk && x.IsFavorite).OrderBy(x => x.NameRaw).ThenBy(x => x.Id).ToList();

    /// Albums of the artist or albums which contain songs of the artist.
    public List<Album> GetAlbums(Account account, Artist artist, bool onlyCached = false)
    {
        var q = Context.Albums.Where(a => a.AccountPk == account.Pk && (a.ArtistPk == artist.Pk || a.SongsRaw.Any(s => s.ArtistPk == artist.Pk)));
        if (onlyCached) q = q.Where(a => a.SongsRaw.Any(s => s.RelFilePath != null));
        return q.OrderBy(x => x.NameRaw).ThenBy(x => x.Id).ToList();
    }

    public Album? GetAlbum(Account account, string id) => Context.Albums.FirstOrDefault(x => x.AccountPk == account.Pk && x.Id == id);

    public List<Album> GetAlbumsWithoutSyncedSongs() =>
        Context.Albums.Where(x => !x.IsSongsMetaDataSynced && x.RemoteStatus == RemoteStatus.Available).ToList();

    // --- podcasts ------------------------------------------------------------------------------

    public List<Podcast> GetAllPodcasts() => Context.Podcasts.OrderBy(x => x.TitleRaw).ThenBy(x => x.Id).ToList();
    public List<Podcast> GetPodcasts(Account account) => Context.Podcasts.Where(x => x.AccountPk == account.Pk).OrderBy(x => x.TitleRaw).ThenBy(x => x.Id).ToList();

    private IQueryable<PodcastEpisode> UserAvailableEpisodes() =>
        Context.PodcastEpisodes.Where(e => e.RelFilePath != null || e.PodcastStatus != PodcastEpisodeRemoteStatus.Deleted);

    public List<PodcastEpisode> GetNewestPodcastEpisodes(Account account, int count) =>
        UserAvailableEpisodes().Where(e => e.AccountPk == account.Pk).OrderByDescending(e => e.PublishDateRaw).Take(count).ToList();

    public List<PodcastEpisode> GetUserAvailableEpisodes(Podcast podcast) =>
        UserAvailableEpisodes().Where(e => e.PodcastPk == podcast.Pk).OrderByDescending(e => e.PublishDateRaw).ToList();

    public List<Podcast> GetRemoteAvailablePodcasts(Account account) =>
        Context.Podcasts.Where(p => p.AccountPk == account.Pk &&
                                    (p.RemoteStatus == RemoteStatus.Available || p.EpisodesRaw.Any(e => e.RelFilePath != null)))
            .OrderBy(x => x.TitleRaw).ThenBy(x => x.Id).ToList();

    public Podcast? GetPodcast(Account account, string id) => Context.Podcasts.FirstOrDefault(x => x.AccountPk == account.Pk && x.Id == id);

    public List<PodcastEpisode> GetAllPodcastEpisodes() => SortEpisodesByTitle(Context.PodcastEpisodes).ToList();
    public List<PodcastEpisode> GetPodcastEpisodes(Account account) => SortEpisodesByTitle(Context.PodcastEpisodes.Where(x => x.AccountPk == account.Pk)).ToList();

    public List<PodcastEpisode> GetCachedPodcastEpisodes(Account account) =>
        SortEpisodesByTitle(Context.PodcastEpisodes.Where(x => x.AccountPk == account.Pk && x.RelFilePath != null)).ToList();

    public PodcastEpisode? GetPodcastEpisode(Account account, string id) =>
        Context.PodcastEpisodes.FirstOrDefault(x => x.AccountPk == account.Pk && x.Id == id);

    // --- songs ---------------------------------------------------------------------------------

    public List<Song> GetAllSongs() => SortSongsByTitle(Context.Songs).ToList();
    public List<Song> GetSongs(Account account) => SortSongsByTitle(Context.Songs.Where(x => x.AccountPk == account.Pk)).ToList();

    /// Songs of the artist or songs on albums of the artist.
    public List<Song> GetSongs(Account account, Artist artist, bool onlyCached = false)
    {
        var q = AvailableSongs().Where(s => s.AccountPk == account.Pk && (s.ArtistPk == artist.Pk || (s.Album != null && s.Album.ArtistPk == artist.Pk)));
        if (onlyCached) q = q.Where(s => s.RelFilePath != null);
        return SortSongsByTitle(q).ToList();
    }

    public List<Song> GetCachedSongs(Account account) =>
        SortSongsByTitle(AvailableSongs().Where(x => x.AccountPk == account.Pk && x.RelFilePath != null)).ToList();

    public List<Song> GetRandomSongs(Account account, int count = 100, bool onlyCached = false)
    {
        var q = AvailableSongs().Where(x => x.AccountPk == account.Pk);
        if (onlyCached) q = q.Where(s => s.RelFilePath != null);
        return q.OrderBy(_ => EF.Functions.Random()).Take(count).ToList();
    }

    public List<Song> GetFavoriteSongs(Account account) =>
        SortSongsByTitle(AvailableSongs().Where(x => x.AccountPk == account.Pk && x.IsFavorite)).ToList();

    public List<Song> GetSongsForCompleteLibraryDownload(Account account) =>
        SortSongsByTitle(AvailableSongs().Where(x => x.AccountPk == account.Pk && x.RelFilePath == null && x.Download == null)).ToList();

    public Song? GetSong(Account account, string id) => Context.Songs.FirstOrDefault(x => x.AccountPk == account.Pk && x.Id == id);

    // --- radios --------------------------------------------------------------------------------

    public List<Radio> GetRadios(Account account) =>
        SortRadiosByTitle(Context.Radios.Where(x => x.AccountPk == account.Pk && x.RemoteStatus == RemoteStatus.Available)).ToList();

    public Radio? GetRadio(Account account, string id) => Context.Radios.FirstOrDefault(x => x.AccountPk == account.Pk && x.Id == id);

    // --- scrobble entries ----------------------------------------------------------------------

    public List<ScrobbleEntry> GetAllScrobbleEntries() => Context.ScrobbleEntries.ToList();
    public List<ScrobbleEntry> GetScrobbleEntries(Account account) => Context.ScrobbleEntries.Where(x => x.AccountPk == account.Pk).ToList();

    public ScrobbleEntry? GetFirstUploadableScrobbleEntry(Account account) =>
        Context.ScrobbleEntries.Where(x => x.AccountPk == account.Pk && !x.IsUploaded).OrderBy(x => x.Date).FirstOrDefault();

    // --- playlists -----------------------------------------------------------------------------

    public List<Playlist> GetAllPlaylists(bool areSystemPlaylistsIncluded = false) =>
        (areSystemPlaylistsIncluded ? Context.Playlists : UserPlaylists()).OrderBy(x => x.NameRaw).ThenBy(x => x.Id).ToList();

    public List<Playlist> GetPlaylists(Account account, bool areSystemPlaylistsIncluded = false) =>
        (areSystemPlaylistsIncluded ? Context.Playlists : UserPlaylists()).Where(x => x.AccountPk == account.Pk)
            .OrderBy(x => x.NameRaw).ThenBy(x => x.Id).ToList();

    public List<Playlist> GetRecentlyPlayedPlaylists(Account account, int count) =>
        UserPlaylists().Where(x => x.AccountPk == account.Pk && x.LastPlayedDate != null)
            .OrderByDescending(x => x.LastPlayedDate).Take(count).ToList();

    public List<PlaylistItem> GetAllPlaylistItems() => Context.PlaylistItems.ToList();

    public List<PlaylistItem> GetPlaylistItems(Playlist playlist) =>
        Context.PlaylistItems.Where(i => i.PlaylistPk == playlist.Pk).OrderBy(i => i.Order).ToList();

    public List<PlaylistItem> GetAllPlaylistItemOrphans() =>
        Context.PlaylistItems.Where(i => i.PlaylistPk == null || i.PlayablePk == null).OrderBy(i => i.Order).ToList();

    public Playlist? GetPlaylist(Account account, string id) => Context.Playlists.FirstOrDefault(x => x.AccountPk == account.Pk && x.Id == id);

    // --- music folders / directories -----------------------------------------------------------

    public List<MusicFolder> GetAllMusicFolders() => Context.MusicFolders.OrderBy(f => f.Name).ToList();
    public List<MusicFolder> GetMusicFolders(Account account) => Context.MusicFolders.Where(f => f.AccountPk == account.Pk).OrderBy(f => f.Name).ToList();
    public MusicFolder? GetMusicFolder(Account account, string id) => Context.MusicFolders.FirstOrDefault(x => x.AccountPk == account.Pk && x.Id == id);

    public List<MusicDirectory> GetAllDirectories() => Context.Directories.ToList();
    public MusicDirectory? GetDirectory(Account account, string id) => Context.Directories.FirstOrDefault(x => x.AccountPk == account.Pk && x.Id == id);

    // --- artworks ------------------------------------------------------------------------------

    public List<Artwork> GetAllArtworks() => Context.Artworks.ToList();
    public List<Artwork> GetArtworks(Account account) => Context.Artworks.Where(a => a.AccountPk == account.Pk).ToList();

    public Artwork? GetArtwork(Account account, Api.ArtworkRemoteInfo remoteInfo) =>
        Context.Artworks.FirstOrDefault(a => a.AccountPk == account.Pk && a.Id == remoteInfo.Id && a.Type == remoteInfo.Type);

    public List<Artwork> GetArtworksForCompleteLibraryDownload(Account account) =>
        Context.Artworks.Where(a => a.AccountPk == account.Pk && a.RelFilePath == null &&
                                    (a.Download == null || a.Download.ErrorDate != null) &&
                                    (a.Status == ImageStatus.NotChecked || a.Status == ImageStatus.FetchError)).ToList();

    public List<EmbeddedArtwork> GetAllEmbeddedArtworks() => Context.EmbeddedArtworks.ToList();
    public List<EmbeddedArtwork> GetEmbeddedArtworks(Account account) => Context.EmbeddedArtworks.Where(a => a.AccountPk == account.Pk).ToList();
    public EmbeddedArtwork? GetEmbeddedArtwork(AbstractPlayable owner) => Context.EmbeddedArtworks.FirstOrDefault(a => a.OwnerPk == owner.Pk);

    // --- log entries ---------------------------------------------------------------------------

    public List<LogEntry> GetAllLogEntries() => Context.LogEntries.OrderByDescending(e => e.CreationDate).ToList();

    public void DeleteAllLogEntries() => Context.LogEntries.ExecuteDelete();

    // --- player state --------------------------------------------------------------------------

    /// Returns the (single) persistent player state including its four queue playlists.
    public PlayerState GetPlayerState()
    {
        var states = Context.PlayerStates.ToList();
        PlayerState state;
        if (states.Count == 1)
        {
            state = states[0];
        }
        else
        {
            foreach (var s in states) Context.Remove(s);
            state = Context.CreateProxy<PlayerState>();
            Context.Add(state);
        }
        state.UserQueuePlaylist ??= CreatePlaylist(null);
        state.ContextPlaylist ??= CreatePlaylist(null);
        state.ShuffledContextPlaylist ??= CreatePlaylist(null);
        state.PodcastPlaylist ??= CreatePlaylist(null);
        SaveContext();
        return state;
    }

    // --- search --------------------------------------------------------------------------------

    public IQueryable<Artist> QueryArtists(Account account, string searchText, bool onlyCached, ArtistCategoryFilter displayFilter)
    {
        var q = Context.Artists.Where(a => a.AccountPk == account.Pk &&
                                           (a.RemoteStatus == RemoteStatus.Available || a.SongsRaw.Any(s => s.RelFilePath != null)));
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(a => a.NameRaw != null && EF.Functions.Like(a.NameRaw, $"%{EscapeLike(searchText)}%", "\\"));
        if (onlyCached) q = q.Where(a => a.SongsRaw.Any(s => s.RelFilePath != null));
        q = displayFilter switch
        {
            ArtistCategoryFilter.AlbumArtists => q.Where(a => a.AlbumsRaw.Any()),
            ArtistCategoryFilter.Favorites => q.Where(a => a.IsFavorite),
            _ => q,
        };
        return q;
    }

    public List<Artist> SearchArtists(Account account, string searchText, bool onlyCached, ArtistCategoryFilter displayFilter) =>
        QueryArtists(account, searchText, onlyCached, displayFilter).OrderBy(x => x.NameRaw).ThenBy(x => x.Id).ToList();

    public IQueryable<Album> QueryAlbums(Account account, string searchText, bool onlyCached, DisplayCategoryFilter displayFilter)
    {
        var q = Context.Albums.Where(a => a.AccountPk == account.Pk &&
                                          (a.RemoteStatus == RemoteStatus.Available || a.SongsRaw.Any(s => s.RelFilePath != null)));
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(a => a.NameRaw != null && EF.Functions.Like(a.NameRaw, $"%{EscapeLike(searchText)}%", "\\"));
        if (onlyCached) q = q.Where(a => a.SongsRaw.Any(s => s.RelFilePath != null));
        q = displayFilter switch
        {
            DisplayCategoryFilter.Newest => q.Where(a => a.NewestIndex > 0),
            DisplayCategoryFilter.Recent => q.Where(a => a.RecentIndex > 0),
            DisplayCategoryFilter.Favorites => q.Where(a => a.IsFavorite),
            _ => q,
        };
        return q;
    }

    public List<Album> SearchAlbums(Account account, string searchText, bool onlyCached, DisplayCategoryFilter displayFilter) =>
        QueryAlbums(account, searchText, onlyCached, displayFilter).OrderBy(x => x.NameRaw).ThenBy(x => x.Id).ToList();

    public IQueryable<Playlist> QueryPlaylists(Account account, string searchText, PlaylistSearchCategory category)
    {
        var q = UserPlaylists().Where(p => p.AccountPk == account.Pk);
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(p => p.NameRaw != null && EF.Functions.Like(p.NameRaw, $"%{EscapeLike(searchText)}%", "\\"));
        q = category switch
        {
            PlaylistSearchCategory.Cached => q.Where(p => p.ItemsRaw.Any(i => i.Playable != null && i.Playable.RelFilePath != null)),
            PlaylistSearchCategory.UserOnly => q.Where(p => !p.Id.StartsWith(Playlist.SmartPlaylistIdPrefix)),
            PlaylistSearchCategory.SmartOnly => q.Where(p => p.Id.StartsWith(Playlist.SmartPlaylistIdPrefix)),
            _ => q,
        };
        return q;
    }

    public List<Playlist> SearchPlaylists(Account account, string searchText, PlaylistSearchCategory category) =>
        QueryPlaylists(account, searchText, category).OrderBy(x => x.NameRaw).ThenBy(x => x.Id).ToList();

    public IQueryable<Radio> QueryRadios(Account account, string searchText)
    {
        var q = Context.Radios.Where(r => r.AccountPk == account.Pk && r.RemoteStatus == RemoteStatus.Available);
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(r => r.TitleRaw != null && EF.Functions.Like(r.TitleRaw, $"%{EscapeLike(searchText)}%", "\\"));
        return q;
    }

    public IQueryable<Song> QuerySongs(Account account, string searchText, bool onlyCached, DisplayCategoryFilter displayFilter)
    {
        var q = AvailableSongs().Where(s => s.AccountPk == account.Pk);
        if (!string.IsNullOrEmpty(searchText))
        {
            var pattern = $"%{EscapeLike(searchText)}%";
            q = q.Where(s => (s.TitleRaw != null && EF.Functions.Like(s.TitleRaw, pattern, "\\")) ||
                             (s.Artist != null && s.Artist.NameRaw != null && EF.Functions.Like(s.Artist.NameRaw, pattern, "\\")) ||
                             (s.Album != null && s.Album.NameRaw != null && EF.Functions.Like(s.Album.NameRaw, pattern, "\\")));
        }
        if (onlyCached) q = q.Where(s => s.RelFilePath != null);
        if (displayFilter == DisplayCategoryFilter.Favorites) q = q.Where(s => s.IsFavorite);
        return q;
    }

    public List<Song> SearchSongs(Account account, string searchText, bool onlyCached, DisplayCategoryFilter displayFilter, int limit = int.MaxValue) =>
        SortSongsByTitle(QuerySongs(account, searchText, onlyCached, displayFilter)).Take(limit).ToList();

    public IQueryable<Genre> QueryGenres(Account account, string searchText, bool onlyCached)
    {
        var q = Context.Genres.Where(g => g.AccountPk == account.Pk);
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(g => EF.Functions.Like(g.NameRaw, $"%{EscapeLike(searchText)}%", "\\"));
        if (onlyCached) q = q.Where(g => g.SongsRaw.Any(s => s.RelFilePath != null));
        return q;
    }

    public IQueryable<Podcast> QueryPodcasts(Account account, string searchText, bool onlyCached)
    {
        var q = Context.Podcasts.Where(p => p.AccountPk == account.Pk &&
                                            (p.RemoteStatus == RemoteStatus.Available || p.EpisodesRaw.Any(e => e.RelFilePath != null)));
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(p => EF.Functions.Like(p.TitleRaw, $"%{EscapeLike(searchText)}%", "\\"));
        if (onlyCached) q = q.Where(p => p.EpisodesRaw.Any(e => e.RelFilePath != null));
        return q;
    }

    public IQueryable<PodcastEpisode> QueryPodcastEpisodes(Account account, string searchText, bool onlyCached)
    {
        var q = UserAvailableEpisodes().Where(e => e.AccountPk == account.Pk);
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(e => e.TitleRaw != null && EF.Functions.Like(e.TitleRaw, $"%{EscapeLike(searchText)}%", "\\"));
        if (onlyCached) q = q.Where(e => e.RelFilePath != null);
        return q;
    }

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
