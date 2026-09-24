using Amperfy.Core.Downloads;

namespace Amperfy.Core.Api.Ampache;

/// Download strategy for Ampache artworks (image.php).
public sealed class AmpacheArtworkDownloadDelegate : IDownloadManagerDelegate
{
    /// max file size of an error response from an API
    private const int MaxFileSizeOfErrorResponse = 2_000;

    private readonly AmpacheXmlServerApi _ampacheXmlServerApi;
    private readonly INetworkMonitor _networkMonitor;
    private static CacheFileManager FileManager => CacheFileManager.Shared;

    public AmpacheArtworkDownloadDelegate(AmpacheXmlServerApi ampacheXmlServerApi, INetworkMonitor networkMonitor)
    {
        _ampacheXmlServerApi = ampacheXmlServerApi;
        _networkMonitor = networkMonitor;
    }

    public int ParallelDownloadsCount => 2;

    public IReadOnlyDictionary<string, string> HttpHeaders => _ampacheXmlServerApi.HttpHeaders;

    public async Task<Uri> PrepareDownloadAsync(IDownloadable downloadable, LibraryStorage storage)
    {
        if (downloadable is not Artwork artwork) throw new DownloadException(DownloadError.FetchFailed);
        if (!_networkMonitor.IsConnectedToNetwork) throw new DownloadException(DownloadError.NoConnectivity);
        var artworkRemoteInfo = artwork.RemoteInfo;
        return await _ampacheXmlServerApi.GenerateUrlForArtworkAsync(artworkRemoteInfo);
    }

    public ResponseError? ValidateDownloadedData(string? filePath, Uri? downloadUrl)
    {
        if (filePath is null)
            return new ResponseError(ResponseErrorType.Api, message: "Invalid download", cleansedUrl: downloadUrl is null ? null : _ampacheXmlServerApi.Cleanse(downloadUrl));
        if (GetFileDataIfNotTooBig(filePath, MaxFileSizeOfErrorResponse) is not { } data) return null;
        return _ampacheXmlServerApi.CheckForErrorResponse(new ApiDataResponse(data, downloadUrl));
    }

    /// Swift CacheFileManager.getFileDataIfNotToBig
    private static byte[]? GetFileDataIfNotTooBig(string filePath, long maxFileSize)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length > maxFileSize) return null;
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
        var artworkRemoteInfo = artwork.RemoteInfo;
        var relFilePath = HandleCustomImage(tempFilePath, artworkRemoteInfo, artwork);
        artwork.Status = ImageStatus.CustomImage;
        artwork.RelFilePath = relFilePath;
        storage.SaveContext();
        return Task.CompletedTask;
    }

    private string? HandleCustomImage(string fileUrl, ArtworkRemoteInfo artworkRemoteInfo, Artwork artwork)
    {
        var account = _ampacheXmlServerApi.Account ?? artwork.Account?.Info;
        if (account is null) return null;
        var relFilePath = CacheFileManager.GetRelArtworkFilePath(account, artworkRemoteInfo.Id, artworkRemoteInfo.Type);
        try
        {
            FileManager.MoveItemIntoCache(fileUrl, relFilePath, account);
            return relFilePath;
        }
        catch (Exception ex)
        {
            AmperfyLog.Error("Ampache", $"Artwork could not be moved into the cache: {ex.Message}");
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
