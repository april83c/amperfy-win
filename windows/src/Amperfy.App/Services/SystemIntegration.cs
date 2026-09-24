using Amperfy.App.Pages;
using Amperfy.Core.Common;
using Amperfy.Core.Sync;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.Networking.Connectivity;

namespace Amperfy.App.Services;

/// Windows integration of platform services: toast notifications (replacement for the iOS local
/// notifications of new podcast episodes) and metered connection detection (the iOS "cellular"
/// settings apply to metered connections).
public static class SystemIntegration
{
    private static bool _isToastRegistered;

    /// Metered connection = "cellular" in the streaming settings.
    public static bool IsMeteredConnection()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null) return false;
            var cost = profile.GetConnectionCost();
            return cost.NetworkCostType is NetworkCostType.Fixed or NetworkCostType.Variable || cost.Roaming || cost.OverDataLimit;
        }
        catch (Exception ex)
        {
            AmperfyLog.Error("SystemIntegration", $"Connection cost unavailable: {ex.Message}");
            return false;
        }
    }

    public static void InitializeToasts(LocalNotificationManager manager)
    {
        try
        {
            var notificationManager = AppNotificationManager.Default;
            notificationManager.NotificationInvoked += (_, args) =>
            {
                var arguments = new Dictionary<string, string>(args.Arguments);
                MainThread.Post(() => HandleToastActivation(arguments));
            };
            notificationManager.Register();
            _isToastRegistered = true;
        }
        catch (Exception ex)
        {
            AmperfyLog.Error("SystemIntegration", $"Toast registration failed: {ex.Message}");
        }
        manager.NotificationRequested += request =>
        {
            if (!_isToastRegistered) return;
            try
            {
                var builder = new AppNotificationBuilder()
                    .AddText(request.Title)
                    .AddText(request.Body);
                foreach (var (key, value) in request.UserInfo) builder.AddArgument(key, value);
                if (request.ImagePath is { } image && File.Exists(image))
                    builder.SetAppLogoOverride(new Uri(image), AppNotificationImageCrop.Default);
                AppNotificationManager.Default.Show(builder.BuildNotification());
            }
            catch (Exception ex)
            {
                AmperfyLog.Error("SystemIntegration", $"Toast failed: {ex.Message}");
            }
        };
    }

    public static void Shutdown()
    {
        if (!_isToastRegistered) return;
        try { AppNotificationManager.Default.Unregister(); } catch { }
        _isToastRegistered = false;
    }

    /// Toast clicked: bring the window to the front and show the podcast of the episode.
    public static void HandleToastActivation(IReadOnlyDictionary<string, string> arguments)
    {
        var services = AppServices.Instance;
        services.MainWindow?.BringToFront();
        if (!arguments.TryGetValue(NotificationUserInfo.Type, out var type) || type != nameof(NotificationContentType.PodcastEpisode)) return;
        if (!arguments.TryGetValue(NotificationUserInfo.Id, out var id)) return;
        var account = services.Kit.Settings.Accounts.AllAccounts
            .Where(a => !arguments.TryGetValue(NotificationUserInfo.Account, out var ident) || a.Ident == ident)
            .Select(services.Library.GetAccount)
            .FirstOrDefault();
        if (account is null) return;
        var episode = services.Library.GetPodcastEpisode(account, id);
        if (episode?.Podcast is { } podcast && ShellPage.Current is not null)
        {
            services.Navigation.Navigate(typeof(PodcastDetailPage), podcast);
        }
    }
}
