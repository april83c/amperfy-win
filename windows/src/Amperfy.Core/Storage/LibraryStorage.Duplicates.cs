using System.Linq.Expressions;

namespace Amperfy.Core.Storage;

/// A duplicated key (server id or genre name) and the number of entities sharing it.
public sealed record LibraryDuplicateInfo(string Id, int Count);

/// Entity/key combinations checked by the duplicate resolver (Swift: findDuplicates(for:keyPathString:)).
public enum DuplicateEntityType
{
    GenreById,
    GenreByName,
    Artist,
    Album,
    Song,
    PodcastEpisode,
    Radio,
    Podcast,
    Playlist,
}

/// Duplicate detection and resolution (port of LibraryStorage.swift resolveXxxDuplicates / findDuplicates).
public sealed partial class LibraryStorage
{
    private const string DuplicatesLogCategory = "LibraryStorage";

    public List<LibraryDuplicateInfo> FindDuplicates(DuplicateEntityType type, Account account) => type switch
    {
        DuplicateEntityType.GenreById => FindDuplicates(Context.Genres, g => g.Id, account),
        DuplicateEntityType.GenreByName => FindDuplicates(Context.Genres, g => g.NameRaw, account),
        DuplicateEntityType.Artist => FindDuplicates(Context.Artists, e => e.Id, account),
        DuplicateEntityType.Album => FindDuplicates(Context.Albums, e => e.Id, account),
        DuplicateEntityType.Song => FindDuplicates(Context.Songs, e => e.Id, account),
        DuplicateEntityType.PodcastEpisode => FindDuplicates(Context.PodcastEpisodes, e => e.Id, account),
        DuplicateEntityType.Radio => FindDuplicates(Context.Radios, e => e.Id, account),
        DuplicateEntityType.Podcast => FindDuplicates(Context.Podcasts, e => e.Id, account),
        DuplicateEntityType.Playlist => FindDuplicatePlaylists(account),
        _ => [],
    };

    /// GROUP BY key HAVING count > 1 for library entities of the account; empty keys are removed.
    private static List<LibraryDuplicateInfo> FindDuplicates<T>(IQueryable<T> query, Expression<Func<T, string?>> key, Account account)
        where T : AbstractLibraryEntity
    {
        var accountPk = account.Pk;
        return query.Where(e => e.AccountPk == accountPk)
            .GroupBy(key)
            .Where(g => g.Count() > 1)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToList()
            .Where(x => !string.IsNullOrEmpty(x.Key))
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new LibraryDuplicateInfo(x.Key!, x.Count))
            .ToList();
    }

    private List<LibraryDuplicateInfo> FindDuplicatePlaylists(Account account)
    {
        var accountPk = account.Pk;
        return Context.Playlists.Where(p => p.AccountPk == accountPk)
            .GroupBy(p => p.Id)
            .Where(g => g.Count() > 1)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToList()
            .Where(x => !string.IsNullOrEmpty(x.Key))
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new LibraryDuplicateInfo(x.Key!, x.Count))
            .ToList();
    }

    private static HashSet<string> DuplicateIds(IReadOnlyCollection<LibraryDuplicateInfo> duplicates) => duplicates.Select(d => d.Id).ToHashSet();

    /// Lead (the oldest entity) and the others of each duplicate group.
    private static IEnumerable<(string Key, T Lead, List<T> Others)> SplitLead<T>(Dictionary<string, List<T>> groups, Func<T, int> pk)
    {
        foreach (var (key, list) in groups)
        {
            if (list.Count < 2) continue;
            var ordered = list.OrderBy(pk).ToList();
            yield return (key, ordered[0], ordered.Skip(1).ToList());
        }
    }

    public void ResolveGenresDuplicates(Account account, IReadOnlyCollection<LibraryDuplicateInfo> duplicates, bool byName)
    {
        if (duplicates.Count == 0) return;
        var ids = DuplicateIds(duplicates);
        var groups = byName ? GetGenresDictListByName(account, ids) : GetGenresDictList(account, ids);
        foreach (var (key, lead, others) in SplitLead(groups, g => g.Pk))
        {
            AmperfyLog.Info(DuplicatesLogCategory, byName
                ? $"Duplicated Genre (count {others.Count + 1}): {lead.Name}"
                : $"Duplicated Genre (count {others.Count + 1}) (id: {key}): {lead.Name}");
            foreach (var genre in others)
            {
                PassOwnership(genre, lead);
                Context.Remove(genre);
            }
        }
    }

    public void ResolveArtistsDuplicates(Account account, IReadOnlyCollection<LibraryDuplicateInfo> duplicates)
    {
        if (duplicates.Count == 0) return;
        foreach (var (key, lead, others) in SplitLead(GetArtistsDictList(account, DuplicateIds(duplicates)), a => a.Pk))
        {
            AmperfyLog.Info(DuplicatesLogCategory, $"Duplicated Artist (count {others.Count + 1}) (id: {key}): {lead.Name}");
            foreach (var artist in others)
            {
                PassOwnership(artist, lead);
                Context.Remove(artist);
            }
        }
    }

    public void ResolveAlbumsDuplicates(Account account, IReadOnlyCollection<LibraryDuplicateInfo> duplicates)
    {
        if (duplicates.Count == 0) return;
        foreach (var (key, lead, others) in SplitLead(GetAlbumsDictList(account, DuplicateIds(duplicates)), a => a.Pk))
        {
            AmperfyLog.Info(DuplicatesLogCategory, $"Duplicated Album (count {others.Count + 1}) (id: {key}): {lead.Name}");
            foreach (var album in others)
            {
                foreach (var song in album.SongsRaw.ToList()) song.Album = lead;
                Context.Remove(album);
            }
        }
    }

    public void ResolveSongsDuplicates(Account account, IReadOnlyCollection<LibraryDuplicateInfo> duplicates)
    {
        if (duplicates.Count == 0) return;
        foreach (var (key, lead, others) in SplitLead(GetSongsDictList(account, DuplicateIds(duplicates)), s => s.Pk))
        {
            AmperfyLog.Info(DuplicatesLogCategory, $"Duplicated Song (count {others.Count + 1}) (id: {key}): {lead.DisplayString}");
            foreach (var song in others) RemoveDuplicatePlayable(song, lead);
        }
    }

    public void ResolvePodcastEpisodesDuplicates(Account account, IReadOnlyCollection<LibraryDuplicateInfo> duplicates)
    {
        if (duplicates.Count == 0) return;
        foreach (var (key, lead, others) in SplitLead(GetPodcastEpisodesDictList(account, DuplicateIds(duplicates)), e => e.Pk))
        {
            AmperfyLog.Info(DuplicatesLogCategory, $"Duplicated Podcast Episode (count {others.Count + 1}) (id: {key}): {lead.DisplayString}");
            foreach (var episode in others) RemoveDuplicatePlayable(episode, lead);
        }
    }

    public void ResolveRadioDuplicates(Account account, IReadOnlyCollection<LibraryDuplicateInfo> duplicates)
    {
        if (duplicates.Count == 0) return;
        foreach (var (key, lead, others) in SplitLead(GetRadiosDictList(account, DuplicateIds(duplicates)), r => r.Pk))
        {
            AmperfyLog.Info(DuplicatesLogCategory, $"Duplicated Radio (count {others.Count + 1}) (id: {key}): {lead.DisplayString}");
            foreach (var radio in others) RemoveDuplicatePlayable(radio, lead);
        }
    }

    public void ResolvePodcastsDuplicates(Account account, IReadOnlyCollection<LibraryDuplicateInfo> duplicates)
    {
        if (duplicates.Count == 0) return;
        foreach (var (key, lead, others) in SplitLead(GetPodcastsDictList(account, DuplicateIds(duplicates)), p => p.Pk))
        {
            AmperfyLog.Info(DuplicatesLogCategory, $"Duplicated Podcast (count {others.Count + 1}) (id: {key}): {lead.Name}");
            foreach (var podcast in others)
            {
                foreach (var episode in podcast.EpisodesRaw.ToList()) episode.Podcast = lead;
                Context.Remove(podcast);
            }
        }
    }

    public void ResolvePlaylistsDuplicates(Account account, IReadOnlyCollection<LibraryDuplicateInfo> duplicates)
    {
        if (duplicates.Count == 0) return;
        foreach (var (key, lead, others) in SplitLead(GetPlaylistsDictList(account, DuplicateIds(duplicates)), p => p.Pk))
        {
            AmperfyLog.Info(DuplicatesLogCategory, $"Duplicated Playlist (count {others.Count + 1}) (id: {key}): {lead.Name}");
            // Swift: PlaylistMO.passOwnership is empty -> the duplicates (and their items) are deleted
            foreach (var playlist in others) DeletePlaylist(playlist);
        }
    }

    // --- pass ownership (Swift: XxxMO.passOwnership(to:)) ------------------------------------------

    private static void PassOwnership(Genre genre, Genre target)
    {
        foreach (var artist in genre.ArtistsRaw.ToList()) artist.Genre = target;
        foreach (var album in genre.AlbumsRaw.ToList()) album.Genre = target;
        foreach (var song in genre.SongsRaw.ToList()) song.Genre = target;
    }

    private static void PassOwnership(Artist artist, Artist target)
    {
        foreach (var album in artist.AlbumsRaw.ToList()) album.Artist = target;
        foreach (var song in artist.SongsRaw.ToList()) song.Artist = target;
    }

    private static void PassOwnership(AbstractPlayable playable, AbstractPlayable target)
    {
        foreach (var item in playable.PlaylistItems.ToList()) item.Playable = target;
        foreach (var entry in playable.ScrobbleEntries.ToList()) entry.Playable = target;
        if (target.Download is null && playable.Download is { } download)
        {
            playable.Download = null;
            target.Download = download;
        }
        if (target.EmbeddedArtwork is null && playable.EmbeddedArtwork is { } embeddedArtwork)
        {
            playable.EmbeddedArtwork = null;
            target.EmbeddedArtwork = embeddedArtwork;
        }
    }

    private void RemoveDuplicatePlayable(AbstractPlayable playable, AbstractPlayable lead)
    {
        PassOwnership(playable, lead);
        if (playable.EmbeddedArtwork is { } embeddedArtwork) Context.Remove(embeddedArtwork);
        if (playable.Download is { } download) Context.Remove(download);
        Context.Remove(playable);
    }
}
