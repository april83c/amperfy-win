using System.Diagnostics;
using Amperfy.Core.Api;

namespace Amperfy.Core.Downloads;

/// Filters elements before they are requested for download (Swift: PreDownloadIsValidCB).
public delegate IReadOnlyList<IDownloadable> PreDownloadIsValidCallback(IReadOnlyList<IDownloadable> downloadables);

/// Progress of an active transfer (raised on the main thread, throttled).
public sealed class DownloadProgressEventArgs : EventArgs
{
    public Model.Download Download { get; }
    /// Unique id of the downloaded element (<see cref="DownloadableExtensions.UniqueId"/>).
    public string Id { get; }
    public long BytesReceived { get; }
    public long? TotalBytes { get; }
    /// 0...1 (0 if the total size is unknown)
    public float Progress { get; }

    public DownloadProgressEventArgs(Model.Download download, string id, long bytesReceived, long? totalBytes, float progress)
    {
        Download = download;
        Id = id;
        BytesReceived = bytesReceived;
        TotalBytes = totalBytes;
        Progress = progress;
    }
}

/// Downloads playables or artworks (port of DownloadManager.swift + DownloadManagerSessionExtension.swift).
///
/// Swift used a background URLSession and an OperationQueue; here every transfer is an
/// <see cref="HttpClient"/> request streamed into a temp file (on the thread pool) with at most
/// <see cref="IDownloadManagerDelegate.ParallelDownloadsCount"/> parallel transfers. All state
/// (queue, entities, events) lives on the main thread.
public sealed class DownloadManager : IDownloadManageable, IDisposable
{
    /// Minimum interval between two progress updates of one transfer.
    public static TimeSpan ProgressUpdateInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    private const int CopyBufferSize = 81920;
    private static readonly Lazy<HttpClient> DefaultHttpClient = new(() => AmperfyHttp.CreateClient(TimeSpan.FromMinutes(5)));

    private readonly Account _account;
    private readonly LibraryStorage _library;
    private readonly DownloadRequestManager _requestManager;
    private readonly Func<IDownloadManagerDelegate> _getDownloadDelegate;
    private readonly EventLogger _eventLogger;
    private readonly AmperfySettings _settings;
    private readonly INetworkMonitor _networkMonitor;
    private readonly EventNotificationHandler _notificationHandler;
    private readonly IUrlCleanser _urlCleanser;
    private readonly HttpClient _httpClient;
    private readonly CacheFileManager _fileManager;
    private readonly bool _isCacheSizeLimited;
    private readonly bool _isFailWithPopupError;

    private readonly LinkedList<DownloadOperation> _queue = new();
    private readonly Dictionary<string, DownloadOperation> _operations = [];
    private readonly List<IDisposable> _registrations = [];
    private int _runningCount;
    private TaskCompletionSource _idleTcs = CreateCompletedTcs();
    private bool _isCheckForCachedNeeded = true;
    private PreDownloadIsValidCallback? _preDownloadIsValidCheck;
    private bool _isInitialized;
    private bool _isDisposed;

    public string Name { get; }
    public DownloadableType DownloadType => _requestManager.DownloadType;
    public bool IsRunning { get; private set; }

    /// Raised (main thread) when downloads were added, started, finished, failed, canceled or removed.
    /// The Downloads page should reload <see cref="GetDownloads"/>.
    public event EventHandler? DownloadsChanged;

    /// Raised (main thread, throttled by <see cref="ProgressUpdateInterval"/>) while a transfer is running.
    /// <see cref="Model.Download.Progress"/> and <see cref="Model.Download.TotalSize"/> are updated before.
    public event EventHandler<DownloadProgressEventArgs>? DownloadProgressChanged;

    public DownloadManager(
        string name,
        Account account,
        LibraryStorage library,
        DownloadableType downloadType,
        Func<IDownloadManagerDelegate> getDownloadDelegate,
        EventLogger eventLogger,
        AmperfySettings settings,
        INetworkMonitor networkMonitor,
        EventNotificationHandler notificationHandler,
        IUrlCleanser urlCleanser,
        bool limitCacheSize,
        bool isFailWithPopupError,
        HttpClient? httpClient = null,
        CacheFileManager? fileManager = null)
    {
        Name = name;
        _account = account;
        _library = library;
        _requestManager = new DownloadRequestManager(account, library, downloadType);
        _getDownloadDelegate = getDownloadDelegate;
        _eventLogger = eventLogger;
        _settings = settings;
        _networkMonitor = networkMonitor;
        _notificationHandler = notificationHandler;
        _urlCleanser = urlCleanser;
        _isCacheSizeLimited = limitCacheSize;
        _isFailWithPopupError = isFailWithPopupError;
        _httpClient = httpClient ?? DefaultHttpClient.Value;
        _fileManager = fileManager ?? CacheFileManager.Shared;
    }

    /// Registers for network/offline changes (Swift: initialize(urlSession:isCheckForCachedNeeded:validationCB:)).
    public void Initialize(bool isCheckForCachedNeeded, PreDownloadIsValidCallback? validationCallback)
    {
        _isCheckForCachedNeeded = isCheckForCachedNeeded;
        _preDownloadIsValidCheck = validationCallback;
        if (_isInitialized) return;
        _isInitialized = true;
        _registrations.Add(_notificationHandler.Register(AmperfyNotification.OfflineModeChanged, _ => NetworkStatusChanged()));
        _registrations.Add(_notificationHandler.Register(AmperfyNotification.NetworkStatusChanged, _ => NetworkStatusChanged()));
        _networkMonitor.ConnectionTypeChanged += OnConnectionTypeChanged;
    }

    public DownloadRequestManager RequestManager => _requestManager;

    /// Number of transfers currently running.
    public int ActiveDownloadCount => _runningCount;

    /// Number of requests waiting for a free transfer slot.
    public int QueuedDownloadCount => _queue.Count;

    /// Unique ids of all queued or running requests.
    public IReadOnlyCollection<string> PendingDownloadIds => _operations.Keys.ToList();

    /// All download rows of this manager (account + type), sorted by creation date (Downloads page).
    public List<Model.Download> GetDownloads() => _library.GetDownloads(_account, DownloadType);

    /// Completes when no request is queued or running anymore.
    public Task WhenIdleAsync() => _idleTcs.Task;

    // --- IDownloadManageable ---------------------------------------------------------------------

    public void Download(IDownloadable obj)
    {
        if (_isCheckForCachedNeeded && obj.IsCached) return;
        if (_isCacheSizeLimited && StorageExceedsCacheLimit()) return;
        EnsureSaved([obj]);

        if (_preDownloadIsValidCheck is { } isValidCheck)
        {
            if (isValidCheck([obj]).Count == 0) return;
        }

        var request = _requestManager.Add(obj);
        if (request is null) return;
        RaiseDownloadsChanged();
        if (!IsAllowedToTriggerDownload) return;
        AddDownloadTaskOperation(request);
    }

    public void Download(IEnumerable<IDownloadable> objects)
    {
        var downloadObjects = objects.Where(o => !_isCheckForCachedNeeded || !o.IsCached).ToList();
        if (downloadObjects.Count == 0) return;
        if (_isCacheSizeLimited && StorageExceedsCacheLimit()) return;
        EnsureSaved(downloadObjects);

        IReadOnlyList<IDownloadable> validDls = downloadObjects;
        if (_preDownloadIsValidCheck is { } isValidCheck) validDls = isValidCheck(validDls);
        if (validDls.Count == 0) return;

        var requests = _requestManager.Add(validDls.ToList());
        RaiseDownloadsChanged();
        if (!IsAllowedToTriggerDownload) return;
        foreach (var request in requests) AddDownloadTaskOperation(request);
    }

    public void RemoveFinishedDownload(IDownloadable obj)
    {
        _requestManager.RemoveFinishedDownload(obj.UniqueId());
        RaiseDownloadsChanged();
    }

    public void RemoveFinishedDownload(IEnumerable<IDownloadable> objects)
    {
        _requestManager.RemoveFinishedDownload(objects.Select(o => o.UniqueId()).ToList());
        RaiseDownloadsChanged();
    }

    public void ClearFinishedDownloads()
    {
        _requestManager.ClearFinishedDownloads();
        RaiseDownloadsChanged();
    }

    /// Deletes all download rows if none of them waits to be started (used for the artwork downloader).
    public void ClearAllDownloadsIfAllHaveFinished()
    {
        _requestManager.ClearAllDownloadsIfAllHaveFinished();
        RaiseDownloadsChanged();
    }

    public void ResetFailedDownloads()
    {
        var failedRequests = _requestManager.GetAndResetFailedDownloads();
        RaiseDownloadsChanged();
        if (!IsAllowedToTriggerDownload) return;
        foreach (var failedRequest in failedRequests) AddDownloadTaskOperation(failedRequest);
    }

    public void CancelDownloads()
    {
        CancelAllOperations();
        _requestManager.CancelDownloads();
        CheckIdle();
        RaiseDownloadsChanged();
    }

    public void Start()
    {
        IsRunning = true;
        SetupDownloadQueue();
    }

    public void Stop()
    {
        IsRunning = false;
        CancelDownloads();
    }

    /// Stops the transfers without failing the downloads: they are started again when the network
    /// is back (Swift: suspendDownloads).
    public void SuspendDownloads()
    {
        AmperfyLog.Info(Name, "Suspend active downloads");
        foreach (var op in _operations.Values)
        {
            if (_library.IsAlive(op.Request.Download)) op.Request.Download.Suspend();
        }
        _library.SaveContext();
        CancelAllOperations();
        CheckIdle();
        RaiseDownloadsChanged();
    }

    public bool StorageExceedsCacheLimit()
    {
        var cacheLimit = _settings.User.CacheLimit;
        if (cacheLimit == 0) return false;
        return _fileManager.CompletePlayableCacheSize > cacheLimit;
    }

    public bool IsAllowedToTriggerDownload =>
        IsRunning &&
        _settings.User.IsOnlineMode &&
        _networkMonitor.IsConnectedToNetwork &&
        (!_isCacheSizeLimited || !StorageExceedsCacheLimit());

    // --- queue -----------------------------------------------------------------------------------

    private sealed class DownloadOperation
    {
        public DownloadRequest Request { get; }
        public CancellationTokenSource Cts { get; } = new();
        public CancellationToken Token => Cts.Token;
        public bool IsCanceled => Cts.IsCancellationRequested;

        public DownloadOperation(DownloadRequest request) => Request = request;
    }

    private static TaskCompletionSource CreateCompletedTcs()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult();
        return tcs;
    }

    private void EnsureSaved(IEnumerable<IDownloadable> objects)
    {
        // unique download ids are based on the primary key -> new entities must be saved first
        if (objects.Any(o => o.Pk == 0)) _library.SaveContext();
    }

    private void SetupDownloadQueue()
    {
        if (!IsAllowedToTriggerDownload) return;
        var downloadRequests = _requestManager.GetRequestedDownloads(_operations.Keys.ToHashSet());
        foreach (var downloadRequest in downloadRequests) AddDownloadTaskOperation(downloadRequest);
        RaiseDownloadsChanged();
    }

    private void AddDownloadTaskOperation(DownloadRequest downloadRequest)
    {
        // the operation must be unique
        if (_operations.ContainsKey(downloadRequest.Id)) return;
        var op = new DownloadOperation(downloadRequest);
        _operations[downloadRequest.Id] = op;
        _queue.AddLast(op);
        if (_idleTcs.Task.IsCompleted) _idleTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PumpQueue();
    }

    private int ParallelDownloadsCount
    {
        get
        {
            try { return Math.Max(1, _getDownloadDelegate().ParallelDownloadsCount); }
            catch { return 1; }
        }
    }

    private void PumpQueue()
    {
        var maxParallel = ParallelDownloadsCount;
        while (!_isDisposed && _runningCount < maxParallel && _queue.First is { } node)
        {
            _queue.RemoveFirst();
            var op = node.Value;
            if (op.IsCanceled) continue;
            _runningCount++;
            _ = RunOperationAsync(op);
        }
        CheckIdle();
    }

    private void CheckIdle()
    {
        if (_runningCount == 0 && _queue.Count == 0) _idleTcs.TrySetResult();
    }

    private void CancelAllOperations()
    {
        foreach (var op in _operations.Values) op.Cts.Cancel();
        _operations.Clear();
        _queue.Clear();
    }

    private async Task RunOperationAsync(DownloadOperation op)
    {
        try
        {
            // start asynchronously (avoids deep recursion when many operations finish synchronously)
            await Task.Yield();
            await ManageDownloadAsync(op);
        }
        catch (Exception ex)
        {
            AmperfyLog.Error(Name, $"Download operation for {op.Request.Title} failed: {ex}");
        }
        finally
        {
            _runningCount--;
            if (_operations.TryGetValue(op.Request.Id, out var current) && ReferenceEquals(current, op))
                _operations.Remove(op.Request.Id);
            PumpQueue();
        }
    }

    // --- transfer --------------------------------------------------------------------------------

    private async Task ManageDownloadAsync(DownloadOperation op)
    {
        var request = op.Request;
        if (op.IsCanceled || !IsAllowedToTriggerDownload) return;
        var download = request.Download;
        if (!_library.IsAlive(download)) return;

        download.Reset();
        download.IsDownloading = true;
        _library.SaveContext();
        RaiseDownloadsChanged();
        AmperfyLog.Info(Name, $"Fetching {request.Title} ...");

        Uri url;
        IReadOnlyDictionary<string, string> httpHeaders;
        try
        {
            var downloadDelegate = _getDownloadDelegate();
            url = await downloadDelegate.PrepareDownloadAsync(request.Element, _library);
            httpHeaders = downloadDelegate.HttpHeaders;
        }
        catch (Exception ex)
        {
            if (op.IsCanceled) return;
            var error = ex is DownloadException de ? de.Error : DownloadError.FetchFailed;
            await FinishDownloadAsync(op, null, error);
            return;
        }
        if (op.IsCanceled) return;
        await FetchAsync(op, url, httpHeaders);
    }

    private async Task FetchAsync(DownloadOperation op, Uri url, IReadOnlyDictionary<string, string> httpHeaders)
    {
        string? tempFilePath = null;
        try
        {
            tempFilePath = _fileManager.CreateTempFilePathWithDirectory();
            using var httpRequest = AmperfyHttp.CreateGet(url, httpHeaders.Count == 0 ? null : httpHeaders);
            using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, op.Token);
            var fileMimeType = response.Content.Headers.ContentType?.MediaType;
            var totalBytes = response.Content.Headers.ContentLength;
            // created on the main thread -> reports are posted to the main thread
            var progress = new Progress<long>(received => OnProgress(op, received, totalBytes));
            await CopyToFileAsync(response, tempFilePath, progress, op.Token);

            if (op.IsCanceled)
            {
                CacheFileManagerExtensions.TryDeleteFile(tempFilePath);
                return;
            }

            DownloadError? downloadError = null;
            var fileSize = _fileManager.GetFileSize(tempFilePath);
            if (fileSize is null)
            {
                downloadError = DownloadError.FileManagerError;
                AmperfyLog.Error(Name, "Could not move download to tmp directory");
            }
            else if (fileSize.Value <= 0)
            {
                downloadError = DownloadError.EmptyFile;
            }

            if (downloadError is { } activeError)
            {
                CacheFileManagerExtensions.TryDeleteFile(tempFilePath);
                await FinishDownloadAsync(op, url, activeError);
            }
            else
            {
                await FinishDownloadAsync(op, url, tempFilePath, fileMimeType, (int)response.StatusCode, response.IsSuccessStatusCode);
            }
        }
        catch (Exception ex)
        {
            CacheFileManagerExtensions.TryDeleteFile(tempFilePath);
            if (op.IsCanceled) return;
            AmperfyLog.Info(Name, $"Fetching {op.Request.Title} transfer error: {ex.Message}");
            await FinishDownloadAsync(op, url, DownloadError.FetchFailed);
        }
    }

    private static Task CopyToFileAsync(HttpResponseMessage response, string filePath, IProgress<long> progress, CancellationToken token) =>
        Task.Run(async () =>
        {
            await using var source = await response.Content.ReadAsStreamAsync(token);
            await using var target = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);
            var buffer = new byte[CopyBufferSize];
            long received = 0;
            var stopwatch = Stopwatch.StartNew();
            var lastReport = TimeSpan.Zero;
            int read;
            while ((read = await source.ReadAsync(buffer, token)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), token);
                received += read;
                var elapsed = stopwatch.Elapsed;
                if (elapsed - lastReport >= ProgressUpdateInterval)
                {
                    lastReport = elapsed;
                    progress.Report(received);
                }
            }
        }, token);

    private void OnProgress(DownloadOperation op, long received, long? totalBytes)
    {
        if (op.IsCanceled || _isDisposed) return;
        var download = op.Request.Download;
        if (!_library.IsAlive(download)) return;
        var progress = totalBytes is > 0 ? Math.Clamp((float)received / totalBytes.Value, 0f, 1f) : 0f;
        download.Progress = progress;
        download.TotalSize = totalBytes is > 0 ? totalBytes.Value.AsByteString() : "";
        DownloadProgressChanged?.Invoke(this, new DownloadProgressEventArgs(download, op.Request.Id, received, totalBytes, progress));
    }

    /// Error path (Swift: finishDownload(downloadRequest:task:error:)).
    private async Task FinishDownloadAsync(DownloadOperation op, Uri? url, DownloadError error)
    {
        var request = op.Request;
        var download = request.Download;
        if (!_library.IsAlive(download)) return;

        var isCanceled = download.IsCanceled;
        if (!isCanceled) download.Error = error;
        download.IsDownloading = false;
        _library.SaveContext();

        if (!isCanceled)
        {
            try { await _getDownloadDelegate().FailedDownloadAsync(request.Element, _library); }
            catch (Exception ex) { AmperfyLog.Error(Name, $"FailedDownload handling failed: {ex.Message}"); }
        }
        if (!isCanceled && error != DownloadError.ApiErrorResponse && error != DownloadError.AlreadyDownloaded)
        {
            AmperfyLog.Info(Name, $"Fetching {request.Title} FAILED: {error.Description()}");
            var shortMessage = $"Error \"{error.Description()}\" occurred while downloading object \"{request.Title}\".";
            var responseError = new ResponseError(ResponseErrorType.Api, message: shortMessage,
                cleansedUrl: url is null ? null : _urlCleanser.Cleanse(url));
            _eventLogger.Report("Download Error", responseError, _isFailWithPopupError);
        }
        RaiseDownloadsChanged();
    }

    /// Success path (Swift: finishDownload(downloadRequest:task:fileURL:fileMimeType:)).
    private async Task FinishDownloadAsync(DownloadOperation op, Uri url, string tempFilePath, string? fileMimeType, int statusCode, bool isSuccessStatusCode)
    {
        var request = op.Request;
        var downloadDelegate = _getDownloadDelegate();
        var responseError = downloadDelegate.ValidateDownloadedData(tempFilePath, url);
        if (responseError is not null)
        {
            AmperfyLog.Error(Name, $"Fetching {request.Title} API-ERROR StatusCode: {responseError.StatusCode}, Message: {responseError.ErrorMessage}");
            _eventLogger.Report("Download", responseError, _isFailWithPopupError);
            CacheFileManagerExtensions.TryDeleteFile(tempFilePath);
            await FinishDownloadAsync(op, url, DownloadError.ApiErrorResponse);
            return;
        }
        if (!isSuccessStatusCode)
        {
            // URLSession stored HTTP error bodies as download; don't cache error pages as media files
            AmperfyLog.Error(Name, $"Fetching {request.Title} HTTP status code: {statusCode}");
            CacheFileManagerExtensions.TryDeleteFile(tempFilePath);
            await FinishDownloadAsync(op, url, DownloadError.FetchFailed);
            return;
        }

        AmperfyLog.Info(Name, $"Fetching {request.Title} SUCCESS ({(_fileManager.GetFileSize(tempFilePath) ?? 0).AsByteString()}) ({fileMimeType ?? "no MIME type"})");
        try
        {
            await downloadDelegate.CompletedDownloadAsync(request.Element, tempFilePath, fileMimeType, _library);
        }
        catch (Exception ex)
        {
            AmperfyLog.Error(Name, $"CompletedDownload handling failed: {ex.Message}");
        }
        // the delegate moves the file into the cache; remove leftovers
        CacheFileManagerExtensions.TryDeleteFile(tempFilePath);

        var download = request.Download;
        if (_library.IsAlive(download))
        {
            download.Progress = 1f;
            download.IsDownloading = false;
            _library.SaveContext();
            _notificationHandler.Post(AmperfyNotification.DownloadFinishedSuccess, this, new DownloadNotification(request.Element.UniqueId()));
        }
        if (_isCacheSizeLimited && StorageExceedsCacheLimit())
        {
            _requestManager.CancelDownloads();
        }
        RaiseDownloadsChanged();
    }

    // --- network ---------------------------------------------------------------------------------

    private void OnConnectionTypeChanged(bool _) => NetworkStatusChanged();

    private void NetworkStatusChanged()
    {
        if (!IsRunning || _isDisposed) return;
        if (IsAllowedToTriggerDownload)
        {
            AmperfyLog.Info(Name, $"Download Manager ({Name}): Online Mode | Internet; setup download queue");
            SetupDownloadQueue();
        }
        else
        {
            AmperfyLog.Info(Name, $"Download Manager ({Name}): Offline Mode | No Internet; suspend downloads");
            SuspendDownloads();
        }
    }

    private void RaiseDownloadsChanged()
    {
        if (_isDisposed) return;
        try { DownloadsChanged?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { AmperfyLog.Error(Name, $"DownloadsChanged handler failed: {ex}"); }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        IsRunning = false;
        CancelAllOperations();
        _isDisposed = true;
        _networkMonitor.ConnectionTypeChanged -= OnConnectionTypeChanged;
        foreach (var r in _registrations) r.Dispose();
        _registrations.Clear();
        _idleTcs.TrySetResult();
    }
}
