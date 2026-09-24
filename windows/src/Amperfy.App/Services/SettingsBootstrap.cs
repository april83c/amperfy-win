using Amperfy.Core.Common;
using Amperfy.Core.Storage;

namespace Amperfy.App.Services;

/// Applies the persisted settings that need app side services at startup (accent color,
/// screen lock prevention, notifications, cache size bookkeeping) and tears them down on exit.
public static class SettingsBootstrap
{
    /// Call on the UI thread after AppServices.Initialize() and before the main window is created.
    public static void Initialize(AppServices services)
    {
        Run("Theme", () => ThemeService.Initialize(services));
        Run("ScreenLockPrevention", () => ScreenLockPreventionService.Initialize(services));
        Run("Notifications", () => ToastNotificationService.Initialize(services));
        // streaming settings distinguish unmetered and metered networks ("WiFi"/"Cellular" on iOS)
        Run("MeteredNetwork", () =>
        {
            if (services.Kit.NetworkMonitor is NetworkMonitor { IsMeteredProvider: null } monitor)
                monitor.IsMeteredProvider = MeteredConnectionDetector.IsMetered;
        });
        // the playable cache size is tracked incrementally; compute the start value (used for the cache limit)
        var fileManager = CacheFileManager.Shared;
        _ = Task.Run(() => Run("CacheSize", fileManager.RecalculatePlayableCacheSizes));
    }

    public static void Shutdown()
    {
        Run("Notifications", ToastNotificationService.Shutdown);
        Run("ScreenLockPrevention", ScreenLockPreventionService.Shutdown);
    }

    private static void Run(string topic, Action action)
    {
        try { action(); }
        catch (Exception ex) { AmperfyLog.Error("SettingsBootstrap", $"{topic}: {ex}"); }
    }
}
