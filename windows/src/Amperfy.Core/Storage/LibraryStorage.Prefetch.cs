using Amperfy.Core.Api;
using Microsoft.EntityFrameworkCore;

namespace Amperfy.Core.Storage;

/// IDs collected by a first "IDs only" parser pass so that the real parser can look up existing
/// entities in bulk (instead of one query per element).
public sealed class PrefetchIdContainer
{
    public HashSet<ArtworkRemoteInfo> ArtworkIDs { get; } = [];
    public HashSet<string> GenreIDs { get; } = [];
    public HashSet<string> GenreNames { get; } = [];
    public HashSet<string> ArtistIDs { get; } = [];
    public HashSet<string> LocalArtistNames { get; } = [];
    public HashSet<string> AlbumIDs { get; } = [];
    public HashSet<string> SongIDs { get; } = [];
    public HashSet<string> PodcastEpisodeIDs { get; } = [];
    public HashSet<string> RadioIDs { get; } = [];
    public HashSet<string> MusicFolderIDs { get; } = [];
    public HashSet<string> DirectoryIDs { get; } = [];
    public HashSet<string> PodcastIDs { get; } = [];

    public int Counts =>
        ArtworkIDs.Count + GenreIDs.Count + GenreNames.Count + ArtistIDs.Count + LocalArtistNames.Count + AlbumIDs.Count +
        SongIDs.Count + PodcastEpisodeIDs.Count + RadioIDs.Count + MusicFolderIDs.Count + DirectoryIDs.Count + PodcastIDs.Count;
}

/// Entities looked up for a PrefetchIdContainer. Parsers add newly created entities to these
/// dictionaries so that repeated references within one response resolve to the same entity.
public sealed class PrefetchElementContainer
{
    public Dictionary<ArtworkRemoteInfo, Artwork> PrefetchedArtworkDict { get; set; } = [];
    public Dictionary<string, Genre> PrefetchedGenreDict { get; set; } = [];
    public Dictionary<string, Artist> PrefetchedArtistDict { get; set; } = [];
    public Dictionary<string, Artist> PrefetchedLocalArtistDict { get; set; } = [];
    public Dictionary<string, Album> PrefetchedAlbumDict { get; set; } = [];
    public Dictionary<string, Song> PrefetchedSongDict { get; set; } = [];
    public Dictionary<string, PodcastEpisode> PrefetchedPodcastEpisodeDict { get; set; } = [];
    public Dictionary<string, Radio> PrefetchedRadioDict { get; set; } = [];
    public Dictionary<string, MusicFolder> PrefetchedMusicFolderDict { get; set; } = [];
    public Dictionary<string, MusicDirectory> PrefetchedDirectoryDict { get; set; } = [];
    public Dictionary<string, Podcast> PrefetchedPodcastDict { get; set; } = [];

    public int Counts =>
        PrefetchedArtworkDict.Count + PrefetchedGenreDict.Count + PrefetchedArtistDict.Count + PrefetchedLocalArtistDict.Count +
        PrefetchedAlbumDict.Count + PrefetchedSongDict.Count + PrefetchedPodcastEpisodeDict.Count + PrefetchedRadioDict.Count +
        PrefetchedMusicFolderDict.Count + PrefetchedDirectoryDict.Count + PrefetchedPodcastDict.Count;
}

public sealed partial class LibraryStorage
{
    private const int MaxSqlParameters = 900;

    /// Loads entities by (remote) ids in chunks (SQLite parameter limit).
    private List<T> LoadByIds<T>(IQueryable<T> baseQuery, IReadOnlyCollection<string> ids, Func<IQueryable<T>, List<string>, IQueryable<T>> filter) where T : class
    {
        var result = new List<T>();
        foreach (var chunk in ids.Chunk(MaxSqlParameters))
            result.AddRange(filter(baseQuery, chunk.ToList()).ToList());
        return result;
    }

    private static Dictionary<string, T> FirstByKey<T>(IEnumerable<T> items, Func<T, string> key)
    {
        var dict = new Dictionary<string, T>();
        foreach (var item in items) dict.TryAdd(key(item), item);
        return dict;
    }

    private static Dictionary<string, List<T>> GroupByKey<T>(IEnumerable<T> items, Func<T, string> key)
    {
        var dict = new Dictionary<string, List<T>>();
        foreach (var item in items)
        {
            if (!dict.TryGetValue(key(item), out var list)) dict[key(item)] = list = [];
            list.Add(item);
        }
        return dict;
    }

    public PrefetchElementContainer GetElements(Account account, PrefetchIdContainer prefetchIDs)
    {
        var c = new PrefetchElementContainer();
        if (prefetchIDs.ArtworkIDs.Count > 0) c.PrefetchedArtworkDict = GetArtworksDict(account, prefetchIDs.ArtworkIDs);
        if (prefetchIDs.GenreIDs.Count > 0) c.PrefetchedGenreDict = FirstByKey(GetGenres(account, prefetchIDs.GenreIDs), g => g.Id);
        else if (prefetchIDs.GenreNames.Count > 0) c.PrefetchedGenreDict = FirstByKey(GetGenresByNames(account, prefetchIDs.GenreNames), g => g.Name);
        if (prefetchIDs.ArtistIDs.Count > 0) c.PrefetchedArtistDict = FirstByKey(GetArtists(account, prefetchIDs.ArtistIDs), a => a.Id);
        if (prefetchIDs.LocalArtistNames.Count > 0) c.PrefetchedLocalArtistDict = GetLocalArtists(account, prefetchIDs.LocalArtistNames);
        if (prefetchIDs.AlbumIDs.Count > 0) c.PrefetchedAlbumDict = FirstByKey(GetAlbums(account, prefetchIDs.AlbumIDs), a => a.Id);
        if (prefetchIDs.SongIDs.Count > 0) c.PrefetchedSongDict = FirstByKey(GetSongs(account, prefetchIDs.SongIDs), s => s.Id);
        if (prefetchIDs.PodcastEpisodeIDs.Count > 0) c.PrefetchedPodcastEpisodeDict = FirstByKey(GetPodcastEpisodes(account, prefetchIDs.PodcastEpisodeIDs), e => e.Id);
        if (prefetchIDs.RadioIDs.Count > 0) c.PrefetchedRadioDict = FirstByKey(GetRadios(account, prefetchIDs.RadioIDs), r => r.Id);
        if (prefetchIDs.MusicFolderIDs.Count > 0) c.PrefetchedMusicFolderDict = FirstByKey(GetMusicFolders(account, prefetchIDs.MusicFolderIDs), f => f.Id);
        if (prefetchIDs.DirectoryIDs.Count > 0) c.PrefetchedDirectoryDict = GetDirectories(account, prefetchIDs.DirectoryIDs);
        if (prefetchIDs.PodcastIDs.Count > 0) c.PrefetchedPodcastDict = FirstByKey(GetPodcasts(account, prefetchIDs.PodcastIDs), p => p.Id);
        return c;
    }

    private Dictionary<ArtworkRemoteInfo, Artwork> GetArtworksDict(Account account, IReadOnlyCollection<ArtworkRemoteInfo> remoteInfos)
    {
        var ids = remoteInfos.Select(r => r.Id).Distinct().ToList();
        var artworks = LoadByIds(Context.Artworks.Where(a => a.AccountPk == account.Pk), ids, (q, chunk) => q.Where(a => chunk.Contains(a.Id)));
        var dict = new Dictionary<ArtworkRemoteInfo, Artwork>();
        foreach (var a in artworks) dict.TryAdd(a.RemoteInfo, a);
        return dict;
    }

    public List<Genre> GetGenres(Account account, IReadOnlyCollection<string> ids) =>
        LoadByIds(Context.Genres.Where(g => g.AccountPk == account.Pk), ids, (q, chunk) => q.Where(g => chunk.Contains(g.Id)));

    public List<Genre> GetGenresByNames(Account account, IReadOnlyCollection<string> names) =>
        LoadByIds(Context.Genres.Where(g => g.AccountPk == account.Pk), names, (q, chunk) => q.Where(g => chunk.Contains(g.NameRaw)));

    public List<Artist> GetArtists(Account account, IReadOnlyCollection<string> ids) =>
        LoadByIds(Context.Artists.Where(a => a.AccountPk == account.Pk), ids, (q, chunk) => q.Where(a => chunk.Contains(a.Id)));

    private Dictionary<string, Artist> GetLocalArtists(Account account, IReadOnlyCollection<string> names)
    {
        var artists = LoadByIds(Context.Artists.Where(a => a.AccountPk == account.Pk && a.Id == ""), names,
            (q, chunk) => q.Where(a => a.NameRaw != null && chunk.Contains(a.NameRaw)));
        return FirstByKey(artists, a => a.Name);
    }

    public List<Album> GetAlbums(Account account, IReadOnlyCollection<string> ids) =>
        LoadByIds(Context.Albums.Where(a => a.AccountPk == account.Pk), ids, (q, chunk) => q.Where(a => chunk.Contains(a.Id)));

    public List<Song> GetSongs(Account account, IReadOnlyCollection<string> ids) =>
        LoadByIds(Context.Songs.Where(s => s.AccountPk == account.Pk), ids, (q, chunk) => q.Where(s => chunk.Contains(s.Id)));

    public List<PodcastEpisode> GetPodcastEpisodes(Account account, IReadOnlyCollection<string> ids) =>
        LoadByIds(Context.PodcastEpisodes.Where(e => e.AccountPk == account.Pk), ids, (q, chunk) => q.Where(e => chunk.Contains(e.Id)));

    public List<Radio> GetRadios(Account account, IReadOnlyCollection<string> ids) =>
        LoadByIds(Context.Radios.Where(r => r.AccountPk == account.Pk), ids, (q, chunk) => q.Where(r => chunk.Contains(r.Id)));

    public List<MusicFolder> GetMusicFolders(Account account, IReadOnlyCollection<string> ids) =>
        LoadByIds(Context.MusicFolders.Where(f => f.AccountPk == account.Pk), ids, (q, chunk) => q.Where(f => chunk.Contains(f.Id)));

    public Dictionary<string, MusicDirectory> GetDirectories(Account account, IReadOnlyCollection<string> ids) =>
        FirstByKey(LoadByIds(Context.Directories.Where(d => d.AccountPk == account.Pk), ids, (q, chunk) => q.Where(d => chunk.Contains(d.Id))), d => d.Id);

    public List<Podcast> GetPodcasts(Account account, IReadOnlyCollection<string> ids) =>
        LoadByIds(Context.Podcasts.Where(p => p.AccountPk == account.Pk), ids, (q, chunk) => q.Where(p => chunk.Contains(p.Id)));

    public List<Playlist> GetPlaylists(Account account, IReadOnlyCollection<string> ids) =>
        LoadByIds(Context.Playlists.Where(p => p.AccountPk == account.Pk), ids, (q, chunk) => q.Where(p => chunk.Contains(p.Id)));

    // Grouped variants (used by the duplicate resolver)
    public Dictionary<string, List<Genre>> GetGenresDictList(Account account, IReadOnlyCollection<string> ids) => GroupByKey(GetGenres(account, ids), g => g.Id);
    public Dictionary<string, List<Genre>> GetGenresDictListByName(Account account, IReadOnlyCollection<string> names) => GroupByKey(GetGenresByNames(account, names), g => g.Name);
    public Dictionary<string, List<Artist>> GetArtistsDictList(Account account, IReadOnlyCollection<string> ids) => GroupByKey(GetArtists(account, ids), a => a.Id);
    public Dictionary<string, List<Album>> GetAlbumsDictList(Account account, IReadOnlyCollection<string> ids) => GroupByKey(GetAlbums(account, ids), a => a.Id);
    public Dictionary<string, List<Song>> GetSongsDictList(Account account, IReadOnlyCollection<string> ids) => GroupByKey(GetSongs(account, ids), s => s.Id);
    public Dictionary<string, List<PodcastEpisode>> GetPodcastEpisodesDictList(Account account, IReadOnlyCollection<string> ids) => GroupByKey(GetPodcastEpisodes(account, ids), e => e.Id);
    public Dictionary<string, List<Radio>> GetRadiosDictList(Account account, IReadOnlyCollection<string> ids) => GroupByKey(GetRadios(account, ids), r => r.Id);
    public Dictionary<string, List<Podcast>> GetPodcastsDictList(Account account, IReadOnlyCollection<string> ids) => GroupByKey(GetPodcasts(account, ids), p => p.Id);
    public Dictionary<string, List<Playlist>> GetPlaylistsDictList(Account account, IReadOnlyCollection<string> ids) => GroupByKey(GetPlaylists(account, ids), p => p.Id);
}
