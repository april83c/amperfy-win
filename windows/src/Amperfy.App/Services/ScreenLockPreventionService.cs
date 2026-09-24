using System.Runtime.InteropServices;
using Amperfy.Core.Common;
using Amperfy.Core.Storage;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.System.Power;

namespace Amperfy.App.Services;

/// Keeps the display on while Amperfy runs, depending on <see cref="UserSettings.ScreenLockPreventionPreference"/>
/// (port of AppDelegate.configureLockScreenPrevention; iOS: UIApplication.isIdleTimerDisabled).
/// SetThreadExecutionState is per thread: all calls run on the UI thread.
public static class ScreenLockPreventionService
{
    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
        Continuous = 0x80000000,
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(ExecutionState esFlags);

    private static AppServices? _services;
    private static DispatcherQueue? _dispatcher;
    private static bool _isPowerEventRegistered;
    private static bool? _isPreventing;

    public static void Initialize(AppServices services)
    {
        _services = services;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Apply();
    }

    /// Applies the current setting (call after the setting changed).
    public static void Apply()
    {
        if (_services is null) return;
        var preference = _services.Settings.User.ScreenLockPreventionPreference;
        if (preference == ScreenLockPreventionPreference.OnlyIfCharging) RegisterPowerEvents();
        else UnregisterPowerEvents();
        SetPreventing(preference switch
        {
            ScreenLockPreventionPreference.Always => true,
            ScreenLockPreventionPreference.OnlyIfCharging => IsCharging,
            _ => false,
        });
    }

    public static string Description(this ScreenLockPreventionPreference preference) => preference switch
    {
        ScreenLockPreventionPreference.Always => "Always",
        ScreenLockPreventionPreference.OnlyIfCharging => "When plugged in",
        _ => "Never",
    };

    private static bool IsCharging
    {
        get
        {
            try
            {
                // Desktop PCs without battery report "NotPresent" for the battery but are always on AC power.
                return PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;
            }
            catch
            {
                return false;
            }
        }
    }

    private static void RegisterPowerEvents()
    {
        if (_isPowerEventRegistered) return;
        try
        {
            PowerManager.PowerSupplyStatusChanged += PowerManager_PowerSupplyStatusChanged;
            _isPowerEventRegistered = true;
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("ScreenLockPrevention", $"Power events unavailable: {ex.Message}");
        }
    }

    private static void UnregisterPowerEvents()
    {
        if (!_isPowerEventRegistered) return;
        try { PowerManager.PowerSupplyStatusChanged -= PowerManager_PowerSupplyStatusChanged; }
        catch { }
        _isPowerEventRegistered = false;
    }

    private static void PowerManager_PowerSupplyStatusChanged(object? sender, object e) =>
        _dispatcher?.TryEnqueue(Apply);

    private static void SetPreventing(bool isPreventing)
    {
        if (_isPreventing == isPreventing) return;
        _isPreventing = isPreventing;
        try
        {
            SetThreadExecutionState(isPreventing
                ? ExecutionState.Continuous | ExecutionState.DisplayRequired | ExecutionState.SystemRequired
                : ExecutionState.Continuous);
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("ScreenLockPrevention", $"SetThreadExecutionState failed: {ex.Message}");
        }
    }

    public static void Shutdown()
    {
        UnregisterPowerEvents();
        SetPreventing(false);
    }
}
