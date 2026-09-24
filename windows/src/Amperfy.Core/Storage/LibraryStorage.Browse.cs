using Microsoft.EntityFrameworkCore;

namespace Amperfy.Core.Storage;

/// Queries used by the library browsing UI (category pages, detail pages, search). They return
/// IQueryables so the UI can count and page through big libraries without materializing
/// everything (port of the sort descriptors / predicates of the Swift fetched results controllers).
public sealed partial class LibraryStorage
{
    private const string NoCase = "NOCASE";

    // --- sorting ---------------------------------------------------------------------------------

    /// Swift: ArtistFetchedResultsController sort types.
    public static IQueryable<Artist> SortArtists(IQueryable<Artist> q, ArtistElementSortType sortType) => sortType switch
    {
        ArtistElementSortType.Rating => q.OrderByDescending(a => a.Rating)
            .ThenBy(a => EF.Functions.Collate(a.NameRaw, NoCase)).ThenBy(a => a.Id),
        ArtistElementSortType.Duration => q.OrderBy(a => a.DurationRaw)
            .ThenBy(a => EF.Functions.Collate(a.NameRaw, NoCase)).ThenBy(a => a.Id),
        // newest: artists do not support recently added -> identifier sorted
        ArtistElementSortType.Newest => q.OrderBy(a => EF.Functions.Collate(a.NameRaw, NoCase)).ThenBy(a => a.Id),
        _ => q.OrderBy(a => a.AlphabeticSectionInitial)
            .ThenBy(a => EF.Functions.Collate(a.NameRaw, NoCase)).ThenBy(a => a.Id),
    };

    /// Swift: AlbumFetchedResultsController sort types.
    public static IQueryable<Album> SortAlbums(IQueryable<Album> q, AlbumElementSortType sortType) => sortType switch
    {
        AlbumElementSortType.Rating => q.OrderByDescending(a => a.Rating)
            .ThenBy(a => EF.Functions.Collate(a.NameRaw, NoCase)).ThenBy(a => a.Id),
        AlbumElementSortType.Newest => q.OrderBy(a => a.NewestIndex)
            .ThenBy(a => EF.Functions.Collate(a.NameRaw, NoCase)).ThenBy(a => a.Id),
        AlbumElementSortType.Recent => q.OrderBy(a => a.RecentIndex)
            .ThenBy(a => EF.Functions.Collate(a.NameRaw, NoCase)).ThenBy(a => a.Id),
        AlbumElementSortType.Artist => q
            .OrderBy(a => a.Artist!.AlphabeticSectionInitial)
            .ThenBy(a => EF.Functions.Collate(a.Artist!.NameRaw, NoCase))
            .ThenBy(a => a.AlphabeticSectionInitial)
            .ThenBy(a => EF.Functions.Collate(a.NameRaw, NoCase)).ThenBy(a => a.Id),
        AlbumElementSortType.Duration => q.OrderBy(a => a.DurationRaw)
            .ThenBy(a => EF.Functions.Collate(a.NameRaw, NoCase)).ThenBy(a => a.Id),
        AlbumElementSortType.Year => q.OrderByDescending(a => a.Year)
            .ThenBy(a => EF.Functions.Collate(a.NameRaw, NoCase)).ThenBy(a => a.Id),
        _ => q.OrderBy(a => a.AlphabeticSectionInitial)
            .ThenBy(a => EF.Functions.Collate(a.NameRaw, NoCase)).ThenBy(a => a.Id),
    };

    /// Swift: SongsFetchedResultsController sort types.
    public static IQueryable<Song> SortSongs(IQueryable<Song> q, SongElementSortType sortType) => sortType switch
    {
        SongElementSortType.Rating => q.OrderByDescending(s => s.Rating)
            .ThenBy(s => EF.Functions.Collate(s.TitleRaw, NoCase)).ThenBy(s => s.Id),
        SongElementSortType.AddedDate => q.OrderByDescending(s => s.AddedDate)
            .ThenBy(s => EF.Functions.Collate(s.TitleRaw, NoCase)).ThenBy(s => s.Id),
        SongElementSortType.Duration => q.OrderBy(s => s.CombinedDuration)
            .ThenBy(s => EF.Functions.Collate(s.TitleRaw, NoCase)).ThenBy(s => s.Id),
        SongElementSortType.StarredDate => q.OrderByDescending(s => s.StarredDate)
            .ThenBy(s => EF.Functions.Collate(s.TitleRaw, NoCase)).ThenBy(s => s.Id),
        _ => q.OrderBy(s => s.AlphabeticSectionInitial)
            .ThenBy(s => EF.Functions.Collate(s.TitleRaw, NoCase)).ThenBy(s => s.Id),
    };

    /// Swift: PlaylistFetchedResultsController sort types.
    public static IQueryable<Playlist> SortPlaylists(IQueryable<Playlist> q, PlaylistSortType sortType) => sortType switch
    {
        PlaylistSortType.LastPlayed => q.OrderByDescending(p => p.LastPlayedDate)
            .ThenBy(p => EF.Functions.Collate(p.NameRaw, NoCase)).ThenBy(p => p.Id),
        PlaylistSortType.LastChanged => q.OrderByDescending(p => p.ChangeDate)
            .ThenBy(p => EF.Functions.Collate(p.NameRaw, NoCase)).ThenBy(p => p.Id),
        PlaylistSortType.Duration => q.OrderByDescending(p => p.DurationRaw).ThenByDescending(p => p.SongCountRaw)
            .ThenBy(p => EF.Functions.Collate(p.NameRaw, NoCase)).ThenBy(p => p.Id),
        _ => q.OrderBy(p => p.AlphabeticSectionInitial)
            .ThenBy(p => EF.Functions.Collate(p.NameRaw, NoCase)).ThenBy(p => p.Id),
    };

    public static IQueryable<Genre> SortGenres(IQueryable<Genre> q) =>
        q.OrderBy(g => g.AlphabeticSectionInitial).ThenBy(g => EF.Functions.Collate(g.NameRaw, NoCase)).ThenBy(g => g.Id);

    public static IQueryable<Podcast> SortPodcasts(IQueryable<Podcast> q) =>
        q.OrderBy(p => EF.Functions.Collate(p.TitleRaw, NoCase)).ThenBy(p => p.Id);

    public static IQueryable<Radio> SortRadios(IQueryable<Radio> q) =>
        q.OrderBy(r => r.AlphabeticSectionInitial).ThenBy(r => EF.Functions.Collate(r.TitleRaw, NoCase)).ThenBy(r => r.Id);

    public static IQueryable<MusicDirectory> SortDirectories(IQueryable<MusicDirectory> q) =>
        q.OrderBy(d => EF.Functions.Collate(d.NameRaw, NoCase)).ThenBy(d => d.Id);

    /// Episodes sorted by release date, newest first.
    public static IQueryable<PodcastEpisode> SortEpisodesByPublishDate(IQueryable<PodcastEpisode> q) =>
        q.OrderByDescending(e => e.PublishDateRaw).ThenBy(e => e.Id);

    // --- section index (jump to letter) ----------------------------------------------------------

    /// Distinct alphabetic section initials of the (unsorted or sorted) query, ordered.
    public static List<string> GetSectionInitials<T>(IQueryable<T> q) where T : AbstractLibraryEntity =>
        q.Select(e => e.AlphabeticSectionInitial).Distinct().OrderBy(s => s).ToList();

    public static List<string> GetSectionInitials(IQueryable<Playlist> q) =>
        q.Select(e => e.AlphabeticSectionInitial).Distinct().OrderBy(s => s).ToList();

    /// Number of elements sorted before the given section initial (for alphabetically sorted queries
    /// the index of the first element of the section).
    public static int CountBeforeSectionInitial<T>(IQueryable<T> q, string sectionInitial) where T : AbstractLibraryEntity =>
        q.Count(e => string.Compare(e.AlphabeticSectionInitial, sectionInitial) < 0);

    public static int CountBeforeSectionInitial(IQueryable<Playlist> q, string sectionInitial) =>
        q.Count(e => string.Compare(e.AlphabeticSectionInitial, sectionInitial) < 0);

    // --- artists ---------------------------------------------------------------------------------

    /// Albums of the artist or albums containing songs of the artist (Swift: ArtistAlbumsItemsFetchedResultsController),
    /// sorted by release year.
    public IQueryable<Album> QueryArtistAlbums(Artist artist, string searchText = "", bool onlyCached = false)
    {
        var pk = artist.Pk;
        var q = Context.Albums.Where(a => a.AccountPk == artist.AccountPk &&
                                          (a.RemoteStatus == RemoteStatus.Available || a.SongsRaw.Any(s => s.RelFilePath != null)) &&
                                          (a.ArtistPk == pk || a.SongsRaw.Any(s => s.ArtistPk == pk)));
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(a => a.NameRaw != null && EF.Functions.Like(a.NameRaw, $"%{EscapeLike(searchText)}%", "\\"));
        if (onlyCached) q = q.Where(a => a.SongsRaw.Any(s => s.RelFilePath != null));
        return q.OrderBy(a => a.Year).ThenBy(a => EF.Functions.Collate(a.NameRaw, NoCase)).ThenBy(a => a.Id);
    }

    /// Songs of the artist (Swift: ArtistSongsItemsFetchedResultsController). With the album artists
    /// filter songs on albums of the artist are included too. Sorted alphabetically.
    public IQueryable<Song> QueryArtistSongs(Artist artist, ArtistCategoryFilter displayFilter, string searchText = "", bool onlyCached = false)
    {
        var pk = artist.Pk;
        var q = AvailableSongs().Where(s => s.AccountPk == artist.AccountPk);
        q = displayFilter == ArtistCategoryFilter.AlbumArtists
            ? q.Where(s => s.ArtistPk == pk || (s.Album != null && s.Album.ArtistPk == pk))
            : q.Where(s => s.ArtistPk == pk);
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(s => s.TitleRaw != null && EF.Functions.Like(s.TitleRaw, $"%{EscapeLike(searchText)}%", "\\"));
        if (onlyCached) q = q.Where(s => s.RelFilePath != null);
        return SortSongs(q, SongElementSortType.Name);
    }

    // --- genres ----------------------------------------------------------------------------------

    public IQueryable<Artist> QueryGenreArtists(Genre genre, string searchText = "", bool onlyCached = false)
    {
        var pk = genre.Pk;
        var q = Context.Artists.Where(a => a.GenrePk == pk &&
                                           (a.RemoteStatus == RemoteStatus.Available || a.SongsRaw.Any(s => s.RelFilePath != null)));
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(a => a.NameRaw != null && EF.Functions.Like(a.NameRaw, $"%{EscapeLike(searchText)}%", "\\"));
        if (onlyCached) q = q.Where(a => a.SongsRaw.Any(s => s.RelFilePath != null));
        return SortArtists(q, ArtistElementSortType.Name);
    }

    public IQueryable<Album> QueryGenreAlbums(Genre genre, string searchText = "", bool onlyCached = false)
    {
        var pk = genre.Pk;
        var q = Context.Albums.Where(a => a.GenrePk == pk &&
                                          (a.RemoteStatus == RemoteStatus.Available || a.SongsRaw.Any(s => s.RelFilePath != null)));
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(a => a.NameRaw != null && EF.Functions.Like(a.NameRaw, $"%{EscapeLike(searchText)}%", "\\"));
        if (onlyCached) q = q.Where(a => a.SongsRaw.Any(s => s.RelFilePath != null));
        return SortAlbums(q, AlbumElementSortType.Name);
    }

    public IQueryable<Song> QueryGenreSongs(Genre genre, string searchText = "", bool onlyCached = false)
    {
        var pk = genre.Pk;
        var q = AvailableSongs().Where(s => s.GenrePk == pk);
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(s => s.TitleRaw != null && EF.Functions.Like(s.TitleRaw, $"%{EscapeLike(searchText)}%", "\\"));
        if (onlyCached) q = q.Where(s => s.RelFilePath != null);
        return SortSongs(q, SongElementSortType.Name);
    }

    // --- podcasts --------------------------------------------------------------------------------

    /// Episodes of a podcast which are available to the user, newest first (Swift: PodcastEpisodesFetchedResultsController).
    public IQueryable<PodcastEpisode> QueryPodcastEpisodes(Podcast podcast, string searchText = "", bool onlyCached = false)
    {
        var pk = podcast.Pk;
        var q = UserAvailableEpisodes().Where(e => e.PodcastPk == pk);
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(e => e.TitleRaw != null && EF.Functions.Like(e.TitleRaw, $"%{EscapeLike(searchText)}%", "\\"));
        if (onlyCached) q = q.Where(e => e.RelFilePath != null);
        return SortEpisodesByPublishDate(q);
    }

    // --- music folders / directories -------------------------------------------------------------

    public IQueryable<MusicFolder> QueryMusicFolders(Account account, string searchText = "")
    {
        var q = Context.MusicFolders.Where(f => f.AccountPk == account.Pk);
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(f => EF.Functions.Like(f.Name, $"%{EscapeLike(searchText)}%", "\\"));
        return q.OrderBy(f => EF.Functions.Collate(f.Name, NoCase)).ThenBy(f => f.Id);
    }

    /// Top level directories of a music folder (Swift: MusicFolderDirectoriesFetchedResultsController).
    public IQueryable<MusicDirectory> QueryMusicFolderDirectories(MusicFolder folder, string searchText = "")
    {
        var pk = folder.Pk;
        var q = Context.Directories.Where(d => d.MusicFolderPk == pk);
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(d => d.NameRaw != null && EF.Functions.Like(d.NameRaw, $"%{EscapeLike(searchText)}%", "\\"));
        return SortDirectories(q);
    }

    public IQueryable<MusicDirectory> QuerySubdirectories(MusicDirectory directory, string searchText = "")
    {
        var pk = directory.Pk;
        var q = Context.Directories.Where(d => d.ParentPk == pk);
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(d => d.NameRaw != null && EF.Functions.Like(d.NameRaw, $"%{EscapeLike(searchText)}%", "\\"));
        return SortDirectories(q);
    }

    public IQueryable<Song> QueryDirectorySongs(MusicDirectory directory, string searchText = "", bool onlyCached = false)
    {
        var pk = directory.Pk;
        var q = Context.Songs.Where(s => s.DirectoryPk == pk);
        if (!string.IsNullOrEmpty(searchText)) q = q.Where(s => s.TitleRaw != null && EF.Functions.Like(s.TitleRaw, $"%{EscapeLike(searchText)}%", "\\"));
        if (onlyCached) q = q.Where(s => s.RelFilePath != null);
        return q.OrderBy(s => EF.Functions.Collate(s.TitleRaw, NoCase)).ThenBy(s => s.Id);
    }

    // --- radios ----------------------------------------------------------------------------------

    public IQueryable<Radio> QuerySortedRadios(Account account, string searchText = "") => SortRadios(QueryRadios(account, searchText));
}
