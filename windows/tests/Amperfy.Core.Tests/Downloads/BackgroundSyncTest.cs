using Amperfy.Core.Downloads;
using Amperfy.Core.Sync;

namespace Amperfy.Core.Tests.Downloads;

/// Records download requests (Swift: MOCK_SongDownloader).
public sealed class RecordingDownloadManager : IDownloadManageable
{
    public List<IDownloadable> Downloadables { get; } = [];
    public void Download(IDownloadable obj) => Downloadables.Add(obj);
    public void Download(IEnumerable<IDownloadable> objects) => Downloadables.AddRange(objects);
    public void RemoveFinishedDownload(IDownloadable obj) { }
    public void RemoveFinishedDownload(IEnumerable<IDownloadable> objects) { }
    public void ClearFinishedDownloads() { }
    public void ResetFailedDownloads() { }
    public void CancelDownloads() { }
    public void Start() { }
    public void Stop() { }
}

[Collection(DownloadTestCollection.Name)]
public class BackgroundSyncTest : IDisposable
{
    private readonly DownloadTestContext _c = new();
    private readonly ILibrarySyncer _syncer;
    private readonly InterfaceFake<ILibrarySyncer> _syncerFake;
    private readonly RecordingDownloadManager _downloads = new();
    private int _newestCounter;

    public BackgroundSyncTest()
    {
        (_syncer, _syncerFake) = InterfaceFake<ILibrarySyncer>.Create();
    }

    public void Dispose() => _c.Dispose();

    private Album CreateNewestAlbum(string id, int songCount = 2)
    {
        var album = _c.Library.CreateAlbum(_c.Account);
        album.Id = id;
        album.Name = id;
        album.UpdateIsNewestInfo(++_newestCounter);
        for (var i = 0; i < songCount; i++)
        {
            var song = _c.Library.CreateSong(_c.Account);
            song.Id = $"{id}-s{i}";
            song.Album = album;
        }
        _c.Library.SaveContext();
        return album;
    }

    private PodcastEpisode CreateEpisode(string id, DateTime published)
    {
        var episode = _c.Library.CreatePodcastEpisode(_c.Account);
        episode.Id = id;
        episode.PublishDate = published;
        episode.PodcastStatus = PodcastEpisodeRemoteStatus.Completed;
        _c.Library.SaveContext();
        return episode;
    }

    private void EnableAutoDownload(bool songs, bool episodes) =>
        _c.Settings.Accounts.UpdateSetting(_c.Account.Info, s =>
        {
            s.IsAutoDownloadLatestSongsActive = songs;
            s.IsAutoDownloadLatestPodcastEpisodesActive = episodes;
        });

    [Fact]
    public void NewestAlbumsAreSyncedAndDownloaded()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            EnableAutoDownload(songs: true, episodes: false);
            CreateNewestAlbum("old");
            var syncedAlbums = new List<Album>();
            _syncerFake.Handlers[nameof(ILibrarySyncer.SyncNewestAlbumsAsync)] = _ =>
            {
                CreateNewestAlbum("new1");
                CreateNewestAlbum("new2");
                return Task.CompletedTask;
            };
            _syncerFake.Handlers[nameof(ILibrarySyncer.SyncAsync)] = args =>
            {
                if (args[0] is Album album) syncedAlbums.Add(album);
                return Task.CompletedTask;
            };
            var syncer = new AutoDownloadLibrarySyncer(_c.Library, _c.Settings, _c.Account, _syncer, _downloads);

            await syncer.SyncNewestLibraryElementsAsync();

            Assert.Equal(["new1", "new2"], syncedAlbums.Select(a => a.Id).Order());
            Assert.Equal(4, _downloads.Downloadables.Count);
            Assert.All(_downloads.Downloadables, d => Assert.StartsWith("new", ((Song)d).Id));
        });
    }

    [Fact]
    public void NoAutoDownloadOnInitialSyncOrWhenDisabled()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            EnableAutoDownload(songs: true, episodes: false);
            _syncerFake.Handlers[nameof(ILibrarySyncer.SyncNewestAlbumsAsync)] = _ =>
            {
                CreateNewestAlbum($"a{_newestCounter}");
                return Task.CompletedTask;
            };
            var syncer = new AutoDownloadLibrarySyncer(_c.Library, _c.Settings, _c.Account, _syncer, _downloads);
            await syncer.SyncNewestLibraryElementsAsync(); // initial: no old newest albums
            Assert.Empty(_downloads.Downloadables);

            EnableAutoDownload(songs: false, episodes: false);
            await syncer.SyncNewestLibraryElementsAsync();
            Assert.Empty(_downloads.Downloadables);
        });
    }

    [Fact]
    public void NewPodcastEpisodesAreDownloadedAndNotified()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            EnableAutoDownload(songs: false, episodes: true);
            var podcast = _c.Library.CreatePodcast(_c.Account);
            podcast.Id = "p1";
            podcast.Title = "Podcast";
            _c.Library.SaveContext();
            CreateEpisode("e-old", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Podcast = podcast;
            _syncerFake.Handlers[nameof(ILibrarySyncer.SyncNewestPodcastEpisodesAsync)] = _ =>
            {
                CreateEpisode("e-new", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Podcast = podcast;
                _c.Library.SaveContext();
                return Task.CompletedTask;
            };
            var notifications = new LocalNotificationManager(_c.Settings);
            var requests = new List<LocalNotificationRequest>();
            notifications.NotificationRequested += requests.Add;
            var syncer = new BackgroundFetchTriggeredSyncer(_c.Library, _c.Settings, _c.Account, _syncer, notifications, _downloads);

            await syncer.SyncAndNotifyPodcastEpisodesAsync();

            var episode = (PodcastEpisode)Assert.Single(_downloads.Downloadables);
            Assert.Equal("e-new", episode.Id);
            var request = Assert.Single(requests);
            Assert.Equal("Podcast", request.Title);
            Assert.Equal(episode.Title, request.Body);
            Assert.Equal($"account-{_c.Account.Ident}-podcast-p1-episode-e-new", request.Identifier);
            Assert.Equal("e-new", request.UserInfo[NotificationUserInfo.Id]);
            Assert.Equal(nameof(NotificationContentType.PodcastEpisode), request.UserInfo[NotificationUserInfo.Type]);
        });
    }

    [Fact]
    public void PeriodicFetcherRunsAllSyncersAndReportsErrors()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var calls = 0;
            _syncerFake.Handlers[nameof(ILibrarySyncer.SyncNewestPodcastEpisodesAsync)] = _ =>
            {
                calls++;
                return calls == 1 ? Task.FromException(new InvalidOperationException("server down")) : Task.CompletedTask;
            };
            var notifications = new LocalNotificationManager(_c.Settings);
            var syncer = new BackgroundFetchTriggeredSyncer(_c.Library, _c.Settings, _c.Account, _syncer, notifications, _downloads);
            using var fetcher = new PeriodicBackgroundFetcher(() => [syncer, syncer], _c.EventLogger, _c.Settings, _c.Network, TimeSpan.FromHours(1));
            bool? performed = null;
            fetcher.FetchPerformed += s => performed = s;

            var success = await fetcher.PerformFetchAsync();
            await Task.Yield();

            Assert.False(success);
            Assert.False(performed);
            Assert.Equal(2, calls);
            Assert.Contains(_c.Library.GetAllLogEntries(), l => l.Message.Contains("server down"));

            _c.Network.IsConnectedToNetwork = false;
            Assert.False(await fetcher.PerformFetchAsync());
            Assert.Equal(2, calls);

            fetcher.Start();
            Assert.True(fetcher.IsStarted);
            fetcher.Stop();
            Assert.False(fetcher.IsStarted);
        });
    }

    [Fact]
    public void BackgroundLibrarySyncerSyncsAlbumsWithoutSongs()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var album1 = _c.Library.CreateAlbum(_c.Account);
            album1.Id = "a1";
            var album2 = _c.Library.CreateAlbum(_c.Account);
            album2.Id = "a2";
            var synced = _c.Library.CreateAlbum(_c.Account);
            synced.Id = "a3";
            synced.IsSongsMetaDataSynced = true;
            _c.Library.SaveContext();
            var syncedAlbums = new List<string>();
            _syncerFake.Handlers[nameof(ILibrarySyncer.SyncAsync)] = args =>
            {
                var album = (Album)args[0]!;
                syncedAlbums.Add(album.Id);
                if (album.Id == "a2") return Task.FromException(new InvalidOperationException("fail"));
                album.IsSongsMetaDataSynced = true;
                return Task.CompletedTask;
            };
            var auto = new AutoDownloadLibrarySyncer(_c.Library, _c.Settings, _c.Account, _syncer, _downloads);
            var background = new BackgroundLibrarySyncer(_c.Account, _c.Library, _c.Settings, _c.Network, _syncer, auto, _c.EventLogger);

            background.Start();
            Assert.True(background.IsActive);
            await background.RunningTask;

            Assert.False(background.IsActive);
            Assert.Contains(nameof(ILibrarySyncer.SyncNewestAlbumsAsync), _syncerFake.Calls);
            Assert.Equal(["a1", "a2"], syncedAlbums.Order());
            // failed albums are marked as synced to avoid endless retries
            Assert.True(album2.IsSongsMetaDataSynced);
            Assert.Empty(_c.Library.GetAlbumsWithoutSyncedSongs(_c.Account));
        });
    }

    [Fact]
    public void BackgroundLibrarySyncerDoesNothingOffline()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var album = _c.Library.CreateAlbum(_c.Account);
            album.Id = "a1";
            _c.Library.SaveContext();
            _c.Settings.User.IsOfflineMode = true;
            var auto = new AutoDownloadLibrarySyncer(_c.Library, _c.Settings, _c.Account, _syncer, _downloads);
            var background = new BackgroundLibrarySyncer(_c.Account, _c.Library, _c.Settings, _c.Network, _syncer, auto, _c.EventLogger);
            background.Start();
            await background.RunningTask;
            Assert.Empty(_syncerFake.Calls);
        });
    }

    [Fact]
    public void LibraryUpdaterBumpsVersionAndCleansObsoleteAccounts()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var settings = _c.Settings;
            settings.App.LibrarySyncVersion = LibrarySyncVersion.V18;
            var obsolete = _c.Library.GetAccount(AccountInfo.Create("https://old.example", "old", BackendApiType.Ampache));
            var obsoleteSong = _c.Library.CreateSong(obsolete);
            obsoleteSong.Id = "x";
            _c.Library.SaveContext();
            settings.Accounts.Login(new LoginCredentials("https://test.example", "testuser", "pw", BackendApiType.Subsonic));

            var updater = new LibraryUpdater(_c.Library, settings);
            Assert.True(updater.IsVisualUpdateNeeded);
            updater.PerformSmallBlockingLibraryUpdatesIfNeeded();
            Assert.Equal(LibrarySyncVersion.V20, settings.App.LibrarySyncVersion);
            var ticks = 0;
            await updater.PerformLibraryUpdateWithStatusAsync(new UpdaterCallbacks(() => ticks++));
            Assert.Equal(SettingEnumerationExtensions.NewestLibrarySyncVersion, settings.App.LibrarySyncVersion);
            Assert.Equal(1, ticks);
            Assert.False(updater.IsVisualUpdateNeeded);

            updater.PerformAccountCleanUpIfNecessary();
            Assert.Equal(_c.Account.Ident, Assert.Single(_c.Library.GetAllAccounts()).Ident);
            Assert.DoesNotContain(_c.Library.GetAllSongs(), s => s.Id == "x");
        });
    }

    private sealed class UpdaterCallbacks(Action tick) : ILibraryUpdaterCallbacks
    {
        public void StartOperation(string name, int totalCount) { }
        public void TickOperation() => tick();
    }
}
