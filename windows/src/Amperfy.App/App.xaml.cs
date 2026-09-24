using Amperfy.App.Services;
using Microsoft.UI.Xaml;

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
        _window.Closed += (_, _) => services.Shutdown();
        _window.Activate();
        CrashLog.Write("Window activated");
    }
}
