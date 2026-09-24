using Amperfy.App.Services;
using Amperfy.Core;
using Amperfy.Core.Common;
using Amperfy.Core.Model;
using Amperfy.Core.Sync;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Amperfy.App.Library;

/// Shared pieces of the library category and detail pages.
public static class CategoryPageHelper
{
    private static AppServices Services => AppServices.Instance;

    /// "Jump to" flyout with the alphabetic section initials of the list (desktop replacement of the
    /// iOS table section index).
    public static Flyout CreateJumpFlyout(Func<IReadOnlyList<string>> initials, Action<string> jump)
    {
        var flyout = new Flyout();
        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 7, ItemWidth = 44, ItemHeight = 40 };
        flyout.Content = grid;
        flyout.Opening += (_, _) =>
        {
            grid.Children.Clear();
            IReadOnlyList<string> sections;
            try { sections = initials(); }
            catch (Exception ex)
            {
                AmperfyLog.Error("CategoryPageHelper", $"Section initials failed: {ex.Message}");
                sections = [];
            }
            foreach (var section in sections)
            {
                var button = new Button { Content = section, Width = 40, Height = 36, Padding = new Thickness(0) };
                button.Click += (_, _) =>
                {
                    flyout.Hide();
                    jump(section);
                };
                grid.Children.Add(button);
            }
            if (sections.Count == 0) grid.Children.Add(new TextBlock { Text = "No sections", Margin = new Thickness(8) });
        };
        return flyout;
    }

    /// Shows the empty state in the host when the list is empty.
    public static void UpdateEmptyState(Border host, bool isEmpty, bool isSearchActive, string glyph, string title)
    {
        if (!isEmpty)
        {
            host.Visibility = Visibility.Collapsed;
            return;
        }
        var heading = isSearchActive ? "No Results" : $"No {title}";
        var text = isSearchActive ? "Check the spelling or try a new search." : $"Your {title.ToLowerInvariant()} will appear here.";
        if (host.Child is StackPanel panel) Ui.UpdateEmptyState(panel, isSearchActive ? Helpers.Icons.Search : glyph, heading, text);
        else host.Child = Ui.EmptyState(isSearchActive ? Helpers.Icons.Search : glyph, heading, text);
        host.Visibility = Visibility.Visible;
    }

    /// Pull-to-refresh replacement: syncs the newest library elements (Swift: handleRefresh).
    public static async Task SyncNewestLibraryElementsAsync(Account account, string topic)
    {
        if (!Services.Settings.User.IsOnlineMode) return;
        try
        {
            var meta = Services.Kit.GetMeta(account.Info);
            await new AutoDownloadLibrarySyncer(Services.Library, Services.Settings, account, meta.LibrarySyncer, meta.PlayableDownloadManager)
                .SyncNewestLibraryElementsAsync();
        }
        catch (Exception ex)
        {
            Services.EventLogger.Report(topic, ex);
        }
    }

    /// Runs a server call (online mode only), reports errors with the given topic.
    public static async Task<bool> SyncAsync(string topic, Func<Task> sync, bool displayPopup = true)
    {
        if (!Services.Settings.User.IsOnlineMode) return false;
        try
        {
            await sync();
            return true;
        }
        catch (Exception ex)
        {
            Services.EventLogger.Report(topic, ex, displayPopup);
            return false;
        }
    }

    /// The "Cached" toggle: forced on in offline mode.
    public static AppBarToggleButton AddCachedToggle(Controls.LibraryPageHeader header, Func<bool> get, Action<bool> set)
    {
        var offline = Services.Settings.User.IsOfflineMode;
        var toggle = header.AddToggleCommand("Cached", LibraryGlyphs.Cached, get() || offline, isChecked => set(isChecked),
            "Show only cached items");
        toggle.IsEnabled = !offline;
        return toggle;
    }

    public static bool IsOffline => Services.Settings.User.IsOfflineMode;
    public static bool IsOnline => Services.Settings.User.IsOnlineMode;

    public static int MaxSongsToAddOnce => Services.Player.MaxSongsToAddOnce;

    public static ServerApiType? ApiType(Account account) => account.ApiType.AsServerApiType();

    /// Shows the menu of a button (used for "More" commands without a static flyout).
    public static void ShowMenu(FrameworkElement target, IEnumerable<MenuFlyoutItemBase> items)
    {
        var flyout = new MenuFlyout();
        foreach (var item in items) flyout.Items.Add(item);
        flyout.ShowAt(target, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Bottom });
    }

    public static int NewestFetchCount => AmperfyInfo.NewestElementsFetchCount;
}
