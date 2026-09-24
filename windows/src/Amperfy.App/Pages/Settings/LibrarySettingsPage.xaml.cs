using Amperfy.App.Services;
using Amperfy.Core.Common;
using Amperfy.Core.Downloads;
using Amperfy.Core.Storage;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Pages.Settings;

/// Library settings of the active account (port of LibrarySettingsView and the auto cache options
/// of AccountSettingsView): library counts, background sync, auto download, cache size / limit,
/// download the whole library and delete the downloads.
public sealed partial class LibrarySettingsPage : Page
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly List<IDisposable> _subscriptions = [];
    private DispatcherQueueTimer? _refreshTimer;
    private int _refreshTick;
    private bool _isCacheSizeCalculating;
    private bool _isLoading;

    private static readonly string[] CacheLimitUnits = ["MB", "GB"];

    public LibrarySettingsPage()
    {
        InitializeComponent();
        foreach (var unit in CacheLimitUnits) CacheLimitUnitCombo.Items.Add(unit);
        LoadCacheLimit();
        CacheLimitBox.ValueChanged += (_, _) => SaveCacheLimit();
        CacheLimitUnitCombo.SelectionChanged += (_, _) => SaveCacheLimit();
        AutoDownloadSongsToggle.Toggled += (_, _) => UpdateAccountSetting(s => s.IsAutoDownloadLatestSongsActive = AutoDownloadSongsToggle.IsOn);
        AutoDownloadEpisodesToggle.Toggled += (_, _) => UpdateAccountSetting(s => s.IsAutoDownloadLatestPodcastEpisodesActive = AutoDownloadEpisodesToggle.IsOn);
        FetchEpisodesButton.Click += (_, _) => _ = FetchPodcastEpisodesAsync();
        ResolveDuplicatesButton.Click += (_, _) => _ = ResolveDuplicatesAsync();
        OpenCacheFolderButton.Click += (_, _) => SettingsUi.OpenFolder(CacheFileManager.Shared.RootDirectory);
        DownloadAllSongsButton.Click += (_, _) => _ = DownloadAllSongsAsync();
        DeleteCacheButton.Click += (_, _) => _ = DeleteCacheAsync();

        Loaded += (_, _) =>
        {
            if (_subscriptions.Count == 0)
                _subscriptions.Add(_services.Notifications.Register(AmperfyNotification.AccountActiveChanged, _ => Reload()));
            Reload();
            _refreshTimer ??= CreateRefreshTimer();
            _refreshTimer.Start();
        };
        Unloaded += (_, _) =>
        {
            _refreshTimer?.Stop();
            foreach (var s in _subscriptions) s.Dispose();
            _subscriptions.Clear();
        };
    }

    private DispatcherQueueTimer CreateRefreshTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Tick += (_, _) =>
        {
            UpdateCounts();
            if (++_refreshTick % 3 == 0) UpdateCacheSizes();
        };
        return timer;
    }

    private AccountInfo? ActiveInfo => _services.Settings.Accounts.Active;

    private void UpdateAccountSetting(Action<AccountSetting> update)
    {
        if (_isLoading || ActiveInfo is not { } info) return;
        _services.Settings.Accounts.UpdateSetting(info, update);
    }

    private void Reload()
    {
        _isLoading = true;
        try
        {
            var info = ActiveInfo;
            IsEnabled = info is not null;
            if (info is null)
            {
                AccountHintText.Text = "You aren't logged in yet.";
                return;
            }
            var setting = _services.Settings.Accounts.GetSetting(info);
            AccountHintText.Text = $"Library of the account {setting.LoginCredentials?.Username} · {setting.LoginCredentials?.DisplayServerUrl}.";
            AutoDownloadSongsToggle.IsOn = setting.IsAutoDownloadLatestSongsActive;
            AutoDownloadEpisodesToggle.IsOn = setting.IsAutoDownloadLatestPodcastEpisodesActive;
            UpdateCounts();
            UpdateCacheSizes();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void UpdateCounts()
    {
        if (ActiveInfo is not { } info) return;
        try
        {
            var library = _services.Library;
            var account = library.GetAccount(info);
            var albumCount = library.GetAlbumCount(account);
            PlaylistCountText.Text = library.GetPlaylistCount(account).ToString();
            ArtistCountText.Text = library.GetArtistCount(account).ToString();
            AlbumCountText.Text = albumCount.ToString();
            SongCountText.Text = library.GetSongCount(account).ToString();
            PodcastCountText.Text = library.GetPodcastCount(account).ToString();
            EpisodeCountText.Text = library.GetPodcastEpisodeCount(account).ToString();
            InitialSyncText.Text = _services.Settings.Accounts.GetSetting(info).InitialSyncCompletionStatus.Description();
            LibraryExpander.Description = $"{albumCount} albums · {SongCountText.Text} songs";

            var albumsWithSyncedSongs = library.GetAlbumWithSyncedSongsCount(account);
            var progress = albumCount < 1 ? 0.0 : albumsWithSyncedSongs * 100.0 / albumCount;
            var isSyncing = _services.Kit.GetMeta(info).BackgroundLibrarySyncer.IsActive;
            BackgroundSyncText.Text = $"{progress:0.0}%{(isSyncing && progress < 100 ? " · syncing" : "")}";

            CachedSongsText.Text = library.GetCachedSongCount(account).ToString();
            CachedEpisodesText.Text = library.GetCachedPodcastEpisodeCount(account).ToString();
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("LibrarySettings", $"Counts could not be updated: {ex.Message}");
        }
    }

    /// Walks the cache directories on a background thread.
    private async void UpdateCacheSizes()
    {
        if (_isCacheSizeCalculating || ActiveInfo is not { } info) return;
        _isCacheSizeCalculating = true;
        try
        {
            var fileManager = CacheFileManager.Shared;
            var (account, all) = await Task.Run(() => (fileManager.CalculateCacheSize(info), fileManager.CalculateCompleteCacheSize()));
            CacheSizeText.Text = account.Total.AsByteString();
            CacheSizeExpander.Description = $"Downloaded files of this account (songs and podcast episodes: {account.Playables.AsByteString()})";
            PlayableCacheSizeText.Text = account.Playables.AsByteString();
            ArtworkCacheSizeText.Text = account.Artworks.AsByteString();
            EmbeddedArtworkCacheSizeText.Text = account.EmbeddedArtworks.AsByteString();
            LyricsCacheSizeText.Text = account.Lyrics.AsByteString();
            AllAccountsCacheSizeText.Text = all.Playables.AsByteString();
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("LibrarySettings", $"Cache size could not be calculated: {ex.Message}");
        }
        finally
        {
            _isCacheSizeCalculating = false;
        }
    }

    // --- cache limit ---------------------------------------------------------------------------

    private void LoadCacheLimit()
    {
        _isLoading = true;
        var limit = CacheSizeLimit.FromBytes(_services.Settings.User.CacheLimit);
        CacheLimitBox.Value = limit.Value;
        CacheLimitUnitCombo.SelectedIndex = limit.IsGigabyte ? 1 : 0;
        UpdateCacheLimitDescription(limit);
        _isLoading = false;
    }

    private void UpdateCacheLimitDescription(CacheSizeLimit limit) =>
        CacheLimitCard.Description = "Downloads stop when the downloaded songs and podcast episodes of all accounts exceed the limit. 0 means no limit. " +
                                     $"Current limit: {limit}.";

    private void SaveCacheLimit()
    {
        if (_isLoading) return;
        var value = CacheLimitBox.Value;
        var limit = new CacheSizeLimit(double.IsNaN(value) ? 0 : value, CacheLimitUnitCombo.SelectedIndex == 1);
        if (_services.Settings.User.CacheLimit == limit.Bytes) return;
        _services.Settings.User.CacheLimit = limit.Bytes;
        UpdateCacheLimitDescription(limit);
        // a raised limit allows the queued downloads to continue
        foreach (var meta in _services.Kit.AllActiveMetas.Values)
        {
            if (meta.PlayableDownloadManager.IsRunning) meta.PlayableDownloadManager.Start();
        }
    }

    // --- actions -------------------------------------------------------------------------------

    private async Task FetchPodcastEpisodesAsync()
    {
        if (_services.Settings.User.IsOfflineMode)
        {
            await _services.Dialogs.ShowMessageAsync("Offline Mode", "Podcasts can't be checked for new episodes in offline mode.");
            return;
        }
        FetchEpisodesButton.IsEnabled = false;
        try
        {
            var success = await _services.Kit.BackgroundFetcher.PerformFetchAsync();
            _services.Alerts.ShowInfo("New Podcast Episodes", success ? "Podcasts have been checked for new episodes." : "Podcasts could not be checked. See the event log for details.");
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("New Podcast Episodes", ex);
        }
        finally
        {
            FetchEpisodesButton.IsEnabled = true;
            UpdateCounts();
        }
    }

    private async Task ResolveDuplicatesAsync()
    {
        if (ActiveInfo is not { } info) return;
        ResolveDuplicatesButton.IsEnabled = false;
        try
        {
            var resolver = _services.Kit.GetMeta(info).DuplicateEntitiesResolver;
            resolver.Start();
            await resolver.RunningTask;
            _services.Alerts.ShowInfo("Duplicate Entries", "Duplicate library entries have been resolved.");
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Duplicate Entries", ex);
        }
        finally
        {
            ResolveDuplicatesButton.IsEnabled = true;
            UpdateCounts();
        }
    }

    private async Task DownloadAllSongsAsync()
    {
        if (ActiveInfo is not { } info) return;
        if (_services.Settings.User.IsOfflineMode)
        {
            await _services.Dialogs.ShowMessageAsync("Offline Mode", "Songs can't be downloaded in offline mode.");
            return;
        }
        var confirmed = await _services.Dialogs.ConfirmAsync("Download all songs in library",
            "This will add all uncached songs in your library to the download queue. This may use a lot of data and storage. Continue?");
        if (!confirmed) return;
        try
        {
            var account = _services.Library.GetAccount(info);
            var songs = _services.Library.GetSongsForCompleteLibraryDownload(account);
            _services.Kit.GetMeta(info).PlayableDownloadManager.Download(songs.Cast<IDownloadable>());
            _services.Alerts.ShowInfo("Download all songs", $"{songs.Count} songs were added to the download queue.");
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Download all songs", ex);
        }
    }

    /// Port of the "Delete Cache" action of LibrarySettingsView.
    private async Task DeleteCacheAsync()
    {
        if (ActiveInfo is not { } info) return;
        var confirmed = await _services.Dialogs.ConfirmAsync("Delete Cache",
            "Are you sure you want to delete this account’s downloaded songs and podcast episodes?", "Delete", destructive: true);
        if (!confirmed) return;
        try
        {
            _services.Player.Stop();
            var account = _services.Library.GetAccount(info);
            var downloadManager = _services.Kit.GetMeta(info).PlayableDownloadManager;
            downloadManager.Stop();
            _services.Library.DeletePlayableCachePaths(account);
            _services.Library.SaveContext();
            CacheFileManager.Shared.DeletePlayableCache(info);
            downloadManager.Start();
            _services.Notifications.Post(AmperfyNotification.LibraryChanged, this);
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Delete Cache", ex);
        }
        UpdateCounts();
        UpdateCacheSizes();
    }
}
