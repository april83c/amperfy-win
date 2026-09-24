using System.Diagnostics;

namespace Amperfy.Core.Common;

public enum LogLevel { Debug, Info, Warning, Error }

/// Lightweight application log (replacement for os_log). Writes to Debug output and an optional file sink.
public static class AmperfyLog
{
    private static readonly object Lock = new();
    private static StreamWriter? _file;

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public static void SetFileSink(string? path)
    {
        lock (Lock)
        {
            _file?.Dispose();
            _file = null;
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 5_000_000) File.Delete(path);
                _file = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
            }
            catch { _file = null; }
        }
    }

    public static void Write(LogLevel level, string category, string message)
    {
        if (level < MinimumLevel) return;
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {category}: {message}";
        Debug.WriteLine(line);
        lock (Lock)
        {
            try { _file?.WriteLine(line); } catch { }
        }
    }

    public static void Debug_(string category, string message) => Write(LogLevel.Debug, category, message);
    public static void Info(string category, string message) => Write(LogLevel.Info, category, message);
    public static void Warning(string category, string message) => Write(LogLevel.Warning, category, message);
    public static void Error(string category, string message) => Write(LogLevel.Error, category, message);
}
