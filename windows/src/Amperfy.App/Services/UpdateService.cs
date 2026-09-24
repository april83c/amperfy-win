using Amperfy.Core;
using Amperfy.Core.Common;
using Velopack;
using Velopack.Sources;

namespace Amperfy.App.Services;

/// Self-updates via Velopack from the GitHub releases of this repository. Only active when the app
/// was installed with the Velopack installer (not when run from a zip / the build output).
/// Checks shortly after start and then every few hours; a downloaded update is applied when the
/// user clicks "Restart" or otherwise on the next exit.
public static class UpdateService
{
    public const string RepositoryUrl = "https://github.com/april83c/amperfy-win";
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private static UpdateManager? _manager;
    private static UpdateInfo? _pendingUpdate;
    private static bool _isChecking;
    private static IDisposable? _timer;

    public enum Status
    {
        NotInstalled,
        UpToDate,
        UpdateReady,
        Failed,
    }

    private static UpdateManager? Manager
    {
        get
        {
            if (_manager is not null) return _manager;
            try
            {
                _manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
            }
            catch (Exception ex)
            {
                AmperfyLog.Warning("Update", $"Update manager unavailable: {ex.Message}");
            }
            return _manager;
        }
    }

    /// True if the app runs from a Velopack installation (updates possible).
    public static bool IsInstalled
    {
        get
        {
            try { return Manager?.IsInstalled ?? false; }
            catch { return false; }
        }
    }

    public static string CurrentVersion => (IsInstalled ? Manager?.CurrentVersion?.ToString() : null) ?? AmperfyInfo.Version;

    /// Version of the downloaded update that is applied on the next start (null if none).
    public static string? PendingVersion => _pendingUpdate?.TargetFullRelease.Version.ToString();

    /// Starts the periodic update checks (UI thread, after the main window was created).
    public static void Start()
    {
        if (!IsInstalled) return;
        _timer ??= MainThread.CreateTimer(CheckInterval, () => _ = CheckAndDownloadAsync(isUserInitiated: false));
        _ = Task.Delay(StartupDelay).ContinueWith(_ => MainThread.Post(() => _ = CheckAndDownloadAsync(isUserInitiated: false)),
            TaskScheduler.Default);
    }

    public static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// Checks for an update and downloads it. Returns the resulting status.
    public static async Task<Status> CheckAndDownloadAsync(bool isUserInitiated)
    {
        if (!IsInstalled || Manager is not { } manager) return Status.NotInstalled;
        if (_pendingUpdate is not null)
        {
            if (isUserInitiated) OfferRestart();
            return Status.UpdateReady;
        }
        if (_isChecking) return Status.UpToDate;
        _isChecking = true;
        try
        {
            var update = await Task.Run(() => manager.CheckForUpdatesAsync());
            if (update is null)
            {
                AmperfyLog.Info("Update", $"Amperfy {CurrentVersion} is up to date");
                return Status.UpToDate;
            }
            AmperfyLog.Info("Update", $"Downloading update {update.TargetFullRelease.Version}");
            await Task.Run(() => manager.DownloadUpdatesAsync(update));
            _pendingUpdate = update;
            // applied on exit if the user doesn't restart now
            manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);
            OfferRestart();
            return Status.UpdateReady;
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("Update", $"Update check failed: {ex.Message}");
            if (isUserInitiated)
                AppServices.Instance.Alerts.ShowInfo("Update check failed", ex.Message);
            return Status.Failed;
        }
        finally
        {
            _isChecking = false;
        }
    }

    private static void OfferRestart()
    {
        if (PendingVersion is not { } version) return;
        AppServices.Instance.Alerts.ShowAction("Update available",
            $"Amperfy {version} has been downloaded and will be installed when you close Amperfy.",
            "Restart now", RestartToUpdate);
    }

    /// Installs the downloaded update and restarts the app.
    public static void RestartToUpdate()
    {
        if (_pendingUpdate is not { } update || Manager is not { } manager) return;
        try
        {
            AppServices.Instance.Settings.SaveNow();
            manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
        }
        catch (Exception ex)
        {
            AmperfyLog.Error("Update", $"Applying the update failed: {ex}");
            AppServices.Instance.Alerts.ShowInfo("Update failed", ex.Message);
        }
    }
}
