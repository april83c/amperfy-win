namespace Amperfy.Core.Model;

public enum RemoteStatus : short
{
    Available = 0,
    Deleted = 1,
}

public enum DerivedPlayableType
{
    Song,
    PodcastEpisode,
    Radio,
}

public enum ImageStatus : short
{
    IsDefaultImage = 0,
    NotChecked = 1,
    CustomImage = 2,
    FetchError = 3,
}

/// Subsonic: new, downloading, completed, error, deleted, skipped.
/// Ampache: pending -> downloading, completed.
public enum PodcastEpisodeRemoteStatus : short
{
    Undefined = 0,
    New = 1,
    Downloading = 2,
    Completed = 3,
    Error = 4,
    Deleted = 5,
    Skipped = 6,
}

public static class PodcastEpisodeRemoteStatusExtensions
{
    public static PodcastEpisodeRemoteStatus Create(string text) => text switch
    {
        "new" => PodcastEpisodeRemoteStatus.New,
        "downloading" => PodcastEpisodeRemoteStatus.Downloading,
        "completed" => PodcastEpisodeRemoteStatus.Completed,
        "error" => PodcastEpisodeRemoteStatus.Error,
        "deleted" => PodcastEpisodeRemoteStatus.Deleted,
        "skipped" => PodcastEpisodeRemoteStatus.Skipped,
        "Pending" => PodcastEpisodeRemoteStatus.Downloading,
        "Completed" => PodcastEpisodeRemoteStatus.Completed,
        _ => PodcastEpisodeRemoteStatus.Undefined,
    };
}

public enum PodcastEpisodeUserStatus
{
    SyncingOnServer,
    AvailableOnServer,
    Cached,
    Deleted,
}

public static class PodcastEpisodeUserStatusExtensions
{
    public static string Description(this PodcastEpisodeUserStatus s) => s switch
    {
        PodcastEpisodeUserStatus.SyncingOnServer => "Server syncing",
        PodcastEpisodeUserStatus.AvailableOnServer => "Available",
        PodcastEpisodeUserStatus.Cached => "Cached",
        PodcastEpisodeUserStatus.Deleted => "Deleted on server",
        _ => "",
    };
}

public enum LogEntryType : short
{
    ApiError = 0,
    Error = 1,
    Info = 2,
    Debug = 3,
}

public static class LogEntryTypeExtensions
{
    public static string Description(this LogEntryType t) => t switch
    {
        LogEntryType.ApiError => "API Error",
        LogEntryType.Error => "Error",
        LogEntryType.Info => "Info",
        LogEntryType.Debug => "Debug",
        _ => "",
    };
}

public enum PlayerMode : short
{
    Music = 0,
    Podcast = 1,
}

public static class PlayerModeExtensions
{
    public static PlayerMode NextMode(this PlayerMode m) => m == PlayerMode.Music ? PlayerMode.Podcast : PlayerMode.Music;
    public static string Description(this PlayerMode m) => m == PlayerMode.Music ? "Music" : "Podcast";
    public static string PlayableName(this PlayerMode m) => m == PlayerMode.Music ? "Song" : "Podcast Episode";
}

public enum RepeatMode : short
{
    Off = 0,
    All = 1,
    Single = 2,
}

public static class RepeatModeExtensions
{
    public static RepeatMode NextMode(this RepeatMode m) => m switch
    {
        RepeatMode.Off => RepeatMode.All,
        RepeatMode.All => RepeatMode.Single,
        _ => RepeatMode.Off,
    };

    public static string Description(this RepeatMode m) => m switch
    {
        RepeatMode.Off => "Off",
        RepeatMode.All => "All",
        _ => "Single",
    };
}

public enum PlaybackRate
{
    Dot5 = 0,
    Dot75,
    One,
    OneDot25,
    OneDot5,
    OneDot75,
    Two,
}

public static class PlaybackRateExtensions
{
    public static PlaybackRate Create(double playbackRate)
    {
        if (playbackRate < 0.4) return PlaybackRate.One;
        if (playbackRate < 0.6) return PlaybackRate.Dot5;
        if (playbackRate < 0.8) return PlaybackRate.Dot75;
        if (playbackRate < 1.1) return PlaybackRate.One;
        if (playbackRate < 1.3) return PlaybackRate.OneDot25;
        if (playbackRate < 1.6) return PlaybackRate.OneDot5;
        if (playbackRate < 1.8) return PlaybackRate.OneDot75;
        if (playbackRate < 2.1) return PlaybackRate.Two;
        return PlaybackRate.One;
    }

    public static double AsDouble(this PlaybackRate r) => r switch
    {
        PlaybackRate.Dot5 => 0.5,
        PlaybackRate.Dot75 => 0.75,
        PlaybackRate.One => 1,
        PlaybackRate.OneDot25 => 1.25,
        PlaybackRate.OneDot5 => 1.5,
        PlaybackRate.OneDot75 => 1.75,
        PlaybackRate.Two => 2,
        _ => 1,
    };

    public static string Description(this PlaybackRate r) => r switch
    {
        PlaybackRate.Dot5 => "0.5x",
        PlaybackRate.Dot75 => "0.75x",
        PlaybackRate.One => "1x",
        PlaybackRate.OneDot25 => "1.25x",
        PlaybackRate.OneDot5 => "1.5x",
        PlaybackRate.OneDot75 => "1.75x",
        PlaybackRate.Two => "2x",
        _ => "1x",
    };
}

public enum PlayerQueueType
{
    Prev = 0,
    User = 2,
    Next = 3,
}

public static class PlayerQueueTypeExtensions
{
    public static string Description(this PlayerQueueType t) => t switch
    {
        PlayerQueueType.Prev => "Previous",
        PlayerQueueType.User => "Next in Queue",
        _ => "Next",
    };
}

public readonly record struct PlayerIndex(PlayerQueueType QueueType, int Index);

public enum ServerApiType
{
    Ampache = 1,
    Subsonic = 2,
}

public enum BackendApiType
{
    NotDetected = 0,
    Ampache = 1,
    Subsonic = 2,
    SubsonicLegacy = 3,
}

public static class BackendApiTypeExtensions
{
    public static string Description(this BackendApiType t) => t switch
    {
        BackendApiType.NotDetected => "NotDetected",
        BackendApiType.Ampache => "Ampache",
        BackendApiType.Subsonic => "Subsonic",
        BackendApiType.SubsonicLegacy => "Subsonic (legacy login)",
        _ => "",
    };

    public static string SelectorDescription(this BackendApiType t) => t switch
    {
        BackendApiType.NotDetected => "Auto-Detect",
        BackendApiType.Ampache => "Ampache",
        BackendApiType.Subsonic => "Subsonic",
        BackendApiType.SubsonicLegacy => "Subsonic (legacy login)",
        _ => "",
    };

    public static ServerApiType? AsServerApiType(this BackendApiType t) => t switch
    {
        BackendApiType.Ampache => ServerApiType.Ampache,
        BackendApiType.Subsonic => ServerApiType.Subsonic,
        BackendApiType.SubsonicLegacy => ServerApiType.Subsonic,
        _ => null,
    };
}

public enum ArtworkType
{
    Song,
    Album,
    Genre,
    Artist,
    Podcast,
    PodcastEpisode,
    Playlist,
    Folder,
    Radio,
}

public enum PlayableContainerBaseType
{
    Song = 0,
    PodcastEpisode,
    Album,
    Artist,
    Genre,
    Playlist,
    Podcast,
    Directory,
    Radio,
}

public static class PlayableContainerBaseTypeExtensions
{
    public static string DisplayString(this PlayableContainerBaseType t) => t switch
    {
        PlayableContainerBaseType.Song => "Song",
        PlayableContainerBaseType.PodcastEpisode => "Podcast Episode",
        PlayableContainerBaseType.Album => "Album",
        PlayableContainerBaseType.Artist => "Artist",
        PlayableContainerBaseType.Genre => "Genre",
        PlayableContainerBaseType.Playlist => "Playlist",
        PlayableContainerBaseType.Podcast => "Podcast",
        PlayableContainerBaseType.Directory => "Directory",
        PlayableContainerBaseType.Radio => "Radio",
        _ => "",
    };
}

public enum DetailType
{
    Short,
    Long,
    NoCountInfo,
}

public enum PlaylistSearchCategory
{
    All = 0,
    Cached = 1,
    UserOnly = 2,
    SmartOnly = 3,
}

public sealed record RadioNowPlayingInfo(string Title, string Artist)
{
    public bool IsEmpty => string.IsNullOrEmpty(Title) && string.IsNullOrEmpty(Artist);
}
