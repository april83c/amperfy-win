using Amperfy.App.Services;
using Amperfy.Core.Sync;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Amperfy.App.Pages.Settings;

/// Notification settings (Windows replacement for the iOS notification permission): toast
/// notifications for new podcast episodes (background fetch) and finished downloads.
public sealed partial class NotificationSettingsPage : Page
{
    private readonly AppServices _services = AppServices.Instance;

    public NotificationSettingsPage()
    {
        InitializeComponent();
        var user = _services.Settings.User;
        var minutes = (int)PeriodicBackgroundFetcher.DefaultInterval.TotalMinutes;
        PodcastNotificationsCard.Description =
            $"While Amperfy is running, podcasts are checked for new episodes every {minutes} minutes. Show a notification for each new episode.";
        PodcastNotificationsToggle.Bind(user.IsPodcastNotificationsEnabled, v => user.IsPodcastNotificationsEnabled = v);
        DownloadNotificationsToggle.Bind(user.IsDownloadNotificationsEnabled, v => user.IsDownloadNotificationsEnabled = v);
        SystemSettingsCard.Click += (_, _) => SettingsUi.OpenUri("ms-settings:notifications");
        EventLogCard.Click += (_, _) => Frame?.Navigate(typeof(EventLogSettingsPage), null, new EntranceNavigationTransitionInfo());
    }
}
