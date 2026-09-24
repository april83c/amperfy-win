using System.Net;
using Amperfy.Core.Downloads;

namespace Amperfy.Core.Tests.Downloads;

[Collection(DownloadTestCollection.Name)]
public class DownloadManagerTest : IDisposable
{
    private readonly DownloadTestContext _c = new();

    public void Dispose() => _c.Dispose();

    private static Model.Download DownloadOf(AbstractPlayable p) => p.Download!;

    [Fact]
    public void SuccessfulDownloadMovesFileIntoCacheAndMarksEntityCached()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var content = FakeHttpMessageHandler.Bytes(1234);
            _c.Http.Handler = (_, _) => Task.FromResult(FakeHttpMessageHandler.Ok(content, "audio/mpeg"));
            var song = _c.CreateSong("s1");
            using var manager = _c.CreateManager();
            var changedCount = 0;
            manager.DownloadsChanged += (_, _) => changedCount++;

            manager.Download(song);
            await manager.WhenIdleAsync();

            Assert.True(song.IsCached);
            Assert.Equal("audio/mpeg", song.ContentTypeTranscoded);
            Assert.Equal(CacheFileManager.GetRelSongFilePath(_c.Account.Info, "s1", "audio/mpeg"), song.RelFilePath);
            Assert.EndsWith(".mp3", song.RelFilePath);
            Assert.Equal(content, File.ReadAllBytes(_c.FileManager.GetAbsolutePath(song.RelFilePath!)));
            Assert.Equal(1234, _c.FileManager.CompletePlayableCacheSize);

            var download = DownloadOf(song);
            Assert.True(download.IsFinishedSuccessfully);
            Assert.False(download.IsDownloading);
            Assert.Null(download.Error);
            Assert.Equal(1f, download.Progress);
            Assert.Equal(song.UniqueId(), download.Id);
            Assert.Equal(new Uri($"https://download.test/{song.UniqueId()}"), Assert.Single(_c.Http.RequestedUrls));
            Assert.Equal(song.UniqueId(), Assert.Single(_c.FinishedNotifications).Id);
            Assert.True(changedCount >= 2);
            Assert.Single(manager.GetDownloads());
        });
    }

    [Fact]
    public void HttpHeadersOfTheDelegateAreSent()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            _c.Delegate.HttpHeaders = new Dictionary<string, string> { ["X-Test"] = "abc" };
            var song = _c.CreateSong("s1");
            using var manager = _c.CreateManager();
            manager.Download(song);
            await manager.WhenIdleAsync();
            var request = Assert.Single(_c.Http.Requests);
            Assert.Equal("abc", request.Headers.GetValues("X-Test").Single());
        });
    }

    [Fact]
    public void AlreadyCachedElementIsNotDownloaded()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var song = _c.CreateSong("s1");
            song.RelFilePath = "some/path.mp3";
            _c.Library.SaveContext();
            using var manager = _c.CreateManager();
            manager.Download(song);
            await manager.WhenIdleAsync();
            Assert.Empty(_c.Http.RequestedUrls);
            Assert.Null(song.Download);
        });
    }

    [Fact]
    public void TransferFailureMarksDownloadAsFailed()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            _c.Http.Handler = (_, _) => throw new HttpRequestException("connection refused");
            var song = _c.CreateSong("s1");
            using var manager = _c.CreateManager();

            manager.Download(song);
            await manager.WhenIdleAsync();
            await Task.Yield(); // EventLogger saves on the next main thread cycle

            Assert.False(song.IsCached);
            var download = DownloadOf(song);
            Assert.Equal(DownloadError.FetchFailed, download.Error);
            Assert.NotNull(download.ErrorDate);
            Assert.NotNull(download.FinishDate);
            Assert.False(download.IsFinishedSuccessfully);
            Assert.Same(song, Assert.Single(_c.Delegate.Failed));
            Assert.Empty(_c.FinishedNotifications);
            var log = Assert.Single(_c.Library.GetAllLogEntries());
            Assert.Contains("\"Fetch Failed\" occurred while downloading object \"Unknown Artist - Song s1\"", log.Message);
        });
    }

    [Fact]
    public void HttpErrorStatusMarksDownloadAsFailed()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            _c.Http.Handler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html>not found</html>") });
            var song = _c.CreateSong("s1");
            using var manager = _c.CreateManager();
            manager.Download(song);
            await manager.WhenIdleAsync();
            Assert.False(song.IsCached);
            Assert.Equal(DownloadError.FetchFailed, DownloadOf(song).Error);
            Assert.Empty(_c.Delegate.Completed);
        });
    }

    [Fact]
    public void PrepareErrorIsStoredAsDownloadError()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            _c.Delegate.PrepareException = new DownloadException(DownloadError.NoConnectivity);
            var song = _c.CreateSong("s1");
            using var manager = _c.CreateManager();
            manager.Download(song);
            await manager.WhenIdleAsync();
            Assert.Equal(DownloadError.NoConnectivity, DownloadOf(song).Error);
            Assert.Empty(_c.Http.RequestedUrls);
        });
    }

    [Fact]
    public void AlreadyDownloadedErrorIsNotReported()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            _c.Delegate.PrepareException = new DownloadException(DownloadError.AlreadyDownloaded);
            var song = _c.CreateSong("s1");
            using var manager = _c.CreateManager();
            manager.Download(song);
            await manager.WhenIdleAsync();
            await Task.Yield();
            Assert.Equal(DownloadError.AlreadyDownloaded, DownloadOf(song).Error);
            Assert.Empty(_c.Library.GetAllLogEntries());
        });
    }

    [Fact]
    public void EmptyFileMarksDownloadAsFailed()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            _c.Http.Handler = (_, _) => Task.FromResult(FakeHttpMessageHandler.Ok([]));
            var song = _c.CreateSong("s1");
            using var manager = _c.CreateManager();
            manager.Download(song);
            await manager.WhenIdleAsync();
            Assert.Equal(DownloadError.EmptyFile, DownloadOf(song).Error);
            Assert.False(song.IsCached);
        });
    }

    [Fact]
    public void InvalidDataMarksDownloadAsApiError()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            _c.Delegate.Validator = (path, url) => new ResponseError(ResponseErrorType.Api, 70, "Song not found", new CleansedUrl(url!.ToString()));
            var song = _c.CreateSong("s1");
            using var manager = _c.CreateManager();
            manager.Download(song);
            await manager.WhenIdleAsync();
            await Task.Yield();
            Assert.Equal(DownloadError.ApiErrorResponse, DownloadOf(song).Error);
            Assert.False(song.IsCached);
            Assert.Empty(_c.Delegate.Completed);
            Assert.Same(song, Assert.Single(_c.Delegate.Failed));
            // only the API error itself is logged (no additional "Download Error")
            var log = Assert.Single(_c.Library.GetAllLogEntries());
            Assert.Equal(70, log.StatusCode);
        });
    }

    [Fact]
    public void CancelDownloadsStopsRunningTransferAndMarksCanceled()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _c.Http.Handler = async (_, token) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("not reached");
            };
            var song1 = _c.CreateSong("s1");
            var song2 = _c.CreateSong("s2");
            var song3 = _c.CreateSong("s3");
            _c.Delegate.ParallelDownloadsCount = 1;
            using var manager = _c.CreateManager();

            manager.Download([song1, song2, song3]);
            await started.Task;
            Assert.True(DownloadOf(song1).IsDownloading);
            Assert.Equal(1, manager.ActiveDownloadCount);
            Assert.Equal(2, manager.QueuedDownloadCount);

            manager.CancelDownloads();
            await manager.WhenIdleAsync();
            await Task.Yield();

            foreach (var song in new[] { song1, song2, song3 })
            {
                Assert.False(song.IsCached);
                Assert.True(DownloadOf(song).IsCanceled);
            }
            Assert.Equal(1, _c.Http.RequestCount);
            Assert.Empty(_c.Delegate.Failed);
            Assert.Empty(_c.Library.GetAllLogEntries());
            Assert.Equal(0, manager.ActiveDownloadCount);
        });
    }

    [Fact]
    public void ParallelDownloadsAreLimited()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var startedCount = 0;
            var twoStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _c.Http.Handler = async (_, _) =>
            {
                if (Interlocked.Increment(ref startedCount) == 2) twoStarted.TrySetResult();
                await gate.Task;
                return FakeHttpMessageHandler.Ok(FakeHttpMessageHandler.Bytes(10));
            };
            _c.Delegate.ParallelDownloadsCount = 2;
            var songs = Enumerable.Range(1, 5).Select(i => _c.CreateSong($"s{i}")).ToList();
            using var manager = _c.CreateManager();

            manager.Download(songs);
            await twoStarted.Task;
            await Task.Delay(50);
            Assert.Equal(2, manager.ActiveDownloadCount);
            Assert.Equal(3, manager.QueuedDownloadCount);
            Assert.Equal(2, Volatile.Read(ref startedCount));

            gate.SetResult();
            await manager.WhenIdleAsync();

            Assert.Equal(2, _c.Http.MaxConcurrent);
            Assert.Equal(5, _c.Http.RequestCount);
            Assert.All(songs, s => Assert.True(s.IsCached));
            Assert.All(songs, s => Assert.True(DownloadOf(s).IsFinishedSuccessfully));
        });
    }

    [Fact]
    public void DownloadsFollowTheRequestOrder()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            _c.Delegate.ParallelDownloadsCount = 1;
            var songs = Enumerable.Range(1, 4).Select(i => _c.CreateSong($"s{i}")).ToList();
            using var manager = _c.CreateManager();
            manager.Download(songs);
            await manager.WhenIdleAsync();
            Assert.Equal(songs.Select(s => new Uri($"https://download.test/{s.UniqueId()}")), _c.Http.RequestedUrls);
        });
    }

    [Fact]
    public void CacheLimitCancelsRemainingDownloadsWhenExceeded()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            _c.Settings.User.CacheLimit = 150;
            _c.Http.Handler = (_, _) => Task.FromResult(FakeHttpMessageHandler.Ok(FakeHttpMessageHandler.Bytes(100)));
            _c.Delegate.ParallelDownloadsCount = 1;
            var songs = Enumerable.Range(1, 4).Select(i => _c.CreateSong($"s{i}")).ToList();
            using var manager = _c.CreateManager();

            manager.Download(songs);
            await manager.WhenIdleAsync();

            // 100 bytes: below the limit, 200 bytes: limit exceeded -> remaining downloads are canceled
            Assert.True(songs[0].IsCached);
            Assert.True(songs[1].IsCached);
            Assert.False(songs[2].IsCached);
            Assert.False(songs[3].IsCached);
            Assert.True(DownloadOf(songs[2]).IsCanceled);
            Assert.True(DownloadOf(songs[3]).IsCanceled);
            Assert.Equal(2, _c.Http.RequestCount);
            Assert.True(manager.StorageExceedsCacheLimit());
            Assert.False(manager.IsAllowedToTriggerDownload);

            // new requests are ignored while the cache limit is exceeded
            var song5 = _c.CreateSong("s5");
            manager.Download(song5);
            await manager.WhenIdleAsync();
            Assert.Null(song5.Download);
        });
    }

    [Fact]
    public void CacheLimitIsIgnoredWithoutLimitFlag()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            _c.Settings.User.CacheLimit = 10;
            var songs = Enumerable.Range(1, 3).Select(i => _c.CreateSong($"s{i}")).ToList();
            using var manager = _c.CreateManager(limitCacheSize: false);
            manager.Download(songs);
            await manager.WhenIdleAsync();
            Assert.All(songs, s => Assert.True(s.IsCached));
        });
    }

    [Fact]
    public void RequestsAreQueuedUntilStartAndWhileOffline()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var song = _c.CreateSong("s1");
            using var manager = _c.CreateManager(start: false);
            manager.Download(song);
            await manager.WhenIdleAsync();
            Assert.NotNull(song.Download);
            Assert.False(song.IsCached);
            Assert.Empty(_c.Http.RequestedUrls);

            _c.Network.IsConnectedToNetwork = false;
            manager.Start();
            await manager.WhenIdleAsync();
            Assert.Empty(_c.Http.RequestedUrls);

            _c.Network.IsConnectedToNetwork = true;
            _c.Network.RaiseChanged();
            await manager.WhenIdleAsync();
            Assert.True(song.IsCached);
            Assert.True(DownloadOf(song).IsFinishedSuccessfully);
        });
    }

    [Fact]
    public void OfflineModeSuspendsDownloadsAndResumesThem()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var block = true;
            _c.Http.Handler = async (_, token) =>
            {
                if (block)
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }
                return FakeHttpMessageHandler.Ok(FakeHttpMessageHandler.Bytes(10));
            };
            var song = _c.CreateSong("s1");
            using var manager = _c.CreateManager();
            manager.Download(song);
            await started.Task;

            _c.Settings.User.IsOfflineMode = true;
            _c.Notifications.Post(AmperfyNotification.OfflineModeChanged);
            await manager.WhenIdleAsync();
            var download = DownloadOf(song);
            Assert.Null(download.Error);
            Assert.Null(download.StartDate);
            Assert.Null(download.FinishDate);
            Assert.False(song.IsCached);

            block = false;
            _c.Settings.User.IsOfflineMode = false;
            _c.Notifications.Post(AmperfyNotification.OfflineModeChanged);
            await manager.WhenIdleAsync();
            Assert.True(song.IsCached);
            Assert.True(download.IsFinishedSuccessfully);
        });
    }

    [Fact]
    public void ResetFailedDownloadsRetriesThem()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var fail = true;
            _c.Http.Handler = (_, _) => fail
                ? throw new HttpRequestException("fail")
                : Task.FromResult(FakeHttpMessageHandler.Ok(FakeHttpMessageHandler.Bytes(10)));
            var song = _c.CreateSong("s1");
            using var manager = _c.CreateManager(isFailWithPopupError: false);
            manager.Download(song);
            await manager.WhenIdleAsync();
            Assert.Equal(DownloadError.FetchFailed, DownloadOf(song).Error);

            fail = false;
            manager.ResetFailedDownloads();
            await manager.WhenIdleAsync();
            Assert.True(song.IsCached);
            Assert.True(DownloadOf(song).IsFinishedSuccessfully);
        });
    }

    [Fact]
    public void DownloadingAFailedElementAgainResetsTheDownload()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var fail = true;
            _c.Http.Handler = (_, _) => fail
                ? throw new HttpRequestException("fail")
                : Task.FromResult(FakeHttpMessageHandler.Ok(FakeHttpMessageHandler.Bytes(10)));
            var song = _c.CreateSong("s1");
            using var manager = _c.CreateManager();
            manager.Download(song);
            await manager.WhenIdleAsync();
            var download = DownloadOf(song);
            Assert.NotNull(download.Error);

            fail = false;
            manager.Download(song);
            await manager.WhenIdleAsync();
            Assert.Same(download, song.Download);
            Assert.True(download.IsFinishedSuccessfully);
            Assert.Single(_c.Library.GetDownloads(_c.Account, DownloadableType.Playable));
        });
    }

    [Fact]
    public void ClearAndRemoveFinishedDownloads()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            _c.Http.Handler = (req, _) => req.RequestUri!.ToString().Contains(_failId)
                ? throw new HttpRequestException("fail")
                : Task.FromResult(FakeHttpMessageHandler.Ok(FakeHttpMessageHandler.Bytes(10)));
            var ok1 = _c.CreateSong("ok1");
            var ok2 = _c.CreateSong("ok2");
            var failed = _c.CreateSong("failed");
            _failId = failed.UniqueId();
            var pending = _c.CreateSong("pending");
            using var manager = _c.CreateManager();
            manager.Download([ok1, ok2, failed]);
            await manager.WhenIdleAsync();
            manager.Stop();
            // a pending request (manager stopped -> not started)
            var stopped = _c.CreateManager(start: false);
            stopped.Download(pending);
            Assert.Equal(4, manager.GetDownloads().Count);

            manager.RemoveFinishedDownload(ok1);
            Assert.Null(ok1.Download);
            manager.RemoveFinishedDownload(pending); // not finished -> kept
            Assert.NotNull(pending.Download);

            manager.ClearFinishedDownloads();
            var remaining = Assert.Single(manager.GetDownloads());
            Assert.Same(pending, remaining.Element);
            Assert.True(ok2.IsCached); // cache itself is not touched
            stopped.Dispose();
        });
    }

    private string _failId = "";

    [Fact]
    public void StopCancelsPendingRequests()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var song = _c.CreateSong("s1");
            using var manager = _c.CreateManager(start: false);
            manager.Download(song);
            manager.Stop();
            await manager.WhenIdleAsync();
            Assert.True(DownloadOf(song).IsCanceled);
        });
    }

    [Fact]
    public void ProgressIsReportedOnTheMainThread()
    {
        var previous = DownloadManager.ProgressUpdateInterval;
        DownloadManager.ProgressUpdateInterval = TimeSpan.Zero;
        try
        {
            SingleThreadSynchronizationContext.Run(async () =>
            {
                var mainThreadId = Environment.CurrentManagedThreadId;
                var content = FakeHttpMessageHandler.Bytes(1_000_000);
                _c.Http.Handler = (_, _) => Task.FromResult(FakeHttpMessageHandler.Ok(content));
                var song = _c.CreateSong("s1");
                using var manager = _c.CreateManager();
                var events = new List<(float Progress, int ThreadId, long? Total)>();
                manager.DownloadProgressChanged += (_, e) => events.Add((e.Progress, Environment.CurrentManagedThreadId, e.TotalBytes));

                manager.Download(song);
                await manager.WhenIdleAsync();

                Assert.NotEmpty(events);
                Assert.All(events, e => Assert.Equal(mainThreadId, e.ThreadId));
                Assert.All(events, e => Assert.Equal(1_000_000, e.Total));
                Assert.Contains(events, e => e.Progress > 0f);
                Assert.Equal(1f, DownloadOf(song).Progress);
                Assert.Equal(1_000_000L.AsByteString(), DownloadOf(song).TotalSize);
            });
        }
        finally
        {
            DownloadManager.ProgressUpdateInterval = previous;
        }
    }

    [Fact]
    public void ArtworkDownloadValidationFollowsTheArtworkSetting()
    {
        SingleThreadSynchronizationContext.Run(() =>
        {
            var info = _c.Account.Info;
            var notChecked = _c.Library.CreateArtwork(_c.Account);
            notChecked.Id = "a1";
            notChecked.Status = ImageStatus.NotChecked;
            var custom = _c.Library.CreateArtwork(_c.Account);
            custom.Id = "a2";
            custom.Status = ImageStatus.CustomImage;
            _c.Library.SaveContext();
            var validation = DownloadManagerFactory.CreateArtworkValidation(_c.Settings, info);

            _c.Settings.Accounts.UpdateSetting(info, s => s.ArtworkDownloadSetting = ArtworkDownloadSetting.OnlyOnce);
            Assert.Equal(new IDownloadable[] { notChecked }, validation([notChecked, custom]));
            _c.Settings.Accounts.UpdateSetting(info, s => s.ArtworkDownloadSetting = ArtworkDownloadSetting.UpdateOncePerSession);
            Assert.Equal(new IDownloadable[] { notChecked, custom }, validation([notChecked, custom]));
            _c.Settings.Accounts.UpdateSetting(info, s => s.ArtworkDownloadSetting = ArtworkDownloadSetting.Never);
            Assert.Empty(validation([notChecked, custom]));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void RequestManagerCreatesOneDownloadPerElement()
    {
        SingleThreadSynchronizationContext.Run(() =>
        {
            var song = _c.CreateSong("s1");
            var artwork = _c.Library.CreateArtwork(_c.Account);
            artwork.Id = "a1";
            _c.Library.SaveContext();
            var playables = new DownloadRequestManager(_c.Account, _c.Library, DownloadableType.Playable);
            var artworks = new DownloadRequestManager(_c.Account, _c.Library, DownloadableType.Artwork);

            Assert.NotNull(playables.Add(song));
            // existing playable download which is not cached -> reset and requested again
            Assert.NotNull(playables.Add(song));
            Assert.Single(playables.Add([song, song]));
            Assert.Single(_c.Library.GetDownloads(_c.Account, DownloadableType.Playable));

            Assert.NotNull(artworks.Add(artwork));
            // existing artwork downloads are ignored
            Assert.Null(artworks.Add(artwork));
            Assert.Single(_c.Library.GetDownloads(_c.Account, DownloadableType.Artwork));
            Assert.Equal("playable-" + song.Pk, Assert.Single(playables.GetRequestedDownloads()).Id);
            Assert.Equal("artwork-" + artwork.Pk, Assert.Single(artworks.GetRequestedDownloads()).Id);

            artworks.CancelDownloads();
            Assert.Empty(artworks.GetRequestedDownloads());
            Assert.Single(playables.GetRequestedDownloads());
            Assert.Single(artworks.GetAndResetFailedDownloads());
            Assert.Single(artworks.GetRequestedDownloads());
            return Task.CompletedTask;
        });
    }
}
