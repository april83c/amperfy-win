using Amperfy.App.Services;
using Amperfy.Core;
using Amperfy.Core.Common;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Pages.Settings;

/// General settings (port of SettingsView): version, offline mode, screen lock prevention, the keyboard
/// shortcuts and links to the other sections.
public sealed partial class GeneralSettingsPage : Page
{
    private readonly AppServices _services = AppServices.Instance;

    private static readonly ScreenLockPreventionPreference[] ScreenLockValues =
        [ScreenLockPreventionPreference.Never, ScreenLockPreventionPreference.Always, ScreenLockPreventionPreference.OnlyIfCharging];

    public GeneralSettingsPage()
    {
        InitializeComponent();
        var user = _services.Settings.User;
        VersionText.Text = AmperfyInfo.Version;
        OfflineModeToggle.Bind(user.IsOfflineMode, SetOfflineMode);
        ScreenLockCombo.Bind(ScreenLockValues, v => v.Description(), user.ScreenLockPreventionPreference, v =>
        {
            user.ScreenLockPreventionPreference = v;
            ScreenLockPreventionService.Apply();
        });
        KeyboardHost.Children.Add(SettingsUi.ActionCard("Keyboard Shortcuts", "Player, navigation and list shortcuts (F1).", "\uE765",
            () => _ = KeyboardShortcutsDialog.ShowAsync()));
        foreach (var section in SettingsPage.Sections.Skip(1))
        {
            SectionLinksHost.Children.Add(SettingsUi.ActionCard(section.Title, null, section.Glyph,
                () => SettingsPage.Open(Frame, section.Id)));
        }
    }

    /// Port of AppDelegate.switchOnlineOfflineMode.
    private void SetOfflineMode(bool isOfflineMode)
    {
        if (_services.Settings.User.IsOfflineMode == isOfflineMode) return;
        _services.Settings.User.IsOfflineMode = isOfflineMode;
        try { _services.Player.IsOfflineMode = isOfflineMode; }
        catch (Exception ex) { AmperfyLog.Warning("Settings", $"Player offline mode: {ex.Message}"); }
        _services.Notifications.Post(AmperfyNotification.OfflineModeChanged, this);
    }
}
