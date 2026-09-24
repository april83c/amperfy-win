using System.Reflection;
using Amperfy.Core.Player;
using Amperfy.Core.Tests.Helper;
using static Amperfy.Core.Tests.Player.TestUtil;

namespace Amperfy.Core.Tests.Player;

/// Records all calls of an <see cref="ILibrarySyncer"/> (tasks complete immediately).
public class LibrarySyncerProxy : DispatchProxy
{
    public List<(string Method, object?[]? Args)> Calls { get; } = [];
    public Func<Song, List<Song>>? SimilarSongs { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Calls.Add((targetMethod!.Name, args));
        var returnType = targetMethod.ReturnType;
        if (returnType == typeof(Task)) return Task.CompletedTask;
        if (returnType == typeof(Task<List<Song>>))
            return Task.FromResult(SimilarSongs?.Invoke((Song)args![0]!) ?? []);
        if (returnType == typeof(Task<LyricsList>)) return Task.FromResult(new LyricsList());
        return null;
    }

    public static (ILibrarySyncer Syncer, LibrarySyncerProxy Recorder) Create()
    {
        var proxy = DispatchProxy.Create<ILibrarySyncer, LibrarySyncerProxy>();
        return (proxy, (LibrarySyncerProxy)(object)proxy);
    }
}

internal sealed class FakeSystemMediaControls : ISystemMediaControls
{
    public NowPlayingMetadata? Metadata { get; private set; }
    public int NowPlayingUpdateCount { get; private set; }
    public SystemMediaPlaybackStatus Status { get; private set; }
    public bool? IsShuffle { get; private set; }
    public RepeatMode? Repeat { get; private set; }
    public SystemMediaCommandStates? States { get; private set; }
    public IReadOnlyList<double>? PlaybackRates { get; private set; }

    public void UpdateNowPlaying(NowPlayingMetadata? metadata)
    {
        Metadata = metadata;
        NowPlayingUpdateCount++;
    }

    public void UpdatePlaybackStatus(SystemMediaPlaybackStatus status) => Status = status;
    public void UpdateShuffle(bool isShuffle) => IsShuffle = isShuffle;
    public void UpdateRepeat(RepeatMode repeatMode) => Repeat = repeatMode;
    public void UpdateCommandStates(SystemMediaCommandStates states) => States = states;
    public void ConfigureCapabilities(double skipBackwardInterval, double skipForwardInterval, IReadOnlyList<double> supportedPlaybackRates) =>
        PlaybackRates = supportedPlaybackRates;

    public event Action? PlayRequested;
    public event Action? PauseRequested;
    public event Action? TogglePlayPauseRequested;
    public event Action? StopRequested;
    public event Action? PreviousRequested;
    public event Action? NextRequested;
    public event Action? SkipBackwardRequested;
    public event Action? SkipForwardRequested;
    public event Action<double>? SeekRequested;
    public event Action<bool>? ShuffleRequested;
    public event Action<RepeatMode>? RepeatRequested;
    public event Action<double>? PlaybackRateRequested;
    public event Action<int>? RatingRequested;
    public event Action<bool>? LikeRequested;

    public void RaisePlay() => PlayRequested?.Invoke();
    public void RaisePause() => PauseRequested?.Invoke();
    public void RaiseToggle() => TogglePlayPauseRequested?.Invoke();
    public void RaiseStop() => StopRequested?.Invoke();
    public void RaisePrevious() => PreviousRequested?.Invoke();
    public void RaiseNext() => NextRequested?.Invoke();
    public void RaiseSkipBackward() => SkipBackwardRequested?.Invoke();
    public void RaiseSkipForward() => SkipForwardRequested?.Invoke();
    public void RaiseSeek(double position) => SeekRequested?.Invoke(position);
    public void RaiseShuffle(bool shuffle) => ShuffleRequested?.Invoke(shuffle);
    public void RaiseRepeat(RepeatMode mode) => RepeatRequested?.Invoke(mode);
    public void RaisePlaybackRate(double rate) => PlaybackRateRequested?.Invoke(rate);
    public void RaiseRating(int rating) => RatingRequested?.Invoke(rating);
    public void RaiseLike(bool isNegative) => LikeRequested?.Invoke(isNegative);
}

/// Tests of the Windows specific parts of the player port (engine abstraction, preload, system media controls,
/// sleep timer, scrobbling, composition) - not covered by the ported Swift tests.
public class PlayerComponentsTest : IDisposable
{
    private readonly CoreDataHelper cdHelper;
    private readonly LibraryStorage library;
    private readonly Account account;
    private readonly AmperfySettings settings;
    private readonly EventLogger eventLogger;
    private readonly EventNotificationHandler notificationHandler;
    private readonly MockAudioStreamingPlayer engine;
    private readonly MockSongDownloader songDownloader;
    private readonly ILibrarySyncer librarySyncer;
    private readonly LibrarySyncerProxy syncerRecorder;
    private readonly FakeSystemMediaControls controls;
    private readonly PlayerComponents components;
    private readonly MockMusicPlayable mockMusicPlayable;
    private readonly List<(TimeSpan Interval, Action Tick)> createdTimers = [];

    private IPlayerFacade Player => components.Player;
    private BackendAudioPlayer Backend => components.BackendAudioPlayer;

    public PlayerComponentsTest()
    {
        cdHelper = new CoreDataHelper();
        library = cdHelper.CreateSeededStorage();
        account = library.GetAccount(TestAccountInfo.Create1());
        settings = new AmperfySettings();
        eventLogger = new EventLogger(library);
        notificationHandler = new EventNotificationHandler();
        engine = new MockAudioStreamingPlayer();
        songDownloader = new MockSongDownloader();
        (librarySyncer, syncerRecorder) = LibrarySyncerProxy.Create();
        controls = new FakeSystemMediaControls();
        var backendApi = new MockBackendApi();
        components = PlayerFactory.Create(library, settings, eventLogger, new AlwaysOnlineNetworkMonitor(), () => engine,
            _ => backendApi, _ => songDownloader, _ => librarySyncer, new UserStatistics(), notificationHandler, controls);
        components.BackendAudioPlayer.TimerFactory = CreateFakeTimer;
        components.SleepTimer.TimerFactory = CreateFakeTimer;
        mockMusicPlayable = new MockMusicPlayable();
        Player.AddNotifier(mockMusicPlayable);
    }

    public void Dispose() => components.Dispose();

    private IDisposable CreateFakeTimer(TimeSpan interval, Action tick)
    {
        createdTimers.Add((interval, tick));
        return new NoopDisposable();
    }

    /// Lets posted main thread work (engine events, Task.Yield continuations) run.
    private static async Task Flush()
    {
        for (var i = 0; i < 10; i++) await Task.Yield();
    }

    private List<AbstractPlayable> AllCachedSongs => NN(library.GetPlaylist(account, cdHelper.Seeder.Playlists[3].Id)).PlayablesList;

    [Fact]
    public void Factory_WiresPlayer_AppliesSettings() => Run(async () =>
    {
        Assert.Equal(1.0f, engine.Volume);
        Assert.False(engine.IsEqualizerEnabled);
        Assert.NotNull(components.NowPlayingInfoHandler);
        Assert.NotNull(components.RemoteCommandHandler);
        Assert.Equal(Enum.GetValues<PlaybackRate>().Length, controls.PlaybackRates!.Count);
        Assert.True(controls.States!.Play);

        var songs = AllCachedSongs;
        Player.Play(new PlayContext("All Cached", songs));
        await Flush();
        Assert.True(Player.IsPlaying);
        Assert.Equal(PlayType.Cache, Player.PlayType);
        Assert.Equal(songs[0], Player.CurrentlyPlaying);
        Assert.Equal(BackendAudioPlayer.GetFileUrl(library.GetFilePath(songs[0])!), engine.LastPlayedUrl);
        Assert.Equal(2, createdTimers.Count); // elapsed time (1s) + lyrics (0.1s)
        Assert.Contains(createdTimers, t => t.Interval == TimeSpan.FromSeconds(1));
        Assert.Contains(createdTimers, t => t.Interval == TimeSpan.FromSeconds(0.1));
    });

    [Fact]
    public void Preload_NextItemIsQueued_AndPlayedGaplessAfterFinish() => Run(async () =>
    {
        var songs = AllCachedSongs;
        Player.Play(new PlayContext("", songs));
        await Flush();
        Assert.Equal(1, engine.PlayCallCount);
        var currentUrl = engine.LastPlayedUrl!;

        engine.Duration = 100;
        engine.MockElapsedTime = 80;
        Backend.CheckForPreloadNextPlayerItem();
        Assert.Empty(engine.QueuedUrls); // more than 10s remaining

        engine.MockElapsedTime = 95;
        Backend.CheckForPreloadNextPlayerItem();
        Assert.Single(engine.QueuedUrls);
        Backend.CheckForPreloadNextPlayerItem();
        Assert.Single(engine.QueuedUrls); // preloaded only once

        engine.RaiseDidFinishPlaying(currentUrl);
        Assert.Equal(songs[1], Player.CurrentlyPlaying);
        Assert.True(Player.IsPlaying);
        Assert.Equal(PlayType.Cache, Player.PlayType);
        Assert.Equal(1, engine.PlayCallCount); // no new play request: the queued entry continues gapless
    });

    [Fact]
    public void Preload_NotDoneForRepeatSingle_OrPauseAfterTrack() => Run(async () =>
    {
        Player.Play(new PlayContext("", AllCachedSongs));
        await Flush();
        engine.Duration = 100;
        engine.MockElapsedTime = 95;
        Player.SetRepeatMode(RepeatMode.Single);
        Backend.CheckForPreloadNextPlayerItem();
        Assert.Empty(engine.QueuedUrls);
        Player.SetRepeatMode(RepeatMode.Off);
        Player.IsShouldPauseAfterFinishedPlaying = true;
        Backend.CheckForPreloadNextPlayerItem();
        Assert.Empty(engine.QueuedUrls);
    });

    [Fact]
    public void FinishWithoutPreload_PlaysNext() => Run(async () =>
    {
        var songs = AllCachedSongs;
        Player.Play(new PlayContext("", songs));
        await Flush();
        engine.RaiseDidFinishPlaying(engine.LastPlayedUrl!);
        await Flush();
        Assert.Equal(songs[1], Player.CurrentlyPlaying);
        Assert.Equal(2, engine.PlayCallCount);
        Assert.True(Player.IsPlaying);
    });

    [Fact]
    public void UnexpectedError_ReportsAndReinserts() => Run(async () =>
    {
        Player.Play(new PlayContext("", AllCachedSongs));
        await Flush();
        var error = new InvalidOperationException("decoder failed");
        engine.RaiseUnexpectedError(error);
        await Flush();
        Assert.True(Backend.IsErrorOccurred);
        Assert.Same(error, mockMusicPlayable.ThrownError);
        Assert.True(Player.IsPlaying); // was playing before -> reinserted and continues
        Assert.Contains(library.GetAllLogEntries(), e => e.Message.Contains("decoder failed"));
    });

    [Fact]
    public void StreamedSong_UsesStreamingSettings_AndAutoCaches() => Run(async () =>
    {
        Backend.SetStreamingMaxBitrates(new StreamingMaxBitrates(StreamingMaxBitratePreference.Limit128, StreamingMaxBitratePreference.Limit64));
        Backend.SetStreamingTranscodings(new StreamingTranscodings(StreamingFormatPreference.Mp3, StreamingFormatPreference.Raw));
        Player.IsAutoCachePlayedItems = true;
        var song = NN(library.GetSong(account, "3"));
        Player.Play(new PlayContext("", [song]));
        Assert.False(Player.IsPlaying);
        await Flush();
        Assert.True(Player.IsPlaying);
        Assert.Equal(PlayType.Stream, Player.PlayType);
        Assert.Equal(StreamingMaxBitratePreference.Limit128, Player.ActiveStreamingBitrate);
        Assert.Equal(StreamingFormatPreference.Mp3, Player.ActiveTranscodingFormat);
        Assert.Equal(TestFiles.TestUrl.OriginalString, engine.LastPlayedUrl);
        Assert.Contains(song, songDownloader.Downloadables.OfType<AbstractPlayable>());
    });

    [Fact]
    public void ReplayGainAndEqualizer_AppliedToEngine()
    {
        Assert.Equal(1.0f, BackendAudioPlayer.CalculateReplayGainVolume(true, 0.0f, true, 1.0f));
        Assert.Equal(0.5012f, BackendAudioPlayer.CalculateReplayGainVolume(true, -6.0f, false, 0.5f), 3);
        Assert.Equal(0.5f, BackendAudioPlayer.CalculateReplayGainVolume(false, -6.0f, true, 0.5f));
        Assert.Equal(0.25059f, BackendAudioPlayer.CalculateReplayGainVolume(true, -6.0f, true, 0.5f), 3);

        var bass = EqualizerPreset.IncreasedBass.AsEqualizerSetting();
        Backend.UpdateEqualizerEnabled(true);
        Backend.UpdateEqualizerSetting(bass);
        Assert.True(engine.IsEqualizerEnabled);
        Assert.Equal(bass.Gains, engine.EqualizerGains);
        Assert.Equal(bass.CompensatedVolume, engine.ReplayGainVolume, 4);

        Backend.UpdateEqualizerEnabled(false);
        Assert.False(engine.IsEqualizerEnabled);
        Assert.All(engine.EqualizerGains!, g => Assert.Equal(0.0f, g));
        Assert.Equal(1.0f, engine.ReplayGainVolume);
    }

    [Fact]
    public void ReplayGain_TrackGainOfPlayedSong() => Run(async () =>
    {
        var songs = AllCachedSongs;
        songs[0].ReplayGainTrackGain = -6.0f;
        Player.Play(new PlayContext("", songs));
        await Flush();
        Assert.Equal(0.5012f, engine.ReplayGainVolume, 3);
        components.Player.UpdateReplayGainEnabled(false);
        Assert.Equal(1.0f, engine.ReplayGainVolume);
    });

    [Fact]
    public void Radio_StreamMetadata_UpdatesNowPlaying() => Run(async () =>
    {
        var radios = library.GetRadios(account);
        Player.Play(new PlayContext("Radios", radios));
        await Flush();
        Assert.Equal(radios[0], Player.CurrentlyPlaying);
        Assert.Equal(PlayType.Stream, Player.PlayType);
        Assert.Equal(radios[0].Url, engine.LastPlayedUrl);
        Assert.Null(Player.CurrentRadioNowPlaying);

        engine.RaiseDidReadMetadata(new Dictionary<string, string> { ["StreamTitle"] = "'Artist A - Song - Part 2'" });
        Assert.Equal(new RadioNowPlayingInfo("Song - Part 2", "Artist A"), Player.CurrentRadioNowPlaying);
        Assert.Equal("Song - Part 2", controls.Metadata!.Title);
        Assert.Equal("Artist A", controls.Metadata.Artist);
        Assert.True(controls.Metadata.IsLiveStream);
        Assert.Equal(RemoteCommandHandler.GetCommandStates(DerivedPlayableType.Radio, false), controls.States);
    });

    [Fact]
    public void ParseStreamTitle()
    {
        Assert.Equal(new RadioNowPlayingInfo("Title", "Artist"), AudioPlayer.ParseStreamTitle("Artist - Title"));
        Assert.Equal(new RadioNowPlayingInfo("Only Title", ""), AudioPlayer.ParseStreamTitle("\"Only Title\""));
    }

    [Fact]
    public void Autoplay_AppendsSimilarSongs() => Run(async () =>
    {
        settings.User.IsAutoplayEnabled = true;
        var similar = NN(library.GetSong(account, "5"));
        syncerRecorder.SimilarSongs = _ => [similar];
        var song = NN(library.GetSong(account, "36"));
        Player.Play(new PlayContext("", [song]));
        await Flush();
        Player.PlayNext();
        await Flush();
        Assert.Contains(syncerRecorder.Calls, c => c.Method == nameof(ILibrarySyncer.RequestSimilarSongsAsync));
        Assert.Equal(similar, Player.CurrentlyPlaying);
    });

    [Fact]
    public void SystemMediaControls_NowPlayingAndCommands() => Run(async () =>
    {
        var songs = AllCachedSongs;
        Player.Play(new PlayContext("", songs));
        await Flush();
        Assert.Equal(SystemMediaPlaybackStatus.Playing, controls.Status);
        Assert.Equal(songs[0].Title, controls.Metadata!.Title);
        Assert.Equal(songs[0].CreatorName, controls.Metadata.Artist);
        Assert.False(controls.Metadata.IsCloudItem);
        Assert.Equal(RemoteCommandHandler.GetCommandStates(DerivedPlayableType.Song, false), controls.States);

        controls.RaiseNext();
        Assert.Equal(songs[1], Player.CurrentlyPlaying);
        controls.RaisePrevious();
        Assert.Equal(songs[0], Player.CurrentlyPlaying);

        controls.RaiseShuffle(true);
        Assert.True(Player.IsShuffle);
        Assert.True(controls.IsShuffle);
        controls.RaiseShuffle(true); // already shuffled -> no toggle
        Assert.True(Player.IsShuffle);

        controls.RaiseRepeat(RepeatMode.All);
        Assert.Equal(RepeatMode.All, Player.RepeatMode);
        Assert.Equal(RepeatMode.All, controls.Repeat);

        controls.RaisePlaybackRate(1.5);
        Assert.Equal(PlaybackRate.OneDot5, Player.PlaybackRate);
        Assert.Equal(1.5f, engine.Rate);

        await Flush();
        controls.RaiseSeek(42);
        Assert.Equal(42, Player.ElapsedTime);

        controls.RaisePause();
        Assert.False(Player.IsPlaying);
        Assert.Equal(SystemMediaPlaybackStatus.Paused, controls.Status);
        controls.RaiseToggle();
        Assert.True(Player.IsPlaying);

        Player.Stop();
        Assert.Null(controls.Metadata);
        Assert.Equal(SystemMediaPlaybackStatus.Stopped, controls.Status);
    });

    [Fact]
    public void SystemMediaControls_LikeTogglesFavorite() => Run(async () =>
    {
        var song = NN(library.GetSong(account, "36"));
        Assert.False(song.IsFavorite);
        Player.Play(new PlayContext("", [song]));
        await Flush();
        controls.RaiseLike(false);
        await Flush();
        Assert.True(song.IsFavorite);
        Assert.True(controls.States!.IsLikeActive);
        Assert.Contains(syncerRecorder.Calls, c => c.Method == nameof(ILibrarySyncer.SetFavoriteAsync));
        controls.RaiseLike(false); // already a favorite -> no toggle
        await Flush();
        Assert.True(song.IsFavorite);
    });

    [Fact]
    public void NowPlaying_UpdatesOnDownloadFinished() => Run(async () =>
    {
        var song = NN(library.GetSong(account, "36"));
        Player.Play(new PlayContext("", [song]));
        await Flush();
        var count = controls.NowPlayingUpdateCount;
        notificationHandler.Post(AmperfyNotification.DownloadFinishedSuccess, null, new DownloadNotification("playable-99999"));
        Assert.Equal(count, controls.NowPlayingUpdateCount);
        notificationHandler.Post(AmperfyNotification.DownloadFinishedSuccess, null, new DownloadNotification(Amperfy.Core.Downloads.DownloadableExtensions.UniqueId(song)));
        Assert.Equal(count + 1, controls.NowPlayingUpdateCount);
    });

    [Fact]
    public void NotificationAdapter_PostsPlayerNotifications() => Run(async () =>
    {
        var received = new List<AmperfyNotification>();
        void Handler(NotificationArgs args) => received.Add(args.Name);
        using var t1 = notificationHandler.Register(AmperfyNotification.PlayerPlay, Handler);
        using var t2 = notificationHandler.Register(AmperfyNotification.PlayerPause, Handler);
        using var t3 = notificationHandler.Register(AmperfyNotification.PlayerStop, Handler);
        Player.Play(new PlayContext("", AllCachedSongs));
        await Flush();
        Player.Pause();
        Player.Stop();
        // play(context:) pauses the player first (Swift setPlayerModeForContextPlay)
        Assert.Equal([AmperfyNotification.PlayerPause, AmperfyNotification.PlayerPlay, AmperfyNotification.PlayerPause, AmperfyNotification.PlayerStop], received);
    });

    [Fact]
    public void DownloadPreparation_PreDownloadsNextItems() => Run(async () =>
    {
        Player.IsAutoCachePlayedItems = true;
        var playlist = NN(library.GetPlaylist(account, cdHelper.Seeder.Playlists[2].Id)); // "3", "10T", "19" (none cached)
        var cached = NN(library.GetSong(account, "36"));
        Player.Play(new PlayContext("", [cached, .. playlist.Playables]));
        await Flush();
        Assert.Equal(3, songDownloader.Downloadables.Count);
        Assert.Equal(playlist.Playables.Cast<object>(), songDownloader.Downloadables.Cast<object>());
    });

    [Fact]
    public void SleepTimer_PausesAfterDuration() => Run(async () =>
    {
        var sleepTimer = components.SleepTimer;
        Player.Play(new PlayContext("", AllCachedSongs));
        await Flush();
        createdTimers.Clear();
        var option = SleepTimer.Options.First(o => o.Duration == TimeSpan.FromMinutes(15));
        sleepTimer.Activate(option);
        Assert.True(sleepTimer.IsActive);
        Assert.NotNull(sleepTimer.FireDate);
        Assert.StartsWith("Pause at: ", sleepTimer.StatusDescription);
        var timer = Assert.Single(createdTimers);
        Assert.Equal(TimeSpan.FromMinutes(15), timer.Interval);

        timer.Tick();
        Assert.False(Player.IsPlaying);
        Assert.False(sleepTimer.IsActive);
        Assert.Null(sleepTimer.FireDate);
    });

    [Fact]
    public void SleepTimer_EndOfTrack() => Run(async () =>
    {
        var sleepTimer = components.SleepTimer;
        var songs = AllCachedSongs;
        Player.Play(new PlayContext("", songs));
        await Flush();
        sleepTimer.Activate(SleepTimer.Options[0]);
        Assert.True(sleepTimer.IsEndOfTrackActive);
        Assert.Equal("Pause at end of Song", sleepTimer.StatusDescription);
        components.MusicPlayer.DidItemFinishedPlaying();
        Assert.False(Player.IsPlaying);
        Assert.False(sleepTimer.IsActive);
        Assert.Equal(songs[0], Player.CurrentlyPlaying);

        sleepTimer.ActivateEndOfTrack();
        sleepTimer.Deactivate();
        Assert.False(Player.IsShouldPauseAfterFinishedPlaying);
    });

    [Fact]
    public void ScrobbleSyncer_CachesScrobbleWhenOffline() => Run(async () =>
    {
        settings.User.IsOfflineMode = true;
        var scrobbleTimers = new List<Action>();
        var syncer = new ScrobbleSyncer(Player, new AlwaysOnlineNetworkMonitor(), account, library, settings, librarySyncer, eventLogger)
        {
            OneShotTimerFactory = (_, tick) =>
            {
                scrobbleTimers.Add(tick);
                return new NoopDisposable();
            },
        };
        Player.AddNotifier(syncer);
        var song = NN(library.GetSong(account, "36"));
        Player.Play(new PlayContext("", [song]));
        await Flush();
        var tick = Assert.Single(scrobbleTimers);
        tick(); // listened long enough
        Player.Stop();
        await Flush();
        Assert.Equal(1, library.GetUploadableScrobbleEntryCount(account));
        Assert.DoesNotContain(syncerRecorder.Calls, c => c.Method == nameof(ILibrarySyncer.SyncNowPlayingAsync));
        GC.KeepAlive(syncer);
    });

    [Fact]
    public void ScrobbleSyncer_ReportsNowPlayingAndScrobbleOnline() => Run(async () =>
    {
        var scrobbleTimers = new List<Action>();
        var syncer = new ScrobbleSyncer(Player, new AlwaysOnlineNetworkMonitor(), account, library, settings, librarySyncer, eventLogger)
        {
            OneShotTimerFactory = (_, tick) =>
            {
                scrobbleTimers.Add(tick);
                return new NoopDisposable();
            },
        };
        Player.AddNotifier(syncer);
        var song = NN(library.GetSong(account, "36"));
        Player.Play(new PlayContext("", [song]));
        await Flush();
        Assert.Contains(syncerRecorder.Calls, c => c.Method == nameof(ILibrarySyncer.SyncNowPlayingAsync) && (NowPlayingSongPosition)c.Args![1]! == NowPlayingSongPosition.Start);
        Assert.Single(scrobbleTimers)();
        Player.Stop();
        await Flush();
        Assert.Contains(syncerRecorder.Calls, c => c.Method == nameof(ILibrarySyncer.SyncNowPlayingAsync) && (NowPlayingSongPosition)c.Args![1]! == NowPlayingSongPosition.End);
        Assert.Equal(0, library.GetUploadableScrobbleEntryCount(account));
        Assert.Single(library.GetScrobbleEntries(account));
        GC.KeepAlive(syncer);
    });
}
