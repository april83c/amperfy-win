using Amperfy.App.Controls.Player;
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
    }
}
