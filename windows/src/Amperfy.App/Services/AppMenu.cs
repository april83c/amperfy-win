using Amperfy.App.Controls.Player;
using Amperfy.App.Helpers;
using Amperfy.App.Pages;
using Amperfy.App.Pages.Settings;
using Amperfy.App.Services.Player;
using Amperfy.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Amperfy.App.Services;

/// The app menu: the "…" button in the title bar (port of the macOS main menu of AppDelegateMainMenuExtension:
/// the "Controls" menu, the player windows, Settings and Help). The shortcuts are handled by
/// <see cref="PlayerKeyboardShortcuts"/>; the menu only shows them.
public static class AppMenu
{
    private static AppServices Services => AppServices.Instance;

    /// Title bar button with the app menu.
    public static Button CreateButton()
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = Icons.More, FontSize = 16 },
            Width = 40,
            Height = 32,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Flyout = CreateFlyout(),
        };
        ToolTipService.SetToolTip(button, "Menu");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Menu");
        return button;
    }

    public static MenuFlyout CreateFlyout()
    {
        var flyout = new MenuFlyout();
        flyout.Opening += (_, _) =>
        {
            flyout.Items.Clear();
            foreach (var item in CreateItems()) flyout.Items.Add(item);
        };
        return flyout;
    }

    /// Opens the settings in the main window (Ctrl+,).
    public static void OpenSettings(string? section = null)
    {
        if (ShellPage.Current is null) return;
        PlayerUi.BringMainWindowToFront();
        Services.Navigation.Navigate(typeof(SettingsPage), section);
    }

    private static IEnumerable<MenuFlyoutItemBase> CreateItems()
    {
        yield return CreateControlsMenu();
        yield return CreateViewMenu();
        yield return new MenuFlyoutSeparator();
        yield return Item("Settings", Icons.Settings, "Ctrl+,", () => OpenSettings());
        yield return Item("Keyboard Shortcuts", "", "F1", () => _ = KeyboardShortcutsDialog.ShowAsync());
        yield return new MenuFlyoutSeparator();
        yield return Item("Report an issue on GitHub", "", null, () => SettingsUi.OpenUri(SupportSettingsPage.IssuesUrl));
        yield return Item("About Amperfy", Icons.Info, null, () => OpenSettings(SettingsPage.AboutSection));
    }

    /// Port of AppDelegate.buildControlsMenu.
    private static MenuFlyoutSubItem CreateControlsMenu()
    {
        var player = PlayerUi.Player;
        var isPlaying = player.IsPlaying;
        var isSkipAvailable = isPlaying && player.IsSkipAvailable;
        var menu = new MenuFlyoutSubItem { Text = "Controls", Icon = new FontIcon { Glyph = Icons.Play } };
        var playTitle = isPlaying ? (player.IsStopInsteadOfPause ? "Stop Radio" : "Pause") : "Play";
        menu.Items.Add(Item(playTitle, PlayerUi.PlayGlyph, "Space", PlayerUi.TogglePlayPause));
        menu.Items.Add(Item(isPlaying && player.IsStopInsteadOfPause ? "Stop Player" : "Stop", Icons.Stop, "Ctrl+.", PlayerUi.Stop));
        menu.Items.Add(Item("Next Track", Icons.Next, "Ctrl+Right", player.PlayNext));
        menu.Items.Add(Item("Previous Track", Icons.Previous, "Ctrl+Left", player.PlayPreviousOrReplay));
        menu.Items.Add(Item($"Skip Forward: {(int)player.SkipForwardInterval} sec.", Icons.SkipForward, "Ctrl+Shift+Right", PlayerUi.SkipForward, isSkipAvailable));
        menu.Items.Add(Item($"Skip Backward: {(int)player.SkipBackwardInterval} sec.", Icons.SkipBack, "Ctrl+Shift+Left", PlayerUi.SkipBackward, isSkipAvailable));
        menu.Items.Add(Item("Go to Current Song", Icons.Album, "Ctrl+L", PlayerUi.GoToCurrent,
            player.CurrentlyPlaying?.AsSong?.Album is not null || player.CurrentlyPlaying?.AsPodcastEpisode?.Podcast is not null));
        menu.Items.Add(new MenuFlyoutSeparator());

        if (player.PlayerMode == PlayerMode.Music && PlayerUi.IsShuffleEnabled)
        {
            var shuffle = new MenuFlyoutSubItem { Text = "Shuffle", Icon = new FontIcon { Glyph = Icons.Shuffle } };
            shuffle.Items.Add(Radio("On", "shuffle", player.IsShuffle, () => { if (!player.IsShuffle) player.ToggleShuffle(); }));
            shuffle.Items.Add(Radio("Off", "shuffle", !player.IsShuffle, () => { if (player.IsShuffle) player.ToggleShuffle(); }));
            menu.Items.Add(shuffle);
        }
        if (player.PlayerMode == PlayerMode.Music)
        {
            var repeat = new MenuFlyoutSubItem { Text = "Repeat", Icon = new FontIcon { Glyph = PlayerUi.RepeatGlyph } };
            foreach (var mode in Enum.GetValues<RepeatMode>())
            {
                repeat.Items.Add(Radio(mode.Description(), "repeat", mode == player.RepeatMode, () => player.SetRepeatMode(mode)));
            }
            menu.Items.Add(repeat);
        }
        menu.Items.Add(PlayerUi.CreatePlaybackRateMenuItem());
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(PlayerUi.CreateSleepTimerMenuItem());
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Switch Music/Podcast mode", player.PlayerMode == PlayerMode.Music ? PlayerGlyphs.MusicMode : PlayerGlyphs.PodcastMode,
            "Ctrl+M", PlayerUi.SwitchPlayerMode));
        return menu;
    }

    /// Player views and windows (the File/View menus of the macOS app).
    private static MenuFlyoutSubItem CreateViewMenu()
    {
        var menu = new MenuFlyoutSubItem { Text = "View", Icon = new FontIcon { Glyph = PlayerGlyphs.FullScreen } };
        menu.Items.Add(Item(PlayerUi.IsNowPlayingVisible ? "Close Now Playing" : "Now Playing", PlayerGlyphs.FullScreen, "Ctrl+Shift+P", PlayerUi.ToggleNowPlaying));
        menu.Items.Add(Item(PlayerUi.IsQueuePaneVisible ? "Hide Queue" : "Show Queue", Icons.Queue, "Ctrl+Shift+Q", PlayerUi.ToggleQueuePane));
        if (PlayerUi.IsLyricsAvailable || PlayerUi.IsLyricsPaneVisible)
        {
            menu.Items.Add(Item(PlayerUi.IsLyricsPaneVisible ? "Hide Lyrics" : "Show Lyrics", Icons.Lyrics, "Ctrl+Shift+L", PlayerUi.ToggleLyricsPane));
        }
        menu.Items.Add(Item(MiniPlayerWindow.IsOpen ? "Switch to Library" : "Switch to Mini Player", Icons.MiniPlayer, "Ctrl+Shift+M", PlayerUi.ToggleMiniPlayer));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Search", Icons.Search, "Ctrl+F", () =>
        {
            var box = Services.MainWindow.SearchBoxControl;
            if (box.Visibility == Visibility.Visible) box.Focus(FocusState.Programmatic);
        }));
        menu.Items.Add(Item("Toggle Sidebar", "", null, Services.MainWindow.TogglePane));
        menu.Items.Add(Item("Go Back", BackGlyph, "Alt+Left", Services.Navigation.GoBack, Services.Navigation.CanGoBack));
        return menu;
    }

    private const string BackGlyph = "";

    private static MenuFlyoutItem Item(string text, string glyph, string? keys, Action action, bool isEnabled = true)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, IsEnabled = isEnabled };
        if (keys is not null) item.KeyboardAcceleratorTextOverride = keys;
        item.Click += (_, _) => action();
        return item;
    }

    private static RadioMenuFlyoutItem Radio(string text, string group, bool isChecked, Action action)
    {
        var item = new RadioMenuFlyoutItem { Text = text, GroupName = group, IsChecked = isChecked };
        item.Click += (_, _) => action();
        return item;
    }
}
