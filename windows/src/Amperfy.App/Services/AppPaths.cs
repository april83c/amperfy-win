namespace Amperfy.App.Services;

public static class AppPaths
{
    /// %LOCALAPPDATA%\Amperfy (can be overridden with AMPERFY_DATA_DIR for testing).
    public static string DataDirectory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("AMPERFY_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(overridden)) return overridden;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Amperfy");
        }
    }

    public static string CacheDirectory => Path.Combine(DataDirectory, "Cache");
    public static string LogFile => Path.Combine(DataDirectory, "logs", "amperfy.log");
}
