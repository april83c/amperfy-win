using Amperfy.Core.Api;

namespace Amperfy.Core.Downloads;

/// Download strategy for songs and podcast episodes (port of PlayableDownloadDelegate.swift).
public sealed class PlayableDownloadDelegate : IDownloadManagerDelegate
{
    /// max file size of an error response from an API
    public const int MaxFileSizeOfErrorResponse = 2_000;

    private readonly IBackendApi _backendApi;
    private readonly EmbeddedArtworkExtractor _artworkExtractor;
    private readonly INetworkMonitor _networkMonitor;
    private readonly CacheFileManager? _fileManager;

    private CacheFileManager FileManager => _fileManager ?? CacheFileManager.Shared;

    public PlayableDownloadDelegate(IBackendApi backendApi, EmbeddedArtworkExtractor artworkExtractor, INetworkMonitor networkMonitor, CacheFileManager? fileManager = null)
    {
        _backendApi = backendApi;
        _artworkExtractor = artworkExtractor;
        _networkMonitor = networkMonitor;
        _fileManager = fileManager;
    }

    /// Type of the downloads handled by this delegate (Swift: requestPredicate = onlyPlayablesPredicate).
    public static DownloadableType RequestType => DownloadableType.Playable;

    public int ParallelDownloadsCount => 4;

    public IReadOnlyDictionary<string, string> HttpHeaders => _backendApi.HttpHeaders;

    public async Task<Uri> PrepareDownloadAsync(IDownloadable downloadable, LibraryStorage storage)
    {
        if (downloadable is not AbstractPlayable playable) throw new DownloadException(DownloadError.FetchFailed);
        if (!_networkMonitor.IsConnectedToNetwork) throw new DownloadException(DownloadError.NoConnectivity);
        if (playable.IsCached) throw new DownloadException(DownloadError.AlreadyDownloaded);
        return await _backendApi.GenerateUrlForDownloadingPlayableAsync(playable.Info);
    }

    public ResponseError? ValidateDownloadedData(string? filePath, Uri? downloadUrl)
    {
        if (filePath is null)
        {
            return new ResponseError(ResponseErrorType.Api, message: "Invalid download", cleansedUrl: _backendApi.Cleanse(downloadUrl));
        }
        var data = FileManager.GetFileDataIfNotTooBig(filePath, MaxFileSizeOfErrorResponse);
        if (data is null) return null;
        return _backendApi.CheckForErrorResponse(new ApiDataResponse(data, downloadUrl));
    }

    public async Task CompletedDownloadAsync(IDownloadable downloadable, string tempFilePath, string? fileMimeType, LibraryStorage storage)
    {
        if (downloadable is not AbstractPlayable playable) return;
        try
        {
            SavePlayableData(playable, tempFilePath, fileMimeType, storage);
            await _artworkExtractor.ExtractEmbeddedArtworkAsync(playable, storage);
        }
        catch (Exception ex)
        {
            // ignore errors
            AmperfyLog.Info("PlayableDownloadDelegate", $"Completing download of {playable.DisplayString} failed: {ex.Message}");
        }
    }

    /// Moves the downloaded file into the cache and updates <see cref="AbstractPlayable.RelFilePath"/>.
    public void SavePlayableData(AbstractPlayable playable, string tempFilePath, string? fileMimeType, LibraryStorage storage)
    {
        playable.ContentTypeTranscoded = fileMimeType;
        // transcoding info needs to available to generate a correct file extension
        var relFilePath = FileManager.CreateRelPath(playable);
        if (relFilePath is null || playable.Account is not { } account)
        {
            storage.SaveContext();
            return;
        }
        try
        {
            FileManager.MoveItemIntoCache(tempFilePath, relFilePath, account.Info);
            playable.RelFilePath = relFilePath;
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("PlayableDownloadDelegate", $"Could not move download into cache: {ex.Message}");
            playable.RelFilePath = null;
        }
        storage.SaveContext();
    }

    public Task FailedDownloadAsync(IDownloadable downloadable, LibraryStorage storage) => Task.CompletedTask;
}
