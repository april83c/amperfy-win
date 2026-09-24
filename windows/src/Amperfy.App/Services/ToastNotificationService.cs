using Amperfy.App.Pages;
using Amperfy.Core.Common;
using Amperfy.Core.Downloads;
using Amperfy.Core.Sync;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Amperfy.App.Services;

/// Shows Windows toast notifications (replacement for the iOS local notifications):
/// new podcast episodes found by the background fetch (UserSettings.IsPodcastNotificationsEnabled)
/// and finished downloads (UserSettings.IsDownloadNotificationsEnabled).
public static class ToastNotificationService
{
    private const string ArgAccount = "account";
    private const string ArgEpisode = "episode";
    private static readonly TimeSpan DownloadIdleCheckInterval = TimeSpan.FromSeconds(3);

    private static AppServices? _services;
    private static DispatcherQueue? _dispatcher;
    private static bool _isRegistered;
    private static IDisposable? _downloadSubscription;
    private static IDisposable? _downloadIdleTimer;
    private static int _finishedDownloadCount;

    public static void Initialize(AppServices services)
    {
        if (_services is not null) return;
        _services = services;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        services.Kit.LocalNotificationManager.NotificationRequested += Show;
        _downloadSubscription = services.Notifications.Register(AmperfyNotification.DownloadFinishedSuccess, DownloadFinished);
        try
        {
            if (!AppNotificationManager.IsSupported()) return;
            AppNotificationManager.Default.NotificationInvoked += NotificationInvoked;
            AppNotificationManager.Default.Register();
            _isRegistered = true;
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("Notifications", $"App notifications unavailable: {ex.Message}");
        }
    }

    public static void Shutdown()
    {
        _downloadIdleTimer?.Dispose();
        _downloadIdleTimer = null;
        _downloadSubscription?.Dispose();
        _downloadSubscription = null;
        if (_services is not null) _services.Kit.LocalNotificationManager.NotificationRequested -= Show;
        if (!_isRegistered) return;
        try { AppNotificationManager.Default.Unregister(); }
        catch (Exception ex) { AmperfyLog.Warning("Notifications", $"Unregister failed: {ex.Message}"); }
        _isRegistered = false;
    }

    /// Shows a local notification request (podcast episodes, debug messages).
    private static void Show(LocalNotificationRequest request)
    {
        if (!_isRegistered) return;
        try
        {
            var builder = new AppNotificationBuilder()
                .AddText(request.Title)
                .AddText(request.Body);
            if (request.AccountIdent is { } account && request.ElementId is { } id && request.ContentType == NotificationContentType.PodcastEpisode)
            {
                builder.AddArgument(ArgAccount, account).AddArgument(ArgEpisode, id);
            }
            if (request.ImagePath is { } imagePath && File.Exists(imagePath))
            {
                builder.SetAppLogoOverride(new Uri(imagePath));
            }
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("Notifications", $"Notification could not be shown: {ex.Message}");
        }
    }

    private static void ShowText(string title, string body)
    {
        if (!_isRegistered) return;
        try
        {
            AppNotificationManager.Default.Show(new AppNotificationBuilder().AddText(title).AddText(body).BuildNotification());
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("Notifications", $"Notification could not be shown: {ex.Message}");
        }
    }

    private static void DownloadFinished(NotificationArgs args)
    {
        if (_services is null || !_services.Settings.User.IsDownloadNotificationsEnabled) return;
        if (args.Payload is not DownloadNotification { } download ||
            !download.Id.StartsWith(DownloadableType.Playable.ToString().ToLowerInvariant(), StringComparison.Ordinal)) return;
        _finishedDownloadCount++;
        _downloadIdleTimer ??= MainThread.CreateTimer(DownloadIdleCheckInterval, CheckDownloadsIdle, repeats: true);
    }

    /// Shows one notification when all playable downloads have finished.
    private static void CheckDownloadsIdle()
    {
        if (_services is null) return;
        var isIdle = _services.Kit.AllActiveMetas.Values.All(m =>
            m.PlayableDownloadManager.ActiveDownloadCount == 0 && m.PlayableDownloadManager.QueuedDownloadCount == 0);
        if (!isIdle) return;
        _downloadIdleTimer?.Dispose();
        _downloadIdleTimer = null;
        var count = _finishedDownloadCount;
        _finishedDownloadCount = 0;
        if (count <= 0 || !_services.Settings.User.IsDownloadNotificationsEnabled) return;
        ShowText("Downloads finished", count == 1 ? "1 item was downloaded." : $"{count} items were downloaded.");
    }

    /// Notification clicked (background thread): bring the window to the front and show the podcast.
    private static void NotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        _dispatcher?.TryEnqueue(() =>
        {
            if (_services is null) return;
            try
            {
                _services.MainWindow?.Activate();
                if (!args.Arguments.TryGetValue(ArgAccount, out var accountIdent) ||
                    !args.Arguments.TryGetValue(ArgEpisode, out var episodeId)) return;
                if (ShellPage.Current is null) return;
                var account = _services.Library.GetAccount(accountIdent);
                if (account is null || account.Info != _services.Settings.Accounts.Active) return;
                var episode = _services.Library.GetPodcastEpisode(account, episodeId);
                if (episode?.Podcast is not { } podcast) return;
                if (PageRegistry.ForEntity(podcast) is { } target) _services.Navigation.Navigate(target.Page, target.Parameter);
            }
            catch (Exception ex)
            {
                AmperfyLog.Warning("Notifications", $"Notification activation failed: {ex.Message}");
            }
        });
    }
}
