using Amperfy.Core.Api;
using Amperfy.Core.Sync;

namespace Amperfy.Core;

/// Composition root of the core (port of AmperfyKit.swift / AmperKit). Owns storage, settings,
/// logging, networking and one MetaManager per account. The player is attached by the host.
public sealed class AmperKit : IDisposable
{
    public PersistentStorage Storage { get; }
    public LibraryStorage Library => Storage.Main;
    public AmperfySettings Settings => Storage.Settings;
    public EventLogger EventLogger { get; }
    public INetworkMonitor NetworkMonitor { get; }
    public EventNotificationHandler NotificationHandler { get; } = new();
    public LocalNotificationManager LocalNotificationManager { get; }
    public UserStatistics UserStatistics { get; } = new();
    public PeriodicBackgroundFetcher BackgroundFetcher { get; }

    private readonly Dictionary<AccountInfo, MetaManager> _metaManagers = [];

    public AmperKit(PersistentStorage storage, INetworkMonitor networkMonitor)
    {
        Storage = storage;
        NetworkMonitor = networkMonitor;
        EventLogger = new EventLogger(storage.Main);
        LocalNotificationManager = new LocalNotificationManager(storage.Settings);
        BackgroundFetcher = new PeriodicBackgroundFetcher(
            () => Settings.Accounts.AllAccounts.Select(a => GetMeta(a).BackgroundFetchTriggeredSyncer).ToList(),
            EventLogger, Settings, networkMonitor);
        networkMonitor.ConnectionTypeChanged += _ => NotificationHandler.Post(AmperfyNotification.NetworkStatusChanged, this);
    }

    public MetaManager GetMeta(AccountInfo accountInfo)
    {
        if (_metaManagers.TryGetValue(accountInfo, out var meta)) return meta;
        var account = Library.GetAccount(accountInfo);
        meta = new MetaManager(account, Library, Settings, NetworkMonitor, EventLogger, NotificationHandler, LocalNotificationManager);
        _metaManagers[accountInfo] = meta;
        return meta;
    }

    public IReadOnlyDictionary<AccountInfo, MetaManager> AllActiveMetas => _metaManagers;

    public void ResetMeta(AccountInfo accountInfo)
    {
        if (_metaManagers.Remove(accountInfo, out var meta)) meta.Dispose();
    }

    /// The active account (null when not logged in).
    public Account? ActiveAccount => Settings.Accounts.Active is { } info ? Library.GetAccount(info) : null;

    public MetaManager? ActiveMeta => Settings.Accounts.Active is { } info ? GetMeta(info) : null;

    public bool IsLoggedIn => Settings.Accounts.Active is { } info && Settings.Accounts.GetSetting(info).LoginCredentials is not null;

    /// Validates input, detects the api and stores the credentials (port of LoginVC.login()).
    /// Returns the new (active) account; the caller should run the initial sync afterwards.
    public async Task<Account> LoginAsync(string serverUrl, string username, string password, BackendApiType apiType,
        IReadOnlyDictionary<string, string>? httpHeaders = null)
    {
        serverUrl = serverUrl.Trim();
        if (string.IsNullOrEmpty(serverUrl)) throw new AuthenticationError(AuthenticationErrorKind.DownloadError, "No server URL given!");
        if (!serverUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !serverUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            throw new AuthenticationError(AuthenticationErrorKind.DownloadError, "Please provide either 'https://' or 'http://' in your server URL.");
        if (string.IsNullOrEmpty(username)) throw new AuthenticationError(AuthenticationErrorKind.DownloadError, "No username given!");
        if (string.IsNullOrEmpty(password)) throw new AuthenticationError(AuthenticationErrorKind.DownloadError, "No password given!");

        var credentials = new LoginCredentials(serverUrl, username, password);
        if (httpHeaders is not null) credentials.HttpHeaders = new Dictionary<string, string>(httpHeaders);
        var accountInfo = AccountInfo.Create(credentials);
        if (Settings.Accounts.AllAccounts.Contains(accountInfo))
            throw new AuthenticationError(AuthenticationErrorKind.DownloadError, "Account already added!");

        var meta = GetMeta(accountInfo);
        try
        {
            var authenticatedApiType = await meta.BackendApi.LoginAsync(apiType, credentials);
            credentials.BackendApi = authenticatedApiType;
            accountInfo = AccountInfo.Create(credentials);
            meta.BackendApi.SelectedApi = authenticatedApiType;
            meta.Account.AssignInfo(accountInfo);
            Library.SaveContext();
            Settings.Accounts.Login(credentials);
            meta.BackendApi.ProvideCredentials(credentials);
            NotificationHandler.Post(AmperfyNotification.AccountAdded, this);
            NotificationHandler.Post(AmperfyNotification.AccountActiveChanged, this);
            return meta.Account;
        }
        catch
        {
            ResetMeta(accountInfo);
            throw;
        }
    }

    /// Runs the initial library sync of an account (port of SyncVC.viewDidAppear/finishSync).
    public async Task<SyncCompletionStatus> SyncInitialAsync(Account account, ISyncCallbacks? progress)
    {
        EventLogger.SuppressAlerts = true;
        var status = SyncCompletionStatus.Completed;
        try
        {
            if (Settings.Accounts.AllAccounts.Count <= 1) Settings.App.IsLibrarySynced = false;
            Library.CleanStorageOfObsoleteAccountEntries(account);
            account = Library.GetAccount(account.Info);
            ResetMeta(account.Info);
            await GetMeta(account.Info).LibrarySyncer.SyncInitialAsync(progress);
        }
        catch (Exception ex)
        {
            EventLogger.Report("Initial Sync", ex, displayPopup: false);
            status = SyncCompletionStatus.Aborted;
        }
        FinishInitialSync(account.Info, status);
        return status;
    }

    public void FinishInitialSync(AccountInfo accountInfo, SyncCompletionStatus status)
    {
        Settings.Accounts.UpdateSetting(accountInfo, s => s.InitialSyncCompletionStatus = status);
        Settings.App.LibrarySyncVersion = SettingEnumerationExtensions.NewestLibrarySyncVersion;
        Settings.App.IsLibrarySynced = true;
        EventLogger.SuppressAlerts = false;
        GetMeta(accountInfo).StartManagerAfterSync();
    }

    public void SwitchActiveAccount(AccountInfo accountInfo)
    {
        Settings.Accounts.SwitchActiveAccount(accountInfo);
        NotificationHandler.Post(AmperfyNotification.AccountActiveChanged, this);
    }

    /// Logs out: stops the managers, deletes cached files and the stored credentials
    /// (port of AccountSettingsView.logout). The library entries are cleaned on the next sync.
    public void Logout(AccountInfo accountInfo)
    {
        if (_metaManagers.TryGetValue(accountInfo, out var meta)) meta.StopManager();
        ResetMeta(accountInfo);
        CacheFileManager.Shared.DeleteAccountCache(accountInfo);
        Settings.Accounts.Logout(accountInfo);
        Settings.User.IsOfflineMode = false;
        var account = Library.GetAccount(accountInfo);
        Library.CleanStorageOfObsoleteAccountEntries(account);
        NotificationHandler.Post(AmperfyNotification.AccountDeleted, this);
        NotificationHandler.Post(AmperfyNotification.AccountActiveChanged, this);
    }

    /// Starts the background services of all logged in accounts (normal app start).
    public void StartManagersForNormalOperation()
    {
        foreach (var info in Settings.Accounts.AllAccounts) GetMeta(info).StartManagerForNormalOperation();
        BackgroundFetcher.Start();
    }

    public void Dispose()
    {
        BackgroundFetcher.Stop();
        foreach (var meta in _metaManagers.Values) meta.Dispose();
        _metaManagers.Clear();
        Storage.Dispose();
    }
}
