using System.Text.Json;
using System.Text.Json.Serialization;
using Amperfy.Core.Player;

namespace Amperfy.Core.Common;

/// Diagnostic information attached to support requests (port of Common/LogData.swift).
/// The Windows app exports it as JSON file from the support settings.
public sealed class LogData
{
    public const int LatestEventsCount = 30;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public LogBasicInfo? BasicInfo { get; set; }
    public LogDeviceInfo? DeviceInfo { get; set; }
    public LogPlayerInfo? PlayerInfo { get; set; }
    public LogLibraryInfo? LibraryInfo { get; set; }
    public LogUserSettings? UserSettings { get; set; }
    public UserStatistics? UserStatistics { get; set; }
    public LogEventInfo? EventInfo { get; set; }

    /// Collects the information (main thread: reads the library storage).
    public static LogData CollectInformation(AmperfySettings settings, LibraryStorage library, IPlayerFacade? player,
        UserStatistics? userStatistics = null, LogDeviceInfo? deviceInfo = null)
    {
        var logData = new LogData
        {
            BasicInfo = new LogBasicInfo { AppName = AmperfyInfo.Name, AppVersion = AmperfyInfo.Version },
            DeviceInfo = deviceInfo ?? LogDeviceInfo.Current(),
            LibraryInfo = new LogLibraryInfo { Version = settings.App.LibrarySyncVersion.ToString() },
            UserStatistics = userStatistics,
        };
        foreach (var accountInfo in settings.Accounts.AllAccounts)
        {
            var account = library.GetAccount(accountInfo);
            logData.LibraryInfo.Accounts.Add(library.GetInfo(account));
        }

        if (player is not null)
        {
            logData.PlayerInfo = new LogPlayerInfo
            {
                IsPlaying = player.IsPlaying,
                RepeatType = player.RepeatMode.Description(),
                IsShuffle = player.IsShuffle,
                SongIndex = player.CurrentlyPlaying is not null ? 0 : -99,
                PlaylistItemCount = player.PrevQueueCount + player.NextQueueCount + 1,
            };
        }

        logData.UserSettings = new LogUserSettings
        {
            PlayerDisplayStyle = settings.User.PlayerDisplayStyle.Description(),
            IsOfflineMode = settings.User.IsOfflineMode,
            AppearanceMode = settings.User.AppearanceMode.ToString(),
            StreamingFormatUnmetered = settings.User.StreamingFormatWifiPreference.Description(),
            StreamingFormatMetered = settings.User.StreamingFormatCellularPreference.Description(),
            StreamingMaxBitrateUnmetered = settings.User.StreamingMaxBitrateWifiPreference.Description(),
            StreamingMaxBitrateMetered = settings.User.StreamingMaxBitrateCellularPreference.Description(),
            CacheTranscodingFormat = settings.User.CacheTranscodingFormatPreference.Description(),
            CacheLimit = settings.User.CacheLimit,
            IsReplayGainEnabled = settings.User.IsReplayGainEnabled,
            IsEqualizerEnabled = settings.User.IsEqualizerEnabled,
        };

        var eventLogs = library.GetAllLogEntries();
        var events = eventLogs.Take(LatestEventsCount).Select(e => new LogEventEntry
        {
            CreationDate = e.CreationDate,
            Type = e.Type.Description(),
            StatusCode = e.StatusCode,
            Message = e.Message,
        }).ToList();
        logData.EventInfo = new LogEventInfo
        {
            TotalEventCount = eventLogs.Count,
            AttachedEventCount = events.Count,
            Events = events,
        };
        return logData;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// Plain text representation of log entries (event log export).
    public static string FormatLogEntries(IEnumerable<LogEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            sb.Append(e.CreationDate.AsIso8601String()).Append(" [").Append(e.Type.Description());
            if (e.StatusCode > 1) sb.Append(" · Status code ").Append(e.StatusCode);
            sb.Append("] ").AppendLine(e.Message);
        }
        return sb.ToString();
    }
}

public sealed class LogBasicInfo
{
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string? AppName { get; set; }
    public string? AppVersion { get; set; }
}

public sealed class LogDeviceInfo
{
    public string? OsDescription { get; set; }
    public string? OsArchitecture { get; set; }
    public string? Runtime { get; set; }
    public string? TotalDiskCapacity { get; set; }
    public string? AvailableDiskCapacity { get; set; }

    /// Information about the machine; disk capacity of the drive of <paramref name="dataPath"/> (if given).
    public static LogDeviceInfo Current(string? dataPath = null)
    {
        var info = new LogDeviceInfo
        {
            OsDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            OsArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        };
        if (dataPath is not null)
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(dataPath));
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    info.TotalDiskCapacity = drive.TotalSize.AsByteString();
                    info.AvailableDiskCapacity = drive.AvailableFreeSpace.AsByteString();
                }
            }
            catch
            {
                // disk info is optional
            }
        }
        return info;
    }
}

public sealed class LogPlayerInfo
{
    public bool? IsPlaying { get; set; }
    public string? RepeatType { get; set; }
    public bool? IsShuffle { get; set; }
    public int? SongIndex { get; set; }
    public int? PlaylistItemCount { get; set; }
}

public sealed class LogLibraryInfo
{
    public string? Version { get; set; }
    public List<AccountLibraryInfo> Accounts { get; set; } = [];
}

public sealed class LogUserSettings
{
    public string? PlayerDisplayStyle { get; set; }
    public bool? IsOfflineMode { get; set; }
    public string? AppearanceMode { get; set; }
    public string? StreamingFormatUnmetered { get; set; }
    public string? StreamingFormatMetered { get; set; }
    public string? StreamingMaxBitrateUnmetered { get; set; }
    public string? StreamingMaxBitrateMetered { get; set; }
    public string? CacheTranscodingFormat { get; set; }
    public long? CacheLimit { get; set; }
    public bool? IsReplayGainEnabled { get; set; }
    public bool? IsEqualizerEnabled { get; set; }
}

public sealed class LogEventInfo
{
    public int TotalEventCount { get; set; }
    public int AttachedEventCount { get; set; }
    public List<LogEventEntry> Events { get; set; } = [];
}

public sealed class LogEventEntry
{
    public DateTime CreationDate { get; set; }
    public string? Type { get; set; }
    public int StatusCode { get; set; }
    public string? Message { get; set; }
}
