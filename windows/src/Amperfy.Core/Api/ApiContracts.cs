namespace Amperfy.Core.Api;

public enum ParsedObjectType
{
    Artist,
    Album,
    Song,
    Playlist,
    Genre,
    Podcast,
    Cache,
}

public interface IParsedObjectNotifiable
{
    void NotifyParsedObject(ParsedObjectType parsedObjectType);
}

public interface ISyncCallbacks : IParsedObjectNotifiable
{
    void NotifySyncStarted(ParsedObjectType parsedObjectType, int totalCount);
}

public enum NowPlayingSongPosition
{
    Start,
    End,
}

public sealed class ApiDataResponse
{
    public byte[] Data { get; }
    public Uri? Url { get; }

    public ApiDataResponse(byte[] data, Uri? url)
    {
        Data = data;
        Url = url;
    }
}

public sealed class LyricsList
{
    public List<StructuredLyrics> Lyrics { get; set; } = [];

    public StructuredLyrics? GetFirstSyncedLyricsOrUnsyncedAsDefault() =>
        Lyrics.FirstOrDefault(l => l.Synced) ?? Lyrics.FirstOrDefault();
}

public sealed class StructuredLyrics
{
    /// ISO 639 language, "und" / "xxx" if unknown
    public string Lang { get; set; } = "";
    public bool Synced { get; set; }
    /// Ordered by start time (synced) or appearance order (unsynced)
    public List<LyricsLine> Line { get; set; } = [];
    public string? DisplayArtist { get; set; }
    public string? DisplayTitle { get; set; }
    /// Offset in milliseconds. Positive means lyrics appear sooner.
    public int Offset { get; set; }
}

public sealed class LyricsLine
{
    /// Start time in milliseconds relative to the track start (only for synced lyrics)
    public int? Start { get; set; }
    public string Value { get; set; } = "";

    public TimeSpan StartTime => TimeSpan.FromMilliseconds(Start ?? 0);
}

public sealed class CleansedUrl
{
    private readonly string _urlString;
    public CleansedUrl(string urlString) => _urlString = urlString;
    public string Description => _urlString;
    public override string ToString() => _urlString;
}

public interface IUrlCleanser
{
    CleansedUrl Cleanse(Uri? url);
}

public readonly record struct ArtworkRemoteInfo(string Id, string Type);

public sealed record AbstractPlayableInfo(string Id, int Pk, DerivedPlayableType Type, string? StreamId);

public enum ResponseErrorType
{
    Api,
    Xml,
    Resource,
}

public sealed class ResponseError : Exception
{
    public ResponseErrorType Type { get; }
    public int StatusCode { get; }
    public string ErrorMessage { get; }
    public CleansedUrl? CleansedUrl { get; }
    public byte[]? ResponseData { get; }

    public ResponseError(ResponseErrorType type, int statusCode = 0, string message = "", CleansedUrl? cleansedUrl = null, byte[]? data = null)
        : base(BuildDescription(type, statusCode, type == ResponseErrorType.Xml ? "XML response could not be parsed." : message))
    {
        Type = type;
        StatusCode = statusCode;
        ErrorMessage = type == ResponseErrorType.Xml ? "XML response could not be parsed." : message;
        CleansedUrl = cleansedUrl;
        ResponseData = data;
    }

    private static string BuildDescription(ResponseErrorType type, int statusCode, string message) => type switch
    {
        ResponseErrorType.Xml => message,
        _ => $"API error {statusCode}: {message}",
    };

    public ResponseErrorInfo AsInfo(string topic) => new(topic, StatusCode, ErrorMessage, CleansedUrl?.Description ?? "",
        ResponseData is null ? null : Encoding.UTF8.GetString(ResponseData));
}

public sealed record ResponseErrorInfo(string Topic, int StatusCode, string Message, string CleansedURL, string? Data);

public enum BackendErrorKind
{
    InvalidUrl,
    NoCredentials,
    PersistentSaveFailed,
    NotSupported,
    IncorrectServerBehavior,
}

public sealed class BackendError : Exception
{
    public BackendErrorKind Kind { get; }

    public BackendError(BackendErrorKind kind, string? message = null) : base(Describe(kind, message)) => Kind = kind;

    private static string Describe(BackendErrorKind kind, string? message) => kind switch
    {
        BackendErrorKind.InvalidUrl => "Provided URL is invalid.",
        BackendErrorKind.NoCredentials => "Internal error: no credentials provided.",
        BackendErrorKind.PersistentSaveFailed => "Change could not be saved.",
        BackendErrorKind.NotSupported => "Requested functionality is not supported.",
        _ => $"Server didn't behave as expected: {message}",
    };

    public static BackendError InvalidUrl => new(BackendErrorKind.InvalidUrl);
    public static BackendError NoCredentials => new(BackendErrorKind.NoCredentials);
    public static BackendError NotSupported => new(BackendErrorKind.NotSupported);
}

public enum AuthenticationErrorKind
{
    NotAbleToLogin,
    InvalidUrl,
    RequestStatusError,
    DownloadError,
}

public sealed class AuthenticationError : Exception
{
    public AuthenticationErrorKind Kind { get; }

    public AuthenticationError(AuthenticationErrorKind kind, string? message = null) : base(Describe(kind, message)) => Kind = kind;

    private static string Describe(AuthenticationErrorKind kind, string? message) => kind switch
    {
        AuthenticationErrorKind.NotAbleToLogin => "Not able to login, please check credentials!",
        AuthenticationErrorKind.InvalidUrl => "Server URL is invalid!",
        AuthenticationErrorKind.RequestStatusError => $"Requesting server URL finished with status response error code '{message}'!",
        _ => message ?? "Download error",
    };
}

/// Library synchronisation with the server (Subsonic / Ampache implementation).
/// All methods are called on the main (UI) thread; the implementation awaits network I/O
/// off-thread and applies the results to the storage on the main thread.
public interface ILibrarySyncer
{
    Task SyncInitialAsync(ISyncCallbacks? statusNotifier);
    Task SyncAsync(Genre genre);
    Task SyncAsync(Artist artist);
    Task SyncAsync(Album album);
    Task SyncAsync(Song song);
    Task SyncAsync(Podcast podcast);
    Task SyncNewestAlbumsAsync(int offset, int count);
    Task SyncRecentAlbumsAsync(int offset, int count);
    Task SyncNewestPodcastEpisodesAsync();
    Task SyncFavoriteLibraryElementsAsync();
    Task SyncRadiosAsync();
    Task SyncDownPlaylistsWithoutSongsAsync();
    Task SyncDownAsync(Playlist playlist);
    Task SyncUploadPlaylistNameAsync(Playlist playlist);
    Task SyncUploadPlaylistAddSongsAsync(Playlist playlist, IReadOnlyList<Song> songs);
    Task SyncUploadPlaylistDeleteSongAsync(Playlist playlist, int index);
    Task SyncUploadPlaylistOrderAsync(Playlist playlist);
    Task SyncUploadPlaylistDeleteAsync(string playlistId);
    Task SyncDownPodcastsWithoutEpisodesAsync();
    Task SearchArtistsAsync(string searchText);
    Task SearchAlbumsAsync(string searchText);
    Task SearchSongsAsync(string searchText);
    Task SyncMusicFoldersAsync();
    Task SyncIndexesAsync(MusicFolder musicFolder);
    Task SyncAsync(MusicDirectory directory);
    Task RequestRandomSongsAsync(Playlist playlist, int count);
    Task<List<Song>> RequestSimilarSongsAsync(Song song, int count);
    Task RequestPodcastEpisodeDeleteAsync(PodcastEpisode podcastEpisode);
    Task SyncNowPlayingAsync(Song song, NowPlayingSongPosition songPosition);
    Task ScrobbleAsync(Song song, DateTime? date);
    Task SetRatingAsync(Song song, int rating);
    Task SetRatingAsync(Album album, int rating);
    Task SetRatingAsync(Artist artist, int rating);
    Task SetFavoriteAsync(Song song, bool isFavorite);
    Task SetFavoriteAsync(Album album, bool isFavorite);
    Task SetFavoriteAsync(Artist artist, bool isFavorite);
    /// Parses a downloaded lyrics file (relative path in the cache) into a LyricsList.
    Task<LyricsList> ParseLyricsAsync(string relFilePath);
}

public sealed record TranscodingInfo(CacheTranscodingFormatPreference? Format, StreamingMaxBitratePreference? Bitrate)
{
    public string Description => $"Format: {Format?.Description() ?? "-"}, Bitrate: {Bitrate?.Description() ?? "-"}";
}

/// Server API (Subsonic or Ampache).
public interface IBackendApi : IUrlCleanser
{
    string ClientApiVersion { get; }
    string ServerApiVersion { get; }
    IReadOnlyDictionary<string, string> HttpHeaders { get; }
    void ProvideCredentials(LoginCredentials credentials);
    Task IsAuthenticationValidAsync(LoginCredentials credentials);
    Task<Uri> GenerateUrlForDownloadingPlayableAsync(AbstractPlayableInfo playableInfo);
    Task<Uri> GenerateUrlForStreamingPlayableAsync(AbstractPlayableInfo playableInfo, StreamingMaxBitratePreference maxBitrate, StreamingFormatPreference formatPreference);
    Task<Uri> GenerateUrlForArtworkAsync(Artwork artwork);
    ResponseError? CheckForErrorResponse(ApiDataResponse response);
    ILibrarySyncer CreateLibrarySyncer(Account account, LibraryStorage storage);
    Downloads.IDownloadManagerDelegate CreateArtworkDownloadDelegate();
    ArtworkRemoteInfo? ExtractArtworkInfoFromUrl(string urlString);
}
