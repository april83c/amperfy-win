using Amperfy.App.Services;
using Amperfy.Core.Common;
using Amperfy.Core.Downloads;
using Amperfy.Core.Storage;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Pages.Settings;

/// Artwork settings of the active account (port of ArtworkSettingsView, ArtworkDownloadSettingsView
/// and ArtworkDisplaySettings).
public sealed partial class ArtworkSettingsPage : Page
{
    /// Swift: ArtworkSettingsView.artworkNotCheckedThreshold
    private const int ArtworkNotCheckedThreshold = 10;

    private readonly AppServices _services = AppServices.Instance;
    private readonly List<IDisposable> _subscriptions = [];
    private DispatcherQueueTimer? _refreshTimer;
    private bool _isLoading;

    private static readonly ArtworkDownloadSetting[] DownloadValues =
        [ArtworkDownloadSetting.UpdateOncePerSession, ArtworkDownloadSetting.OnlyOnce, ArtworkDownloadSetting.Never];
    private static readonly ArtworkDisplayPreference[] DisplayValues =
    [
        ArtworkDisplayPreference.PreferId3Tag, ArtworkDisplayPreference.PreferServerArtwork,
        ArtworkDisplayPreference.ServerArtworkOnly, ArtworkDisplayPreference.Id3TagOnly,
    ];

    public ArtworkSettingsPage()
    {
        InitializeComponent();
        foreach (var value in DownloadValues) DownloadSettingCombo.Items.Add(value.Description());
        foreach (var value in DisplayValues) DisplaySettingCombo.Items.Add(value.Description());
        DownloadSettingCombo.SelectionChanged += (_, _) =>
        {
            if (DownloadSettingCombo.SelectedIndex < 0) return;
            var value = DownloadValues[DownloadSettingCombo.SelectedIndex];
            UpdateAccountSetting(s => s.ArtworkDownloadSetting = value);
        };
        DisplaySettingCombo.SelectionChanged += (_, _) =>
        {
            if (DisplaySettingCombo.SelectedIndex < 0) return;
            var value = DisplayValues[DisplaySettingCombo.SelectedIndex];
            UpdateAccountSetting(s => s.ArtworkDisplayPreference = value);
        };
        DownloadAllButton.Click += (_, _) => _ = DownloadAllAsync();
        DeleteAllButton.Click += (_, _) => _ = DeleteAllAsync();
        Loaded += (_, _) =>
        {
            if (_subscriptions.Count == 0)
                _subscriptions.Add(_services.Notifications.Register(AmperfyNotification.AccountActiveChanged, _ => Reload()));
            Reload();
            if (_refreshTimer is null)
            {
                _refreshTimer = DispatcherQueue.CreateTimer();
                _refreshTimer.Interval = TimeSpan.FromSeconds(2);
                _refreshTimer.Tick += (_, _) => UpdateCounts();
            }
            _refreshTimer.Start();
        };
        Unloaded += (_, _) =>
        {
            _refreshTimer?.Stop();
            foreach (var s in _subscriptions) s.Dispose();
            _subscriptions.Clear();
        };
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
            AccountHintText.Text = $"Artworks of the account {setting.LoginCredentials?.Username} · {setting.LoginCredentials?.DisplayServerUrl}.";
            DownloadSettingCombo.SelectedIndex = Array.IndexOf(DownloadValues, setting.ArtworkDownloadSetting);
            DisplaySettingCombo.SelectedIndex = Array.IndexOf(DisplayValues, setting.ArtworkDisplayPreference);
            UpdateCounts();
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
            var count = library.GetArtworkCount(account);
            var notChecked = library.GetArtworkNotCheckedCount(account);
            var cached = library.GetCachedArtworkCount(account);
            ArtworkCountText.Text = count.ToString();
            NotCheckedCountText.Text = (notChecked > ArtworkNotCheckedThreshold ? notChecked : 0).ToString();
            CachedCountText.Text = cached.ToString();
            ArtworkCountsExpander.Description = $"{cached} of {count} artworks cached";
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("ArtworkSettings", $"Counts could not be updated: {ex.Message}");
        }
    }

    private async Task DownloadAllAsync()
    {
        if (ActiveInfo is not { } info) return;
        if (_services.Settings.User.IsOfflineMode)
        {
            await _services.Dialogs.ShowMessageAsync("Offline Mode", "Artworks can't be downloaded in offline mode.");
            return;
        }
        var confirmed = await _services.Dialogs.ConfirmAsync("Download all artworks in library",
            "This action will add all uncached artworks to the download queue. With this action a lot network traffic can be generated and device storage capacity will be taken. Continue?");
        if (!confirmed) return;
        try
        {
            var account = _services.Library.GetAccount(info);
            var artworks = _services.Library.GetArtworksForCompleteLibraryDownload(account);
            _services.Kit.GetMeta(info).ArtworkDownloadManager.Download(artworks.Cast<IDownloadable>());
            _services.Alerts.ShowInfo("Download all artworks", $"{artworks.Count} artworks were added to the download queue.");
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Download all artworks", ex);
        }
    }

    private async Task DeleteAllAsync()
    {
        if (ActiveInfo is not { } info) return;
        var confirmed = await _services.Dialogs.ConfirmAsync("Delete all downloaded artworks",
            "This action will delete downloaded artworks. Artworks embedded in song/podcast episode files will be kept. Continue?", "Delete", destructive: true);
        if (!confirmed) return;
        try
        {
            var account = _services.Library.GetAccount(info);
            var manager = _services.Kit.GetMeta(info).ArtworkDownloadManager;
            manager.Stop();
            manager.CancelDownloads();
            manager.ClearFinishedDownloads();
            _services.Library.DeleteRemoteArtworkCachePaths(account);
            _services.Library.SaveContext();
            CacheFileManager.Shared.DeleteRemoteArtworkCache(info);
            manager.Start();
            _services.Notifications.Post(AmperfyNotification.LibraryChanged, this);
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Delete artworks", ex);
        }
        UpdateCounts();
    }
}
