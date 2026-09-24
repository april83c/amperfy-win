using Amperfy.App.Controls.Player;
using Amperfy.App.Library;
using Amperfy.App.Pages;
using Amperfy.Core.Common;

namespace Amperfy.App.Services.Player;

/// Player UI setup of the main window (called once after the main window was created).
public static class PlayerUiService
{
    private static bool _isInitialized;
    private static IDisposable? _downloadRegistration;

    /// Keeps the registration alive for the app lifetime.
    internal static bool IsRegistered => _downloadRegistration is not null;

    public static void Initialize(MainWindow window)
    {
        if (_isInitialized) return;
        _isInitialized = true;
        // shortcuts only while the library shell (not login/sync) is shown
        PlayerKeyboardShortcuts.Attach(window, () => window.RootContentFrame.Content is ShellPage);
        window.Closed += (_, _) => MiniPlayerWindow.CloseForShutdown();
        _downloadRegistration = AppServices.Instance.Notifications.Register(AmperfyNotification.DownloadFinishedSuccess, PlayerUi.OnDownloadFinished);
        // "Show Lyrics" of the currently playing song opens the synced lyrics of the player instead of the text dialog
        EntityActions.ShowLyricsHandler = song =>
        {
            if (ReferenceEquals(AppServices.Instance.Player.CurrentlyPlaying, song) && PlayerUi.ShowCurrentLyrics()) return;
            _ = EntityActions.ShowLyricsDialogAsync(song);
        };
    }
}
