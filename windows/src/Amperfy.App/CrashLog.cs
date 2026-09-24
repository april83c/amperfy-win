using Microsoft.UI.Xaml;

namespace Amperfy.App;

/// Writes startup/crash diagnostics to %LOCALAPPDATA%\Amperfy\logs\crash.log
/// (and to the path in AMPERFY_SMOKE_LOG, used by the CI smoke test).
internal static class CrashLog
{
    private static readonly object Lock = new();

    private static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Amperfy", "logs", "crash.log");

    public static void Install(Application app)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write($"AppDomain unhandled: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write($"Unobserved task exception: {e.Exception}");
            e.SetObserved();
        };
        app.UnhandledException += (_, e) =>
        {
            Write($"XAML unhandled: {e.Message}\n{e.Exception}");
        };
        app.DebugSettings.XamlResourceReferenceFailed += (_, e) => Write($"XAML resource reference failed: {e.Message}");
        app.DebugSettings.BindingFailed += (_, e) => Write($"Binding failed: {e.Message}");
        Write($"Start {Environment.ProcessPath} ({typeof(CrashLog).Assembly.GetName().Version})");
    }

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:O} {message}{Environment.NewLine}";
        lock (Lock)
        {
            foreach (var path in new[] { DefaultPath, Environment.GetEnvironmentVariable("AMPERFY_SMOKE_LOG") })
            {
                if (string.IsNullOrEmpty(path)) continue;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.AppendAllText(path, line);
                }
                catch { }
            }
        }
    }
}
