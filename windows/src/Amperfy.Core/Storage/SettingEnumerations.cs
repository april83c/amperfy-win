namespace Amperfy.Core.Storage;

public enum ArtworkDownloadSetting
{
    UpdateOncePerSession = 0,
    OnlyOnce = 1,
    Never = 2,
}

public enum ArtworkDisplayPreference
{
    Id3TagOnly = 0,
    ServerArtworkOnly = 1,
    PreferServerArtwork = 2,
    PreferId3Tag = 3,
}

public enum StreamingMaxBitratePreference
{
    NoLimit = 0,
    Limit32 = 32,
    Limit64 = 64,
    Limit96 = 96,
    Limit128 = 128,
    Limit192 = 192,
    Limit256 = 256,
    Limit320 = 320,
}

public enum StreamingFormatPreference
{
    Mp3 = 0,
    Raw = 1,
    /// omit the format to let the server decide which codec should be used
    ServerConfig = 2,
}

public enum CacheTranscodingFormatPreference
{
    Raw = 0,
    Mp3 = 1,
    ServerConfig = 2,
}

public enum SyncCompletionStatus
{
    Completed = 0,
    Skipped = 1,
    Aborted = 2,
}

public enum ThemePreference
{
    Blue = 0,
    Green = 1,
    Red = 2,
    Yellow = 3,
    Orange = 4,
    Purple = 5,
}

public enum VisualizerType
{
    Ring,
    Waveform,
    SpectrumBars,
    GenerativeArt,
}

public enum AppearanceMode
{
    System = 0,
    Light = 1,
    Dark = 2,
}

public enum ScreenLockPreventionPreference
{
    Always = 0,
    Never = 1,
    OnlyIfCharging = 2,
}

public enum ArtistElementSortType
{
    Name = 0,
    Rating = 1,
    Newest = 2,
    Duration = 3,
}

public enum AlbumElementSortType
{
    Name = 0,
    Rating = 1,
    Newest = 2,
    Artist = 3,
    Duration = 4,
    Year = 5,
    Recent = 6,
}

public enum PlaylistSortType
{
    Name = 0,
    LastPlayed = 1,
    LastChanged = 2,
    Duration = 3,
}

public enum SongElementSortType
{
    Name = 0,
    Rating = 1,
    AddedDate = 2,
    Duration = 3,
    StarredDate = 4,
}

public enum DisplayCategoryFilter
{
    All,
    Newest,
    Recent,
    Favorites,
}

public enum ArtistCategoryFilter
{
    All = 0,
    Favorites = 1,
    AlbumArtists = 2,
}

public enum AlbumsDisplayStyle
{
    Table = 0,
    Grid = 1,
}

public enum PodcastsShowType
{
    Podcasts = 0,
    EpisodesSortedByReleaseDate = 1,
}

public enum PlayerDisplayStyle
{
    Compact = 0,
    Large = 1,
}

public enum SectionIndexType
{
    Alphabet = 0,
    Rating = 1,
    NewestOrRecent = 2,
    DurationSong = 3,
    DurationAlbum = 4,
    DurationArtist = 5,
    None = 6,
    Year = 7,
}

/// Home screen sections. Add new sections always at the end to keep the int values consistent.
public enum HomeSection
{
    LastTimePlayedPlaylists,
    RecentlyPlayedAlbums,
    NewestAlbums,
    RandomAlbums,
    NewestPodcastEpisodes,
    Podcasts,
    Radios,
    RandomArtists,
    RandomGenres,
    RandomSongs,
}

public enum LibraryDisplayType
{
    Artists = 0,
    Albums = 1,
    Songs = 2,
    Genres = 3,
    Directories = 4,
    Playlists = 5,
    Podcasts = 6,
    Downloads = 7,
    FavoriteSongs = 8,
    FavoriteAlbums = 9,
    FavoriteArtists = 10,
    NewestAlbums = 12,
    RecentAlbums = 13,
    Radios = 14,
}

public enum LibrarySyncVersion
{
    V6 = 0, V7, V8, V9, V10, V11, V12, V13, V14, V15, V16, V17, V18, V19, V20,
    /// Account support
    V21,
}

public static class SettingEnumerationExtensions
{
    public const ArtworkDownloadSetting DefaultArtworkDownloadSetting = ArtworkDownloadSetting.OnlyOnce;
    public const LibrarySyncVersion NewestLibrarySyncVersion = LibrarySyncVersion.V21;

    public static string Description(this ArtworkDownloadSetting s) => s switch
    {
        ArtworkDownloadSetting.UpdateOncePerSession => "Download once per session (change detection)",
        ArtworkDownloadSetting.OnlyOnce => "Download only once",
        _ => "Never",
    };

    public static string Description(this ArtworkDisplayPreference p) => p switch
    {
        ArtworkDisplayPreference.Id3TagOnly => "Only ID3 tag artworks",
        ArtworkDisplayPreference.ServerArtworkOnly => "Only server artworks",
        ArtworkDisplayPreference.PreferServerArtwork => "Prefer server artwork over ID3 tag",
        _ => "Prefer ID3 tag over server artwork",
    };

    public static string Description(this StreamingMaxBitratePreference p) =>
        p == StreamingMaxBitratePreference.NoLimit ? "No Limit (default)" : $"{(int)p} kbps";

    public static string ShortInfo(this StreamingFormatPreference p) => p switch
    {
        StreamingFormatPreference.Mp3 => "MP3",
        StreamingFormatPreference.Raw => "RAW",
        _ => "",
    };

    public static string Description(this StreamingFormatPreference p) => p switch
    {
        StreamingFormatPreference.Mp3 => "mp3 (default)",
        StreamingFormatPreference.Raw => "Raw/Original",
        _ => "Server chooses Codec",
    };

    public static string Description(this CacheTranscodingFormatPreference p) => p switch
    {
        CacheTranscodingFormatPreference.Mp3 => "mp3 (default)",
        CacheTranscodingFormatPreference.Raw => "Raw/Original",
        _ => "Server chooses Codec",
    };

    public static string Description(this SyncCompletionStatus s) => s.ToString();

    public static string Description(this ThemePreference t) => t.ToString();

    public static string DisplayName(this VisualizerType v) => v switch
    {
        VisualizerType.Ring => "Ring",
        VisualizerType.Waveform => "Waveform",
        VisualizerType.SpectrumBars => "Spectrum Bars",
        _ => "Generative Art",
    };

    public static string Description(this PlayerDisplayStyle s) => s == PlayerDisplayStyle.Compact ? "Compact" : "Large";

    public static PlayerDisplayStyle NextStyle(this PlayerDisplayStyle s) =>
        s == PlayerDisplayStyle.Compact ? PlayerDisplayStyle.Large : PlayerDisplayStyle.Compact;

    public static SectionIndexType AsSectionIndexType(this ArtistElementSortType t) => t switch
    {
        ArtistElementSortType.Name => SectionIndexType.Alphabet,
        ArtistElementSortType.Rating => SectionIndexType.Rating,
        ArtistElementSortType.Newest => SectionIndexType.NewestOrRecent,
        _ => SectionIndexType.DurationArtist,
    };

    public static SectionIndexType AsSectionIndexType(this AlbumElementSortType t) => t switch
    {
        AlbumElementSortType.Name => SectionIndexType.Alphabet,
        AlbumElementSortType.Rating => SectionIndexType.Rating,
        AlbumElementSortType.Newest => SectionIndexType.NewestOrRecent,
        AlbumElementSortType.Artist => SectionIndexType.Alphabet,
        AlbumElementSortType.Duration => SectionIndexType.DurationAlbum,
        AlbumElementSortType.Year => SectionIndexType.Year,
        _ => SectionIndexType.NewestOrRecent,
    };

    public static SectionIndexType AsSectionIndexType(this PlaylistSortType t) =>
        t == PlaylistSortType.Name ? SectionIndexType.Alphabet : SectionIndexType.None;

    public static SectionIndexType AsSectionIndexType(this SongElementSortType t) => t switch
    {
        SongElementSortType.Name => SectionIndexType.Alphabet,
        SongElementSortType.Rating => SectionIndexType.Rating,
        SongElementSortType.AddedDate => SectionIndexType.NewestOrRecent,
        SongElementSortType.Duration => SectionIndexType.DurationSong,
        _ => SectionIndexType.None,
    };

    public static bool HasSectionTitles(this SongElementSortType t) => t is SongElementSortType.Name or SongElementSortType.Rating or SongElementSortType.Duration;

    public static string Title(this HomeSection s) => s switch
    {
        HomeSection.RecentlyPlayedAlbums => "Recently Played Albums",
        HomeSection.NewestAlbums => "Newest Albums",
        HomeSection.RandomAlbums => "Random Albums",
        HomeSection.LastTimePlayedPlaylists => "Recently Played Playlists",
        HomeSection.NewestPodcastEpisodes => "Newest Podcast Episodes",
        HomeSection.Podcasts => "Podcasts",
        HomeSection.Radios => "Radios",
        HomeSection.RandomArtists => "Random Artists",
        HomeSection.RandomGenres => "Random Genres",
        _ => "Random Songs",
    };

    public static bool IsRandomSection(this HomeSection s) =>
        s is HomeSection.RandomAlbums or HomeSection.RandomArtists or HomeSection.RandomGenres or HomeSection.RandomSongs;

    public static readonly IReadOnlyList<HomeSection> DefaultHomeSections =
        [HomeSection.RandomAlbums, HomeSection.RecentlyPlayedAlbums, HomeSection.LastTimePlayedPlaylists, HomeSection.NewestAlbums];

    public static string DisplayName(this LibraryDisplayType t) => t switch
    {
        LibraryDisplayType.Artists => "Artists",
        LibraryDisplayType.Albums => "Albums",
        LibraryDisplayType.Songs => "Songs",
        LibraryDisplayType.Genres => "Genres",
        LibraryDisplayType.Directories => "Directories",
        LibraryDisplayType.Playlists => "Playlists",
        LibraryDisplayType.Podcasts => "Podcasts",
        LibraryDisplayType.Downloads => "Downloads",
        LibraryDisplayType.FavoriteSongs => "Favorite Songs",
        LibraryDisplayType.FavoriteAlbums => "Favorite Albums",
        LibraryDisplayType.FavoriteArtists => "Favorite Artists",
        LibraryDisplayType.NewestAlbums => "Newest Albums",
        LibraryDisplayType.RecentAlbums => "Recently Played Albums",
        _ => "Radios",
    };
}
