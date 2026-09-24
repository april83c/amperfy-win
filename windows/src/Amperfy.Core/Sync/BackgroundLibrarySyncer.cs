using Amperfy.Core.Api;
using Amperfy.Core.Downloads;

namespace Amperfy.Core.Sync;

/// Syncs the newest library elements and afterwards the songs of all albums whose songs have not
/// been synced yet, one album at a time (port of Api/BackgroundLibrarySyncer.swift). Main thread;
/// network I/O is awaited.
public sealed class BackgroundLibrarySyncer
{
    private const string LogCategory = "BackgroundLibrarySyncer";

    private readonly Account _account;
    private readonly LibraryStorage _library;
    private readonly AmperfySettings _settings;
    private readonly INetworkMonitor _networkMonitor;
    private readonly ILibrarySyncer _librarySyncer;
    private readonly AutoDownloadLibrarySyncer _autoDownloadLibrarySyncer;
    private readonly EventLogger _eventLogger;
    private bool _isRunning;
    private CancellationTokenSource _cts = new();
    private Task _runningTask = Task.CompletedTask;

    public BackgroundLibrarySyncer(
        Account account,
        LibraryStorage library,
        AmperfySettings settings,
        INetworkMonitor networkMonitor,
        ILibrarySyncer librarySyncer,
        AutoDownloadLibrarySyncer autoDownloadLibrarySyncer,
        EventLogger eventLogger)
    {
        _account = account;
        _library = library;
        _settings = settings;
        _networkMonitor = networkMonitor;
        _librarySyncer = librarySyncer;
        _autoDownloadLibrarySyncer = autoDownloadLibrarySyncer;
        _eventLogger = eventLogger;
    }

    /// True while a sync run is in progress.
    public bool IsActive { get; private set; }

    /// Completes when the current sync run has finished.
    public Task RunningTask => _runningTask;

    /// Raised (main thread) after an album's songs have been synced in the background.
    public event Action<Album>? AlbumSynced;

    public void Start()
    {
        _isRunning = true;
        if (IsActive) return;
        IsActive = true;
        _cts = new CancellationTokenSource();
        _runningTask = SyncAlbumSongsInBackgroundAsync(_cts.Token);
    }

    public void Stop()
    {
        _isRunning = false;
        _cts.Cancel();
    }

    private bool IsSyncAllowed(CancellationToken token) =>
        !token.IsCancellationRequested && _isRunning && _settings.User.IsOnlineMode && _networkMonitor.IsConnectedToNetwork;

    private async Task SyncAlbumSongsInBackgroundAsync(CancellationToken token)
    {
        try
        {
            await Task.Yield();
            AmperfyLog.Info(LogCategory, "start");

            if (IsSyncAllowed(token))
            {
                try
                {
                    await _autoDownloadLibrarySyncer.SyncNewestLibraryElementsAsync(0, AmperfyInfo.NewestElementsFetchCount);
                }
                catch (Exception ex)
                {
                    _eventLogger.Report("Latest Library Elements Background Sync", ex, displayPopup: false);
                }
            }

            // Swift: one BackgroundSyncOperation per album in an OperationQueue with maxConcurrentOperationCount = 1
            var albumsToSync = _library.GetAlbumsWithoutSyncedSongs(_account);
            foreach (var album in albumsToSync)
            {
                if (!IsSyncAllowed(token)) break;
                if (!_library.IsAlive(album)) continue;
                try
                {
                    await _librarySyncer.SyncAsync(album);
                    AlbumSynced?.Invoke(album);
                }
                catch (Exception ex)
                {
                    _eventLogger.Report("Album Background Sync", ex, displayPopup: false);
                    album.IsSongsMetaDataSynced = true;
                    _library.SaveContext();
                }
            }
        }
        catch (Exception ex)
        {
            AmperfyLog.Error(LogCategory, $"Background sync failed: {ex}");
        }
        finally
        {
            // Swift: barrier block at the end of the queue
            _isRunning = false;
            IsActive = false;
            AmperfyLog.Info(LogCategory, "stopped");
        }
    }
}
