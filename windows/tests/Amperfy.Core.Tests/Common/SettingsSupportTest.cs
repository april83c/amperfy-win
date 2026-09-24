using Amperfy.Core.Sync;
using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Common;

/// Core helpers used by the settings UI (cache sizes, cache limit, diagnostics export, notification switch).
public class SettingsSupportTest : IDisposable
{
    private readonly TestStorage _storage = new();

    public void Dispose() => _storage.Dispose();

    private static void WriteFile(string path, int size)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
    }

    [Fact]
    public void CalculateCacheSizeSumsAllCacheDirectories()
    {
        var fm = new CacheFileManager(Path.Combine(_storage.CacheDir, "sizes"));
        var info = _storage.Account.Info;
        WriteFile(fm.GetAbsolutePath(CacheFileManager.GetRelSongFilePath(info, "s1", "audio/mpeg")), 100);
        WriteFile(fm.GetAbsolutePath(CacheFileManager.GetRelSongFilePath(info, "s2", "audio/mpeg")), 50);
        WriteFile(fm.GetAbsolutePath(CacheFileManager.GetRelEpisodeFilePath(info, "e1", "audio/mpeg")), 30);
        WriteFile(fm.GetAbsolutePath(CacheFileManager.GetRelArtworkFilePath(info, "a1", "album")), 20);
        WriteFile(fm.GetAbsolutePath(CacheFileManager.GetRelEmbeddedArtworkFilePath(info, "s1", isSong: true)), 10);
        WriteFile(fm.GetAbsolutePath(CacheFileManager.GetRelLyricsFilePath(info, "s1")), 5);

        var size = fm.CalculateCacheSize(info);
        Assert.Equal(150, size.Songs);
        Assert.Equal(30, size.PodcastEpisodes);
        Assert.Equal(20, size.Artworks);
        Assert.Equal(10, size.EmbeddedArtworks);
        Assert.Equal(5, size.Lyrics);
        Assert.Equal(180, size.Playables);
        Assert.Equal(215, size.Total);

        var other = AccountInfo.Create("https://other.example", "other", BackendApiType.Subsonic);
        WriteFile(fm.GetAbsolutePath(CacheFileManager.GetRelSongFilePath(other, "x", "audio/mpeg")), 7);
        var complete = fm.CalculateCompleteCacheSize();
        Assert.Equal(222, complete.Total);
        Assert.Equal(157, complete.Songs);
    }

    [Fact]
    public void CalculateCacheSizeOfAccountWithoutCacheIsEmpty()
    {
        var fm = new CacheFileManager(Path.Combine(_storage.CacheDir, "empty"));
        var size = fm.CalculateCacheSize(AccountInfo.Create("https://none.example", "u", BackendApiType.Subsonic));
        Assert.Equal(CacheSizeInfo.Empty, size);
        Assert.Equal(0, size.Total);
        Assert.Equal(CacheSizeInfo.Empty, fm.CalculateCompleteCacheSize());
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(-5, 0, false)]
    [InlineData(500_000_000, 500, false)]
    [InlineData(1_000_000_000, 1, true)]
    [InlineData(2_500_000_000, 2.5, true)]
    public void CacheSizeLimitFromBytes(long bytes, double expectedValue, bool expectedGb)
    {
        var limit = CacheSizeLimit.FromBytes(bytes);
        Assert.Equal(expectedValue, limit.Value);
        Assert.Equal(expectedGb, limit.IsGigabyte);
        Assert.Equal(Math.Max(0, bytes), limit.Bytes);
    }

    [Fact]
    public void CacheSizeLimitToBytes()
    {
        Assert.Equal(0, new CacheSizeLimit(0, true).Bytes);
        Assert.True(new CacheSizeLimit(0, false).IsNoLimit);
        Assert.Equal(750_000_000, new CacheSizeLimit(750, false).Bytes);
        Assert.Equal(1_500_000_000, new CacheSizeLimit(1.5, true).Bytes);
        Assert.Equal("No Limit", new CacheSizeLimit(0, false).ToString());
        Assert.False(new CacheSizeLimit(2, true).IsNoLimit);
    }

    [Fact]
    public void LogDataCollectsLibraryAndEvents()
    {
        var settings = new AmperfySettings();
        var credentials = new LoginCredentials("https://test.example", "testuser", "pw", BackendApiType.Subsonic);
        settings.Accounts.Login(credentials);
        var library = _storage.Library;
        var account = library.GetAccount(settings.Accounts.Active!);
        var song = library.CreateSong(account);
        song.Id = "s1";
        for (var i = 0; i < LogData.LatestEventsCount + 5; i++)
        {
            var entry = library.CreateLogEntry();
            entry.Message = $"message {i}";
            entry.Type = LogEntryType.Error;
            entry.StatusCode = 4;
            entry.CreationDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i);
        }
        library.SaveContext();
        settings.User.IsOfflineMode = true;

        var logData = LogData.CollectInformation(settings, library, player: null, new UserStatistics());

        Assert.Equal(AmperfyInfo.Name, logData.BasicInfo!.AppName);
        var accountInfo = Assert.Single(logData.LibraryInfo!.Accounts);
        Assert.Equal(1, accountInfo.SongCount);
        Assert.Null(logData.PlayerInfo);
        Assert.True(logData.UserSettings!.IsOfflineMode);
        Assert.Equal(LogData.LatestEventsCount + 5, logData.EventInfo!.TotalEventCount);
        Assert.Equal(LogData.LatestEventsCount, logData.EventInfo.AttachedEventCount);
        // newest first
        Assert.Equal($"message {LogData.LatestEventsCount + 4}", logData.EventInfo.Events[0].Message);

        var json = logData.ToJson();
        Assert.Contains("\"TotalEventCount\": 35", json);
        Assert.Contains("\"IsOfflineMode\": true", json);
        Assert.DoesNotContain("\"PlayerInfo\"", json);
    }

    [Fact]
    public void FormatLogEntriesWritesOneLinePerEntry()
    {
        var entries = new[]
        {
            new LogEntry { CreationDate = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc), Type = LogEntryType.Error, StatusCode = 5, Message = "Connection lost" },
            new LogEntry { CreationDate = new DateTime(2024, 5, 6, 7, 8, 10, DateTimeKind.Utc), Type = LogEntryType.Info, StatusCode = 0, Message = "Hello" },
        };
        var text = LogData.FormatLogEntries(entries);
        var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal($"2024-05-06T07:08:09Z [{LogEntryType.Error.Description()} · Status code 5] Connection lost", lines[0]);
        Assert.Equal($"2024-05-06T07:08:10Z [{LogEntryType.Info.Description()}] Hello", lines[1]);
    }

    [Fact]
    public void PodcastNotificationsCanBeDisabled()
    {
        var settings = new AmperfySettings();
        var manager = new LocalNotificationManager(settings);
        var requests = new List<LocalNotificationRequest>();
        manager.NotificationRequested += requests.Add;
        var podcast = _storage.Library.CreatePodcast(_storage.Account);
        podcast.Id = "p1";
        podcast.Title = "Podcast";
        var episode = _storage.Library.CreatePodcastEpisode(_storage.Account);
        episode.Id = "e1";
        episode.Title = "Episode";
        episode.Podcast = podcast;
        _storage.Library.SaveContext();

        Assert.True(settings.User.IsPodcastNotificationsEnabled);
        manager.Notify(episode);
        Assert.Single(requests);

        settings.User.IsPodcastNotificationsEnabled = false;
        manager.Notify(episode);
        Assert.Single(requests);

        // debug notifications are not affected
        manager.NotifyDebug("t", "b");
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public void PodcastNotificationSettingIsPersisted()
    {
        Directory.CreateDirectory(_storage.CacheDir);
        var path = Path.Combine(_storage.CacheDir, "settings.json");
        using (var settings = new AmperfySettings(path))
        {
            settings.User.IsPodcastNotificationsEnabled = false;
        }
        using var reloaded = new AmperfySettings(path);
        Assert.False(reloaded.User.IsPodcastNotificationsEnabled);
    }
}
