using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using WinRT;

namespace Amperfy.App;

/// Custom entry point (replaces the XAML generated Main): single instance — a second launch
/// (e.g. from a toast notification) redirects its activation to the running instance.
public static class Program
{
    private const string InstanceKey = "Amperfy.Main";

    [STAThread]
    public static int Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();
        if (IsRedirected()) return 0;
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    private static bool IsRedirected()
    {
        // Several instances are allowed when a separate data directory is used (tests / CI).
        if (Environment.GetEnvironmentVariable("AMPERFY_DATA_DIR") is { Length: > 0 }) return false;
        try
        {
            var mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (mainInstance.IsCurrent)
            {
                mainInstance.Activated += (_, e) => App.OnRedirectedActivation(e);
                return false;
            }
            var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            // Redirect synchronously: the STA thread must not be blocked by an await on itself.
            var done = new ManualResetEventSlim(false);
            Task.Run(async () =>
            {
                try { await mainInstance.RedirectActivationToAsync(activationArgs); }
                finally { done.Set(); }
            });
            done.Wait(TimeSpan.FromSeconds(10));
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Single instance check failed: {ex}");
            return false;
        }
    }
}
