using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Pages.Settings;

/// Display & interaction settings (port of DisplaySettingsView). Changes are read by the pages
/// and the player the next time they render.
public sealed partial class DisplaySettingsPage : Page
{
    private readonly AppServices _services = AppServices.Instance;

    private static readonly AppearanceMode[] AppearanceValues = [AppearanceMode.System, AppearanceMode.Light, AppearanceMode.Dark];

    public DisplaySettingsPage()
    {
        InitializeComponent();
        var user = _services.Settings.User;
        AppearanceCombo.Bind(AppearanceValues, m => m.ToString(), user.AppearanceMode, mode =>
        {
            user.AppearanceMode = mode;
            ThemeService.ApplyAppearance(mode);
        });
        ThemeColorCard.Click += (_, _) => SettingsPage.Open(Frame, SettingsPage.AccountSection);
        DetailedInfoToggle.Bind(user.IsShowDetailedInfo, v => user.IsShowDetailedInfo = v);
        SongDurationToggle.Bind(user.IsShowSongDuration, v => user.IsShowSongDuration = v);
        AlbumDurationToggle.Bind(user.IsShowAlbumDuration, v => user.IsShowAlbumDuration = v);
        ArtistDurationToggle.Bind(user.IsShowArtistDuration, v => user.IsShowArtistDuration = v);
        RatingToggle.Bind(user.IsShowRating, v => user.IsShowRating = v);
        SkipButtonsToggle.Bind(user.IsShowMusicPlayerSkipButtons, v => user.IsShowMusicPlayerSkipButtons = v);
        DisableShuffleToggle.Bind(!user.IsPlayerShuffleButtonEnabled, v => user.IsPlayerShuffleButtonEnabled = !v);
        LyricsScrollingToggle.Bind(user.IsLyricsSmoothScrolling, v => user.IsLyricsSmoothScrolling = v);
        MiniPlayerOnTopToggle.Bind(user.IsMiniPlayerAlwaysOnTop, v => user.IsMiniPlayerAlwaysOnTop = v);
        // Ampache has no synced lyrics (as in Swift, the option is hidden for Ampache accounts)
        LyricsScrollingCard.Visibility = _services.ActiveApiType == ServerApiType.Ampache ? Visibility.Collapsed : Visibility.Visible;
    }
}
