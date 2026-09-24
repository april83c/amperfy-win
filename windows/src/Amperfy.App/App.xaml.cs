using Amperfy.App.Services;
using Amperfy.Core.Common;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using IProtocolActivatedEventArgs = Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs;
using ILaunchActivatedEventArgs = Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs;

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
        SettingsBootstrap.Initialize(services);
        _window = new MainWindow();
        services.MainWindow = _window;
        Services.Player.PlayerUiService.Initialize(_window);
        _window.Closed += (_, _) =>
        {
            SettingsBootstrap.Shutdown();
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
                ToastNotificationService.HandleActivation(new Dictionary<string, string>(toast.Arguments));
            }
            else if (args.Kind == ExtendedActivationKind.Protocol && args.Data is IProtocolActivatedEventArgs protocol)
            {
                SystemIntegration.HandleCommandUrl(protocol.Uri);
            }
            else if (args.Kind == ExtendedActivationKind.Launch && args.Data is ILaunchActivatedEventArgs launch &&
                     SystemIntegration.FindCommandUrl(launch.Arguments) is { } url)
            {
                SystemIntegration.HandleCommandUrl(url);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Activation handling failed: {ex}");
        }
    }
}
