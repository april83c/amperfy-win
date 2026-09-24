using Amperfy.Core.Api;
using Amperfy.Core.Downloads;

namespace Amperfy.Core.Sync;

/// Syncs the newest podcast episodes and notifies the user about new ones
/// (port of Api/BackgroundFetchTriggeredSyncer.swift). Triggered by <see cref="PeriodicBackgroundFetcher"/>.
public sealed class BackgroundFetchTriggeredSyncer
{
    private const string LogCategory = "BackgroundFetchTriggeredSyncer";

    private readonly LibraryStorage _library;
    private readonly AmperfySettings _settings;
    private readonly Account _account;
    private readonly ILibrarySyncer _librarySyncer;
    private readonly ILocalNotificationManager _notificationManager;
    private readonly IDownloadManageable _playableDownloadManager;

    public BackgroundFetchTriggeredSyncer(
        LibraryStorage library,
        AmperfySettings settings,
        Account account,
        ILibrarySyncer librarySyncer,
        ILocalNotificationManager notificationManager,
        IDownloadManageable playableDownloadManager)
    {
        _library = library;
        _settings = settings;
        _account = account;
        _librarySyncer = librarySyncer;
        _notificationManager = notificationManager;
        _playableDownloadManager = playableDownloadManager;
    }

    public Account Account => _account;

    public async Task SyncAndNotifyPodcastEpisodesAsync()
    {
        AmperfyLog.Info(LogCategory, "Perform podcast episode sync");
        var autoDlLibSyncer = new AutoDownloadLibrarySyncer(_library, _settings, _account, _librarySyncer, _playableDownloadManager);
        var addedPodcastEpisodes = await autoDlLibSyncer.SyncNewestPodcastEpisodesAsync();
        foreach (var episodeToNotify in addedPodcastEpisodes)
        {
            AmperfyLog.Info(LogCategory, $"Podcast: {episodeToNotify.Podcast?.Name ?? ""}, New Episode: {episodeToNotify.Title}");
            _notificationManager.Notify(episodeToNotify);
        }
    }
}

/// Windows replacement for the iOS background app refresh (BGAppRefreshTask every 45 minutes):
/// a main thread timer running while the app is alive (AppDelegate.performBackgroundFetchTask).
public sealed class PeriodicBackgroundFetcher : IDisposable
{
    private const string LogCategory = "PeriodicBackgroundFetcher";

    /// Swift: request.earliestBeginDate = 45 minutes
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(45);

    private readonly Func<IEnumerable<BackgroundFetchTriggeredSyncer>> _getSyncers;
    private readonly EventLogger _eventLogger;
    private readonly AmperfySettings _settings;
    private readonly INetworkMonitor _networkMonitor;
    private IDisposable? _timer;
    private Task? _currentFetch;

    public TimeSpan Interval { get; }

    /// Raised after each fetch run (argument: success).
    public event Action<bool>? FetchPerformed;

    /// <param name="getSyncers">Syncers of all accounts (Swift: for accountInfo in allAccounts { getMeta(accountInfo).backgroundFetchTriggeredSyncer })</param>
    public PeriodicBackgroundFetcher(
        Func<IEnumerable<BackgroundFetchTriggeredSyncer>> getSyncers,
        EventLogger eventLogger,
        AmperfySettings settings,
        INetworkMonitor networkMonitor,
        TimeSpan? interval = null)
    {
        _getSyncers = getSyncers;
        _eventLogger = eventLogger;
        _settings = settings;
        _networkMonitor = networkMonitor;
        Interval = interval ?? DefaultInterval;
    }

    public bool IsStarted => _timer is not null;

    public void Start()
    {
        if (_timer is not null) return;
        _timer = MainThread.CreateTimer(Interval, () => _ = PerformFetchAsync());
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// Runs a fetch now (a running fetch is joined instead of starting a second one).
    public Task<bool> PerformFetchAsync()
    {
        if (_currentFetch is { IsCompleted: false } running) return JoinAsync(running);
        var task = PerformFetchInternalAsync();
        _currentFetch = task;
        return task;
    }

    private static async Task<bool> JoinAsync(Task running)
    {
        await running;
        return running is Task<bool> b && b.Result;
    }

    private async Task<bool> PerformFetchInternalAsync()
    {
        if (!_settings.User.IsOnlineMode || !_networkMonitor.IsConnectedToNetwork)
        {
            AmperfyLog.Info(LogCategory, "Skip background fetch: offline");
            return false;
        }
        AmperfyLog.Info(LogCategory, "Perform background fetch");
        var success = true;
        foreach (var syncer in _getSyncers().ToList())
        {
            try
            {
                await syncer.SyncAndNotifyPodcastEpisodesAsync();
            }
            catch (Exception ex)
            {
                success = false;
                _eventLogger.Error("Background Task", AmperfyLogStatusCode.ConnectionError, ex.Message, displayPopup: false);
            }
        }
        FetchPerformed?.Invoke(success);
        return success;
    }

    public void Dispose() => Stop();
}
