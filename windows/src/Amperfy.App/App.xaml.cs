using Microsoft.UI.Xaml;

namespace Amperfy.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        CrashLog.Install(this);
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        CrashLog.Write("OnLaunched");
        _window = new MainWindow();
        _window.Activate();
        CrashLog.Write("Window activated");
    }
}
