using Amperfy.Core.Api;
using Microsoft.EntityFrameworkCore;

namespace Amperfy.Core.Storage;

public sealed class AccountLibraryInfo
{
    public string ApiType { get; set; } = "";
    public int ArtistCount { get; set; }
    public int AlbumCount { get; set; }
    public int SongCount { get; set; }
    public int CachedSongCount { get; set; }
    public int PlaylistCount { get; set; }
    public string CachedSongSize { get; set; } = "";
    public int GenreCount { get; set; }
    public int ArtworkCount { get; set; }
    public int MusicFolderCount { get; set; }
    public int DirectoryCount { get; set; }
    public int PodcastCount { get; set; }
    public int PodcastEpisodeCount { get; set; }
    public int RadioCount { get; set; }
}

/// Data access for the library (port of LibraryStorage.swift). Must only be used from the
/// thread that owns the underlying context (the UI thread in the app).
public sealed partial class LibraryStorage
{
    public const int CarPlayMaxElements = 200;

    public AmperfyDbContext Context { get; }
    private CacheFileManager FileManager => CacheFileManager.Shared;

    public LibraryStorage(AmperfyDbContext context)
    {
        Context = context;
    }

    /// Raised after changes have been saved successfully.
    public event Action? Saved;

    public bool HasChanges
    {
        get
        {
            Context.ChangeTracker.DetectChanges();
            return Context.ChangeTracker.HasChanges();
        }
    }

    public void SaveContext()
    {
        try
        {
            if (!HasChanges) return;
            Context.SaveChanges(acceptAllChangesOnSuccess: true);
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            AmperfyLog.Error("LibraryStorage", $"Save context error: {ex}");
            // Attempt recovery: drop pending changes instead of crashing
            foreach (var entry in Context.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
            {
                switch (entry.State)
                {
                    case EntityState.Added: entry.State = EntityState.Detached; break;
                    case EntityState.Modified:
                    case EntityState.Deleted:
                        entry.CurrentValues.SetValues(entry.OriginalValues);
                        entry.State = EntityState.Unchanged;
                        break;
                }
            }
        }
    }

    private T Create<T>(Account? account) where T : class
    {
        var entity = Context.CreateProxy<T>();
        switch (entity)
        {
            case AbstractLibraryEntity e: e.Account = account; break;
            case Playlist p: p.Account = account; break;
            case Artwork a: a.Account = account; break;
            case MusicFolder f: f.Account = account; break;
        }
        Context.Add(entity);
        return entity;
    }

    // --- counts --------------------------------------------------------------------------------

    public AccountLibraryInfo GetInfo(Account account) => new()
    {
        ApiType = account.ApiType.Description(),
        ArtistCount = GetArtistCount(account),
        AlbumCount = GetAlbumCount(account),
        SongCount = GetSongCount(account),
        CachedSongCount = GetCachedSongCount(account),
        PlaylistCount = GetPlaylistCount(account),
        CachedSongSize = FileManager.GetPlayableCacheSize(account.Info).AsByteString(),
        GenreCount = GetGenreCount(account),
        ArtworkCount = GetArtworkCount(account),
        MusicFolderCount = GetMusicFolderCount(account),
        DirectoryCount = GetDirectoryCount(account),
        PodcastCount = GetPodcastCount(account),
        PodcastEpisodeCount = GetPodcastEpisodeCount(account),
        RadioCount = GetRadioCount(account),
    };

    public int GetGenreCount(Account account) => Context.Genres.Count(e => e.AccountPk == account.Pk);
    public int GetArtistCount(Account account) => Context.Artists.Count(e => e.AccountPk == account.Pk);
    public int GetAlbumCount(Account account) => Context.Albums.Count(e => e.AccountPk == account.Pk && e.RemoteStatus == RemoteStatus.Available);
    public int GetAlbumWithSyncedSongsCount(Account account) =>
        Context.Albums.Count(e => e.AccountPk == account.Pk && e.RemoteStatus == RemoteStatus.Available && e.IsSongsMetaDataSynced);
    public int GetSongCount(Account account) => Context.Songs.Count(e => e.AccountPk == account.Pk);
    public int GetUploadableScrobbleEntryCount(Account account) => Context.ScrobbleEntries.Count(e => e.AccountPk == account.Pk && !e.IsUploaded);
    public int GetArtworkCount(Account account) => Context.Artworks.Count(e => e.AccountPk == account.Pk);
    public int GetArtworkNotCheckedCount(Account account) =>
        Context.Artworks.Count(e => e.AccountPk == account.Pk && e.RelFilePath == null && e.Status == ImageStatus.NotChecked);
    public int GetCachedArtworkCount(Account account) => Context.Artworks.Count(e => e.AccountPk == account.Pk && e.RelFilePath != null);
    public int GetMusicFolderCount(Account account) => Context.MusicFolders.Count(e => e.AccountPk == account.Pk);
    public int GetDirectoryCount(Account account) => Context.Directories.Count(e => e.AccountPk == account.Pk);
    public int GetCachedSongCount(Account account) => Context.Songs.Count(e => e.AccountPk == account.Pk && e.RelFilePath != null);
    public int GetPlaylistCount(Account account) => UserPlaylists().Count(e => e.AccountPk == account.Pk);
    public int GetPodcastCount(Account account) => Context.Podcasts.Count(e => e.AccountPk == account.Pk);
    public int GetPodcastEpisodeCount(Account account) => Context.PodcastEpisodes.Count(e => e.AccountPk == account.Pk);
    public int GetRadioCount(Account account) => Context.Radios.Count(e => e.AccountPk == account.Pk);
    public int GetCachedPodcastEpisodeCount(Account account) => Context.PodcastEpisodes.Count(e => e.AccountPk == account.Pk && e.RelFilePath != null);

    /// Number of albums which are by the artist or contain songs of the artist.
    public int GetAlbumCountContainingSongs(Account account, Artist artist) =>
        Context.Albums.Count(a => a.AccountPk == account.Pk && (a.ArtistPk == artist.Pk || a.SongsRaw.Any(s => s.ArtistPk == artist.Pk)));

    /// Number of songs by the artist or on albums of the artist.
    public int GetSongCountOfArtist(Account account, Artist artist) =>
        AvailableSongs().Count(s => s.AccountPk == account.Pk && (s.ArtistPk == artist.Pk || (s.Album != null && s.Album.ArtistPk == artist.Pk)));

    // --- accounts ------------------------------------------------------------------------------

    public List<Account> GetAllAccounts() => Context.Accounts.ToList();

    public Account CreateAccount(AccountInfo info)
    {
        var account = Context.CreateProxy<Account>();
        account.AssignInfo(info);
        Context.Add(account);
        return account;
    }

    public void DeleteAccount(Account account) => Context.Remove(account);

    public Account? GetAccount(string ident)
    {
        var info = AccountInfo.Create(ident);
        if (info is null) return null;
        return Context.Accounts.FirstOrDefault(a => a.ServerHashRaw == info.ServerHash && a.UserHashRaw == info.UserHash);
    }

    /// Gets the account or creates an "empty" one.
    public Account GetAccount(AccountInfo info)
    {
        var local = Context.Accounts.Local.FirstOrDefault(a => a.ServerHash == info.ServerHash && a.UserHash == info.UserHash);
        if (local is not null) return local;
        var account = Context.Accounts.FirstOrDefault(a => a.ServerHashRaw == info.ServerHash && a.UserHashRaw == info.UserHash);
        if (account is not null) return account;
        account = CreateAccount(info);
        SaveContext();
        return account;
    }

    // --- create / delete -----------------------------------------------------------------------

    public Genre CreateGenre(Account account) => Create<Genre>(account);
    public Artist CreateArtist(Account account) => Create<Artist>(account);
    public void DeleteArtist(Artist artist) => Context.Remove(artist);
    public Album CreateAlbum(Account account) => Create<Album>(account);
    public void DeleteAlbum(Album album) => Context.Remove(album);
    public Podcast CreatePodcast(Account account) => Create<Podcast>(account);
    public void DeletePodcast(Podcast podcast) => Context.Remove(podcast);
    public PodcastEpisode CreatePodcastEpisode(Account account) => Create<PodcastEpisode>(account);
    public Song CreateSong(Account account) => Create<Song>(account);
    public void DeleteSong(Song song) => Context.Remove(song);
    public Radio CreateRadio(Account account) => Create<Radio>(account);
    public void DeleteRadio(Radio radio) => Context.Remove(radio);
    public MusicFolder CreateMusicFolder(Account account) => Create<MusicFolder>(account);
    public void DeleteMusicFolder(MusicFolder musicFolder) => Context.Remove(musicFolder);
    public MusicDirectory CreateDirectory(Account account) => Create<MusicDirectory>(account);
    public void DeleteDirectory(MusicDirectory directory) => Context.Remove(directory);
    public Artwork CreateArtwork(Account account) => Create<Artwork>(account);
    public void DeleteArtwork(Artwork artwork) => Context.Remove(artwork);

    public ScrobbleEntry CreateScrobbleEntry(Account account)
    {
        var entry = new ScrobbleEntry { Account = account };
        Context.Add(entry);
        return entry;
    }

    public void DeleteScrobbleEntry(ScrobbleEntry entry) => Context.Remove(entry);

    public LogEntry CreateLogEntry()
    {
        var entry = new LogEntry { CreationDate = DateTime.UtcNow };
        Context.Add(entry);
        return entry;
    }

    public EmbeddedArtwork CreateEmbeddedArtwork(Account account)
    {
        var artwork = new EmbeddedArtwork { Account = account };
        Context.Add(artwork);
        return artwork;
    }

    public void DeleteEmbeddedArtwork(EmbeddedArtwork artwork) => Context.Remove(artwork);

    public Playlist CreatePlaylist(Account? account) => Create<Playlist>(account);

    public void DeletePlaylist(Playlist playlist)
    {
        playlist.RemoveAllItems();
        Context.Remove(playlist);
    }

    public void DeletePlaylistItem(PlaylistItem item) => Context.Remove(item);

    public Model.Download CreateDownload(Account account, string id)
    {
        var download = new Model.Download { Account = account, Id = id, CreationDate = DateTime.UtcNow };
        Context.Add(download);
        return download;
    }

    public List<Model.Download> GetAllDownloads() => Context.Downloads.ToList();

    public Model.Download? GetDownload(Account account, string id) =>
        Context.Downloads.Local.FirstOrDefault(d => d.AccountPk == account.Pk && d.Id == id && Context.Entry(d).State != EntityState.Deleted)
        ?? Context.Downloads.FirstOrDefault(d => d.AccountPk == account.Pk && d.Id == id);

    public List<Model.Download> GetDownloads(Account account, IReadOnlyCollection<string> ids) =>
        Context.Downloads.Where(d => d.AccountPk == account.Pk && ids.Contains(d.Id)).ToList();

    public Dictionary<string, Model.Download> GetDownloadsDict(Account account, IReadOnlyCollection<string> ids)
    {
        var dict = new Dictionary<string, Model.Download>();
        foreach (var d in GetDownloads(account, ids)) dict[d.Id] = d;
        return dict;
    }

    public void DeleteDownload(Model.Download download) => Context.Remove(download);

    // --- cache ---------------------------------------------------------------------------------

    public void DeleteCache(AbstractPlayable playable)
    {
        if (playable.Account is { } account && playable.RelFilePath is { } rel && FileManager.FileExists(rel))
        {
            try { FileManager.RemoveRelItem(rel, account.Info); }
            catch (Exception ex) { AmperfyLog.Info("LibraryStorage", $"File for <{playable.DisplayString}> could not be removed: {ex.Message}"); }
        }
        DeleteCacheFinalStep(playable);
    }

    private static void DeleteCacheFinalStep(AbstractPlayable playable)
    {
        playable.ContentTypeTranscoded = null;
        playable.RelFilePath = null;
        playable.DeleteCache();
    }

    public void DeleteCache(IEnumerable<AbstractPlayable> playables)
    {
        foreach (var p in playables.ToList()) DeleteCache(p);
    }

    public void DeleteCache(IPlayableContainable container) => DeleteCache(container.Playables);

    public void DeletePlayableCachePaths(Account account)
    {
        foreach (var s in GetCachedSongs(account)) DeleteCacheFinalStep(s);
        foreach (var e in GetCachedPodcastEpisodes(account)) DeleteCacheFinalStep(e);
    }

    public void DeleteRemoteArtworkCachePaths(Account account)
    {
        var artworks = Context.Artworks.Where(a => a.AccountPk == account.Pk).ToList();
        foreach (var artwork in artworks)
        {
            artwork.Status = ImageStatus.NotChecked;
            artwork.RelFilePath = null;
            if (string.IsNullOrEmpty(artwork.Id)) DeleteArtwork(artwork);
        }
    }

    /// Absolute file path of a cached playable.
    public string? GetFilePath(AbstractPlayable playable)
    {
        if (playable.RelFilePath is { } rel) return FileManager.GetAbsoluteAmperfyPath(rel);
        AmperfyLog.Error("LibraryStorage", $"File URL was not able to retrieve for: {playable.DisplayString}");
        return null;
    }

    // --- container / search history ------------------------------------------------------------

    public IPlayableContainable? GetContainer(PlayableContainerIdentifier identifier)
    {
        if (identifier.Type is not { } type || !int.TryParse(identifier.ObjectId, out var pk)) return null;
        return type switch
        {
            PlayableContainerBaseType.Song => Context.Songs.Find(pk),
            PlayableContainerBaseType.PodcastEpisode => Context.PodcastEpisodes.Find(pk),
            PlayableContainerBaseType.Album => Context.Albums.Find(pk),
            PlayableContainerBaseType.Artist => Context.Artists.Find(pk),
            PlayableContainerBaseType.Genre => Context.Genres.Find(pk),
            PlayableContainerBaseType.Playlist => Context.Playlists.Find(pk),
            PlayableContainerBaseType.Podcast => Context.Podcasts.Find(pk),
            PlayableContainerBaseType.Directory => Context.Directories.Find(pk),
            PlayableContainerBaseType.Radio => Context.Radios.Find(pk),
            _ => null,
        };
    }

    public SearchHistoryItem CreateOrUpdateSearchHistory(IPlayableContainable container)
    {
        SearchHistoryItem? existing = container switch
        {
            Playlist p => Context.SearchHistoryItems.FirstOrDefault(h => h.SearchedPlaylistPk == p.Pk),
            AbstractLibraryEntity e => Context.SearchHistoryItems.FirstOrDefault(h => h.SearchedLibraryEntityPk == e.Pk),
            _ => null,
        };
        if (existing is not null)
        {
            existing.Date = DateTime.UtcNow;
            return existing;
        }
        var item = new SearchHistoryItem { Date = DateTime.UtcNow, Account = container.Account };
        item.SearchedPlayableContainable = container;
        Context.Add(item);
        return item;
    }

    public void DeleteSearchHistory() => Context.SearchHistoryItems.ExecuteDelete();

    public List<SearchHistoryItem> GetSearchHistory(Account account) =>
        Context.SearchHistoryItems
            .Where(h => h.AccountPk == account.Pk && (h.SearchedLibraryEntityPk != null || h.SearchedPlaylistPk != null))
            .OrderByDescending(h => h.Date)
            .ToList();

    public List<SearchHistoryItem> GetAllSearchHistory() => Context.SearchHistoryItems.ToList();

    // --- storage cleaning ----------------------------------------------------------------------

    /// Deletes all library data of an account (the account entity itself is kept).
    public void CleanStorageOfObsoleteAccountEntries(Account account)
    {
        SaveContext();
        var pk = account.Pk;
        Context.SearchHistoryItems.Where(e => e.AccountPk == pk).ExecuteDelete();
        Context.ScrobbleEntries.Where(e => e.AccountPk == pk).ExecuteDelete();
        Context.Downloads.Where(e => e.AccountPk == pk).ExecuteDelete();
        Context.PlaylistItems.Where(e => e.AccountPk == pk).ExecuteDelete();
        Context.Playlists.Where(e => e.AccountPk == pk).ExecuteDelete();
        Context.EmbeddedArtworks.Where(e => e.AccountPk == pk).ExecuteDelete();
        Context.LibraryEntities.Where(e => e.AccountPk == pk).ExecuteDelete();
        Context.Artworks.Where(e => e.AccountPk == pk).ExecuteDelete();
        Context.MusicFolders.Where(e => e.AccountPk == pk).ExecuteDelete();
        Context.ChangeTracker.Clear();
    }

    /// Deletes everything except accounts.
    public void CleanStorage()
    {
        SaveContext();
        Context.SearchHistoryItems.ExecuteDelete();
        Context.ScrobbleEntries.ExecuteDelete();
        Context.Downloads.ExecuteDelete();
        Context.PlayerStates.ExecuteDelete();
        Context.PlaylistItems.ExecuteDelete();
        Context.Playlists.ExecuteDelete();
        Context.EmbeddedArtworks.ExecuteDelete();
        Context.LibraryEntities.ExecuteDelete();
        Context.Artworks.ExecuteDelete();
        Context.MusicFolders.ExecuteDelete();
        Context.LogEntries.ExecuteDelete();
        Context.ChangeTracker.Clear();
    }
}
