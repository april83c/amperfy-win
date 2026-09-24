using Amperfy.Core.Api;
using Amperfy.Core.Downloads;
using Amperfy.Core.Player;
using Amperfy.Core.Sync;

namespace Amperfy.Core;

/// Per-account services (port of MetaManager.swift): server api, library syncer, download
/// managers and background syncers.
public sealed class MetaManager : IDisposable
{
    private readonly LibraryStorage _library;
    private readonly AmperfySettings _settings;
    private readonly INetworkMonitor _networkMonitor;
    private readonly EventLogger _eventLogger;
    private readonly EventNotificationHandler _notificationHandler;
    private readonly ILocalNotificationManager _localNotificationManager;

    public Account Account { get; }

    public MetaManager(Account account, LibraryStorage library, AmperfySettings settings, INetworkMonitor networkMonitor,
        EventLogger eventLogger, EventNotificationHandler notificationHandler, ILocalNotificationManager localNotificationManager)
    {
        Account = account;
        _library = library;
        _settings = settings;
        _networkMonitor = networkMonitor;
        _eventLogger = eventLogger;
        _notificationHandler = notificationHandler;
        _localNotificationManager = localNotificationManager;
    }

    private BackendProxy? _backendApi;

    public BackendProxy BackendApi
    {
        get
        {
            if (_backendApi is not null) return _backendApi;
            var api = new BackendProxy(_networkMonitor, _eventLogger, _settings);
            api.SelectedApi = Account.ApiType;
            if (_settings.Accounts.GetSetting(Account.Info).LoginCredentials is { } credentials)
                api.ProvideCredentials(credentials);
            else
                AmperfyLog.Info("MetaManager", "Initializing API without credentials");
            _backendApi = api;
            return api;
        }
    }

    private LibrarySyncerProxy? _librarySyncer;
    public ILibrarySyncer LibrarySyncer => _librarySyncer ??= new LibrarySyncerProxy(BackendApi, Account, _library);

    private DuplicateEntitiesResolver? _duplicateEntitiesResolver;
    public DuplicateEntitiesResolver DuplicateEntitiesResolver => _duplicateEntitiesResolver ??= new DuplicateEntitiesResolver(Account, _library);

    private DownloadManager? _playableDownloadManager;
    public DownloadManager PlayableDownloadManager
    {
        get
        {
            if (_playableDownloadManager is not null) return _playableDownloadManager;
            var manager = DownloadManagerFactory.CreatePlayableDownloadManager(Account, _library, BackendApi, _eventLogger, _settings, _networkMonitor, _notificationHandler);
            manager.Initialize(isCheckForCachedNeeded: true, validationCallback: null);
            _playableDownloadManager = manager;
            return manager;
        }
    }

    private DownloadManager? _artworkDownloadManager;
    public DownloadManager ArtworkDownloadManager
    {
        get
        {
            if (_artworkDownloadManager is not null) return _artworkDownloadManager;
            var manager = DownloadManagerFactory.CreateArtworkDownloadManager(Account, _library, BackendApi, BackendApi.GetActiveArtworkDownloadDelegate,
                _eventLogger, _settings, _networkMonitor, _notificationHandler);
            manager.ClearAllDownloadsIfAllHaveFinished();
            manager.Initialize(isCheckForCachedNeeded: false, validationCallback: DownloadManagerFactory.CreateArtworkValidation(_settings, Account.Info));
            _artworkDownloadManager = manager;
            return manager;
        }
    }

    private BackgroundLibrarySyncer? _backgroundLibrarySyncer;
    public BackgroundLibrarySyncer BackgroundLibrarySyncer => _backgroundLibrarySyncer ??= new BackgroundLibrarySyncer(
        Account, _library, _settings, _networkMonitor, LibrarySyncer,
        new AutoDownloadLibrarySyncer(_library, _settings, Account, LibrarySyncer, PlayableDownloadManager), _eventLogger);

    private BackgroundFetchTriggeredSyncer? _backgroundFetchTriggeredSyncer;
    public BackgroundFetchTriggeredSyncer BackgroundFetchTriggeredSyncer => _backgroundFetchTriggeredSyncer ??=
        new BackgroundFetchTriggeredSyncer(_library, _settings, Account, LibrarySyncer, _localNotificationManager, PlayableDownloadManager);

    /// Additional per-account services registered by the player.
    public List<IDisposable> PlayerServices { get; } = [];

    /// Scrobble syncer of this account (created when the player is attached, kept alive here because
    /// the player holds its notifiers weakly).
    public ScrobbleSyncer? ScrobbleSyncer { get; private set; }

    public void AttachPlayer(IPlayerFacade player)
    {
        if (ScrobbleSyncer is not null) return;
        ScrobbleSyncer = new ScrobbleSyncer(player, _networkMonitor, Account, _library, _settings, LibrarySyncer, _eventLogger);
        player.AddNotifier(ScrobbleSyncer);
    }

    public void StartManagerAfterSync()
    {
        AmperfyLog.Info("MetaManager", "Start background manager after sync");
        PlayableDownloadManager.Start();
        ArtworkDownloadManager.Start();
        BackgroundLibrarySyncer.Start();
        ScrobbleSyncer?.Start();
    }

    public void StartManagerForNormalOperation()
    {
        AmperfyLog.Info("MetaManager", "Start background manager for normal operation");
        DuplicateEntitiesResolver.Start();
        ArtworkDownloadManager.Start();
        PlayableDownloadManager.Start();
        BackgroundLibrarySyncer.Start();
        ScrobbleSyncer?.Start();
    }

    public void StopManager()
    {
        AmperfyLog.Info("MetaManager", "Stop meta managers");
        foreach (var s in PlayerServices) s.Dispose();
        PlayerServices.Clear();
        ScrobbleSyncer?.Stop();
        _backgroundLibrarySyncer?.Stop();
        _artworkDownloadManager?.Stop();
        _playableDownloadManager?.Stop();
        _duplicateEntitiesResolver?.Stop();
    }

    public void Dispose()
    {
        StopManager();
        _artworkDownloadManager?.Dispose();
        _playableDownloadManager?.Dispose();
    }
}
