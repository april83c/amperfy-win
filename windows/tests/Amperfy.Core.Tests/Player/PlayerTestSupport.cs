using Amperfy.Core.Downloads;
using Amperfy.Core.Player;
using Amperfy.Core.Tests.Helper;

// The core uses process wide singletons (CacheFileManager.Shared, MainThread), so test classes must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Amperfy.Core.Tests.Player;

internal static class TestUtil
{
    /// Swift `guard let x = ... else { XCTFail(); return }`
    public static T NN<T>(T? value) where T : class
    {
        Assert.NotNull(value);
        return value!;
    }

    public static T NN<T>(T? value) where T : struct
    {
        Assert.NotNull(value);
        return value!.Value;
    }

    /// Generated 1x1 PNG (Swift tests used UIImage.getGeneratedArtwork(...).pngData())
    public static byte[] PngBytes => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    /// Runs the test body on a single threaded "main thread" (like @MainActor tests in Swift).
    public static void Run(Func<Task> body) => SingleThreadSynchronizationContext.Run(body);

    /// Port of checkQueueInfoConsistency() (identical in PlayQueueHandlerTest and MusicPlayerTest).
    public static void CheckQueueInfoConsistency(PlayQueueHandler testQueueHandler)
    {
        // Prev Queue
        var prevQueueCount = testQueueHandler.PrevQueueCount;
        var prevQueueItemsAll = testQueueHandler.GetAllPrevQueueItems();
        Assert.Equal(prevQueueCount, prevQueueItemsAll.Count);
        for (var index = 0; index < prevQueueItemsAll.Count; index++)
            Assert.Equal(prevQueueItemsAll[index], testQueueHandler.GetPrevQueueItem(index));
        var prevRangItemsAll = testQueueHandler.GetPrevQueueItems(0, null);
        Assert.Equal(prevQueueItemsAll, prevRangItemsAll);
        if (prevQueueCount > 0)
        {
            prevRangItemsAll = testQueueHandler.GetPrevQueueItems(0, prevQueueCount - 1);
            Assert.Equal(prevQueueItemsAll, prevRangItemsAll);
        }
        if (prevQueueCount > 1)
        {
            var prevRangeItemsBeforeEnd1 = testQueueHandler.GetPrevQueueItems(0, prevQueueCount - 2);
            Assert.Equal(prevQueueCount - 1, prevRangeItemsBeforeEnd1.Count);
            Assert.Equal(prevQueueItemsAll[0..(prevQueueCount - 1)], prevRangeItemsBeforeEnd1);
            var prevRangeItemsOff1 = testQueueHandler.GetPrevQueueItems(1, prevQueueCount - 1);
            Assert.Equal(prevQueueCount - 1, prevRangeItemsOff1.Count);
            Assert.Equal(prevQueueItemsAll[1..prevQueueCount], prevRangeItemsOff1);
            Assert.Equal(prevQueueItemsAll[1], prevRangeItemsOff1[0]);
        }
        if (prevQueueCount > 2)
        {
            var prevRangeItemsMissingStartAndEnd = testQueueHandler.GetPrevQueueItems(1, prevQueueCount - 2);
            Assert.Equal(prevQueueCount - 2, prevRangeItemsMissingStartAndEnd.Count);
            Assert.Equal(prevQueueItemsAll[1..(prevQueueCount - 1)], prevRangeItemsMissingStartAndEnd);
            Assert.Equal(prevQueueItemsAll[1], prevRangeItemsMissingStartAndEnd[0]);
            Assert.Equal(prevQueueItemsAll[prevQueueCount - 2], prevRangeItemsMissingStartAndEnd[^1]);
        }
        // User Queue
        var userQueueCount = testQueueHandler.UserQueueCount;
        var userQueueItemsAll = testQueueHandler.GetAllUserQueueItems();
        Assert.Equal(userQueueCount, userQueueItemsAll.Count);
        for (var index = 0; index < userQueueItemsAll.Count; index++)
            Assert.Equal(userQueueItemsAll[index], testQueueHandler.GetUserQueueItem(index));
        var userRangItemsAll = testQueueHandler.GetUserQueueItems(0, null);
        Assert.Equal(userQueueItemsAll, userRangItemsAll);
        if (userQueueCount > 0)
        {
            userRangItemsAll = testQueueHandler.GetUserQueueItems(0, userQueueCount - 1);
            Assert.Equal(userQueueItemsAll, userRangItemsAll);
        }
        if (userQueueCount > 1)
        {
            var userRangeItemsBeforeEnd1 = testQueueHandler.GetUserQueueItems(0, userQueueCount - 2);
            Assert.Equal(userQueueCount - 1, userRangeItemsBeforeEnd1.Count);
            Assert.Equal(userQueueItemsAll[0..(userQueueCount - 1)], userRangeItemsBeforeEnd1);
            var userRangeItemsOff1 = testQueueHandler.GetUserQueueItems(1, userQueueCount - 1);
            Assert.Equal(userQueueCount - 1, userRangeItemsOff1.Count);
            Assert.Equal(userQueueItemsAll[1..userQueueCount], userRangeItemsOff1);
            Assert.Equal(userQueueItemsAll[1], userRangeItemsOff1[0]);
        }
        if (userQueueCount > 2)
        {
            var userRangeItemsMissingStartAndEnd = testQueueHandler.GetUserQueueItems(1, userQueueCount - 2);
            Assert.Equal(userQueueCount - 2, userRangeItemsMissingStartAndEnd.Count);
            Assert.Equal(userQueueItemsAll[1..(userQueueCount - 1)], userRangeItemsMissingStartAndEnd);
            Assert.Equal(userQueueItemsAll[1], userRangeItemsMissingStartAndEnd[0]);
            Assert.Equal(userQueueItemsAll[userQueueCount - 2], userRangeItemsMissingStartAndEnd[^1]);
        }
        // Next Queue
        var nextQueueCount = testQueueHandler.NextQueueCount;
        var nextQueueItemsAll = testQueueHandler.GetAllNextQueueItems();
        Assert.Equal(nextQueueCount, nextQueueItemsAll.Count);
        for (var index = 0; index < nextQueueItemsAll.Count; index++)
            Assert.Equal(nextQueueItemsAll[index], testQueueHandler.GetNextQueueItem(index));
        var nextRangItemsAll = testQueueHandler.GetNextQueueItems(0, null);
        Assert.Equal(nextQueueItemsAll, nextRangItemsAll);
        if (nextQueueCount > 0)
        {
            nextRangItemsAll = testQueueHandler.GetNextQueueItems(0, nextQueueCount - 1);
            Assert.Equal(nextQueueItemsAll, nextRangItemsAll);
        }
        if (nextQueueCount > 1)
        {
            var nextRangeItemsBeforeEnd1 = testQueueHandler.GetNextQueueItems(0, nextQueueCount - 2);
            Assert.Equal(nextQueueCount - 1, nextRangeItemsBeforeEnd1.Count);
            Assert.Equal(nextQueueItemsAll[0..(nextQueueCount - 1)], nextRangeItemsBeforeEnd1);
            var nextRangeItemsOff1 = testQueueHandler.GetNextQueueItems(1, nextQueueCount - 1);
            Assert.Equal(nextQueueCount - 1, nextRangeItemsOff1.Count);
            Assert.Equal(nextQueueItemsAll[1..nextQueueCount], nextRangeItemsOff1);
            Assert.Equal(nextQueueItemsAll[1], nextRangeItemsOff1[0]);
        }
        if (nextQueueCount > 2)
        {
            var nextRangeItemsMissingStartAndEnd = testQueueHandler.GetNextQueueItems(1, nextQueueCount - 2);
            Assert.Equal(nextQueueCount - 2, nextRangeItemsMissingStartAndEnd.Count);
            Assert.Equal(nextQueueItemsAll[1..(nextQueueCount - 1)], nextRangeItemsMissingStartAndEnd);
            Assert.Equal(nextQueueItemsAll[1], nextRangeItemsMissingStartAndEnd[0]);
            Assert.Equal(nextQueueItemsAll[nextQueueCount - 2], nextRangeItemsMissingStartAndEnd[^1]);
        }
    }
}

/// Port of MOCK_AudioStreamingPlayer: "plays" instantly; didStartPlaying is posted to the main thread.
internal sealed class MockAudioStreamingPlayer : IAudioStreamingPlayer
{
    public double MockElapsedTime { get; set; }
    public bool IsPlaying { get; set; }
    public bool IsStopped { get; set; } = true;
    public List<string> QueuedUrls { get; } = [];
    public float[]? EqualizerGains { get; private set; }
    public bool IsEqualizerEnabled { get; private set; }

    public int PlayCallCount { get; private set; }
    public string? LastPlayedUrl { get; private set; }

    public void Play(string url, string? mimeType, IReadOnlyDictionary<string, string>? httpHeaders)
    {
        PlayCallCount++;
        LastPlayedUrl = url;
        MockElapsedTime = 0.0;
        IsPlaying = true;
        IsStopped = false;
        var context = SynchronizationContext.Current;
        if (context is null) DidStartPlaying?.Invoke(url);
        else context.Post(_ => DidStartPlaying?.Invoke(url), null);
    }

    public void Queue(string url, string? mimeType, IReadOnlyDictionary<string, string>? httpHeaders) => QueuedUrls.Add(url);

    public void Pause() => IsPlaying = false;

    public void Resume() { }

    public void Stop()
    {
        IsPlaying = false;
        IsStopped = true;
    }

    public void Seek(double seconds) => MockElapsedTime = seconds;

    public float Rate { get; set; } = 1.0f;
    public float Volume { get; set; } = 1.0f;
    public double Progress => MockElapsedTime;
    public double Duration { get; set; }
    public AudioStreamingPlayerState State =>
        IsStopped ? AudioStreamingPlayerState.Stopped : IsPlaying ? AudioStreamingPlayerState.Playing : AudioStreamingPlayerState.Paused;

    public void SetEqualizer(float[] gains, bool enabled)
    {
        EqualizerGains = gains;
        IsEqualizerEnabled = enabled;
    }

    public float ReplayGainVolume { get; set; } = 1.0f;

    public event Action<string>? DidStartPlaying;
    public event Action<string>? DidFinishPlaying;
    public event Action<Exception>? UnexpectedError;
    public event Action? DidCancel;
    public event Action<IReadOnlyDictionary<string, string>>? DidReadMetadata;

    public void RaiseDidFinishPlaying(string url) => DidFinishPlaying?.Invoke(url);
    public void RaiseUnexpectedError(Exception ex) => UnexpectedError?.Invoke(ex);
    public void RaiseDidCancel() => DidCancel?.Invoke();
    public void RaiseDidReadMetadata(IReadOnlyDictionary<string, string> metadata) => DidReadMetadata?.Invoke(metadata);

    public void Dispose() { }
}

/// Port of MOCK_SongDownloader
internal sealed class MockSongDownloader : IDownloadManageable
{
    public List<IDownloadable> Downloadables { get; } = [];
    public bool IsNoDownloadRequested() => Downloadables.Count == 0;
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

/// Port of MOCK_BackendApi
internal sealed class MockBackendApi : IBackendApi
{
    public string ClientApiVersion => "";
    public string ServerApiVersion => "";
    public IReadOnlyDictionary<string, string> HttpHeaders { get; } = new Dictionary<string, string>();
    public void ProvideCredentials(LoginCredentials credentials) { }
    public Task IsAuthenticationValidAsync(LoginCredentials credentials) => throw BackendError.NotSupported;
    public Task<Uri> GenerateUrlForDownloadingPlayableAsync(AbstractPlayableInfo playableInfo) => Task.FromResult(TestFiles.TestUrl);
    public Task<Uri> GenerateUrlForStreamingPlayableAsync(AbstractPlayableInfo playableInfo, StreamingMaxBitratePreference maxBitrate, StreamingFormatPreference formatPreference) =>
        Task.FromResult(TestFiles.TestUrl);
    public Task<Uri> GenerateUrlForArtworkAsync(Artwork artwork) => Task.FromResult(TestFiles.TestUrl);
    public ResponseError? CheckForErrorResponse(ApiDataResponse response) => null;
    public ILibrarySyncer CreateLibrarySyncer(Account account, LibraryStorage storage) => throw BackendError.NotSupported;
    public IDownloadManagerDelegate CreateArtworkDownloadDelegate() => throw BackendError.NotSupported;
    public ArtworkRemoteInfo? ExtractArtworkInfoFromUrl(string urlString) => null;
    public CleansedUrl Cleanse(Uri? url) => new("");
}

/// Port of MOCK_MusicPlayable (XCTestExpectation -> awaitable counter)
internal sealed class MockMusicPlayable : IMusicPlayable
{
    private int _expectedCount;
    private int _count;
    private TaskCompletionSource? _tcs;

    public Exception? ThrownError { get; private set; }

    /// Swift: expectation(description:) + expectedFulfillmentCount
    public void Expect(int count)
    {
        _expectedCount = count;
        _count = 0;
        _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// Swift: wait(for: [expectation], timeout:)
    public async Task WaitAsync(double timeoutInSec)
    {
        var tcs = _tcs ?? throw new InvalidOperationException("No expectation");
        var finished = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutInSec)));
        Assert.True(finished == tcs.Task, $"Expectation not fulfilled: didStartPlaying called {_count} of {_expectedCount} times");
    }

    public void DidStartPlaying()
    {
        if (_tcs is null) return;
        _count++;
        if (_count >= _expectedCount) _tcs.TrySetResult();
    }

    public void ErrorOccurred(Exception error) => ThrownError = error;
}

internal sealed class NoopDisposable : IDisposable
{
    public void Dispose() { }
}
