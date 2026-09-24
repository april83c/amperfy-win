using Amperfy.Core.Downloads;

namespace Amperfy.Core.Api.Subsonic;

/// Download strategy for Subsonic artworks ("getCoverArt").
public sealed class SubsonicArtworkDownloadDelegate : IDownloadManagerDelegate
{
    /// max file size of an error response from an API
    private const int MaxFileSizeOfErrorResponse = 2_000;

    private readonly SubsonicServerApi _subsonicServerApi;
    private readonly INetworkMonitor _networkMonitor;
    private static CacheFileManager FileManager => CacheFileManager.Shared;

    public SubsonicArtworkDownloadDelegate(SubsonicServerApi subsonicServerApi, INetworkMonitor networkMonitor)
    {
        _subsonicServerApi = subsonicServerApi;
        _networkMonitor = networkMonitor;
    }

    public int ParallelDownloadsCount => 2;

    public IReadOnlyDictionary<string, string> HttpHeaders => _subsonicServerApi.HttpHeaders;

    public async Task<Uri> PrepareDownloadAsync(IDownloadable downloadable, LibraryStorage storage)
    {
        if (downloadable is not Artwork artwork) throw new DownloadException(DownloadError.FetchFailed);
        if (!_networkMonitor.IsConnectedToNetwork) throw new DownloadException(DownloadError.NoConnectivity);
        var artworkId = artwork.Id;
        return await _subsonicServerApi.GenerateUrlForArtworkAsync(artworkId);
    }

    public ResponseError? ValidateDownloadedData(string? filePath, Uri? downloadUrl)
    {
        if (filePath is null)
        {
            return new ResponseError(ResponseErrorType.Api, message: "Invalid download",
                cleansedUrl: downloadUrl is null ? null : _subsonicServerApi.Cleanse(downloadUrl));
        }
        var data = GetFileDataIfNotTooBig(filePath, MaxFileSizeOfErrorResponse);
        if (data is null) return null;
        return _subsonicServerApi.CheckForErrorResponse(new ApiDataResponse(data, downloadUrl));
    }

    private static byte[]? GetFileDataIfNotTooBig(string filePath, int maxFileSize)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length >= maxFileSize) return null;
            return File.ReadAllBytes(filePath);
        }
        catch
        {
            return null;
        }
    }

    public Task CompletedDownloadAsync(IDownloadable downloadable, string tempFilePath, string? fileMimeType, LibraryStorage storage)
    {
        if (downloadable is not Artwork artwork) return Task.CompletedTask;
        var relFilePath = HandleCustomImage(tempFilePath, artwork.RemoteInfo);
        artwork.Status = ImageStatus.CustomImage;
        artwork.RelFilePath = relFilePath;
        storage.SaveContext();
        return Task.CompletedTask;
    }

    /// Moves the downloaded image into the artwork cache. Returns the relative path (null on error).
    public string? HandleCustomImage(string tempFilePath, ArtworkRemoteInfo artworkRemoteInfo)
    {
        if (_subsonicServerApi.Account is not { } account) return null;
        var relFilePath = CacheFileManager.GetRelArtworkFilePath(account, artworkRemoteInfo.Id, artworkRemoteInfo.Type);
        try
        {
            FileManager.MoveItemIntoCache(tempFilePath, relFilePath, account);
            return relFilePath;
        }
        catch (Exception ex)
        {
            AmperfyLog.Error("Subsonic", $"Artwork could not be moved into the cache: {ex.Message}");
            return null;
        }
    }

    public Task FailedDownloadAsync(IDownloadable downloadable, LibraryStorage storage)
    {
        if (downloadable is not Artwork artwork) return Task.CompletedTask;
        artwork.MarkErrorIfNeeded();
        storage.SaveContext();
        return Task.CompletedTask;
    }
}
