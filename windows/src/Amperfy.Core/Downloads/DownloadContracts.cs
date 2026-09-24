using Amperfy.Core.Api;

namespace Amperfy.Core.Downloads;

public enum DownloadError
{
    UrlInvalid = 1,
    NoConnectivity = 2,
    AlreadyDownloaded = 3,
    FetchFailed = 4,
    EmptyFile = 5,
    ApiErrorResponse = 6,
    Canceled = 7,
    FileManagerError = 8,
}

public static class DownloadErrorExtensions
{
    public static string Description(this DownloadError e) => e switch
    {
        DownloadError.UrlInvalid => "Invalid URL",
        DownloadError.NoConnectivity => "No Connectivity",
        DownloadError.AlreadyDownloaded => "Already Downloaded",
        DownloadError.FetchFailed => "Fetch Failed",
        DownloadError.EmptyFile => "File is empty",
        DownloadError.ApiErrorResponse => "API Error",
        DownloadError.Canceled => "Canceled",
        _ => "File Manager Error",
    };
}

public sealed class DownloadException : Exception
{
    public DownloadError Error { get; }
    public DownloadException(DownloadError error) : base(error.Description()) => Error = error;
}

public enum DownloadableType
{
    Playable,
    Artwork,
    Unknown,
}

/// Something that can be downloaded (a playable or an artwork).
public interface IDownloadable
{
    int Pk { get; }
    bool IsCached { get; }
    string DisplayString { get; }
    DownloadableType DownloadableType { get; }
}

public static class DownloadableExtensions
{
    /// Unique download id of an element ("playable-12" / "artwork-3")
    public static string UniqueId(this IDownloadable d) => $"{d.DownloadableType.ToString().ToLowerInvariant()}-{d.Pk}";
}

public interface IDownloadManageable
{
    void Download(IDownloadable obj);
    void Download(IEnumerable<IDownloadable> objects);
    void RemoveFinishedDownload(IDownloadable obj);
    void RemoveFinishedDownload(IEnumerable<IDownloadable> objects);
    void ClearFinishedDownloads();
    void ResetFailedDownloads();
    void CancelDownloads();
    void Start();
    void Stop();
}

/// Strategy for a download manager (playable files vs artworks). All methods run on the main thread.
public interface IDownloadManagerDelegate
{
    int ParallelDownloadsCount { get; }
    IReadOnlyDictionary<string, string> HttpHeaders { get; }
    Task<Uri> PrepareDownloadAsync(IDownloadable downloadable, LibraryStorage storage);
    /// Validates the downloaded file (e.g. server returned an error document instead of audio).
    ResponseError? ValidateDownloadedData(string? filePath, Uri? downloadUrl);
    Task CompletedDownloadAsync(IDownloadable downloadable, string tempFilePath, string? fileMimeType, LibraryStorage storage);
    Task FailedDownloadAsync(IDownloadable downloadable, LibraryStorage storage);
}
