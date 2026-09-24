using Amperfy.App.Services;
using Amperfy.Core.Common;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace Amperfy.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        CrashLog.Install(this);
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        CrashLog.Write("OnLaunched");
        AppServices services;
        try
        {
            services = AppServices.Initialize();
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Initialization failed: {ex}");
            throw;
        }
        _window = new MainWindow();
        services.MainWindow = _window;
        _window.Closed += (_, _) =>
        {
            SystemIntegration.Shutdown();
            services.Shutdown();
        };
        _window.Activate();
        CrashLog.Write("Window activated");
        HandleActivation(AppInstance.GetCurrent().GetActivatedEventArgs());
    }

    /// Activation redirected from a second instance (called on a background thread).
    internal static void OnRedirectedActivation(AppActivationArguments args) => MainThread.Post(() =>
    {
        AppServices.Instance?.MainWindow?.BringToFront();
        HandleActivation(args);
    });

    private static void HandleActivation(AppActivationArguments args)
    {
        try
        {
            if (args.Kind == ExtendedActivationKind.AppNotification && args.Data is AppNotificationActivatedEventArgs toast)
            {
                SystemIntegration.HandleToastActivation(new Dictionary<string, string>(toast.Arguments));
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Activation handling failed: {ex}");
        }
    }
}
