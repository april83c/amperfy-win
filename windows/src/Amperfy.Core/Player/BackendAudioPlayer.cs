using Amperfy.Core.Api;
using Amperfy.Core.Downloads;

namespace Amperfy.Core.Player;

/// Port of BackendAudioPlayer.swift: drives the audio engine (<see cref="IAudioStreamingPlayer"/>):
/// cached vs. stream playback, gapless preload of the next item, streaming bitrate/format selection,
/// auto caching of played items, error handling, EQ and replay gain. Main thread only.
public sealed class BackendAudioPlayer
{
    private const string Log = "BackendAudioPlayer";
    private static readonly TimeSpan UpdateElapsedTimeInterval = TimeSpan.FromSeconds(1.0);
    private static readonly TimeSpan UpdateLyricsTimeInterval = TimeSpan.FromSeconds(0.1);
    private const double PreloadRemainingTimeInSec = 10;

    private readonly Func<AccountInfo, IDownloadManageable> _getPlayableDownloaderCB;
    private readonly LibraryStorage _cacheProxy;
    private readonly Func<AccountInfo, IBackendApi> _getBackendApiCB;
    private readonly UserStatistics _userStatistics;
    private readonly Func<IAudioStreamingPlayer> _createAudioStreamingPlayerCB;
    private readonly EventLogger _eventLogger;
    private readonly INetworkMonitor _networkMonitor;

    private bool _isTriggerReinsertPlayableAllowed = true;
    private bool _wasPlayingBeforeErrorOccurred;
    private PlaybackRate _userDefinedPlaybackRate = PlaybackRate.One;
    private string _currentPreparedUrl = "";
    private string _currentPlayUrl = "";
    private AbstractPlayable? _nextPreloadedPlayable;
    private string _nextPreloadedUrl = "";
    private bool _isPreviousPlaylableFinshed = true;
    private bool _isAutoStartPlayback = true;
    private double? _seekTimeWhenStarted;
    private IDisposable? _timerElapsedTimeInterval;
    private IDisposable? _timerLyricsTimeInterval;
    private float _volumePlayer = 1.0f;

    private IAudioStreamingPlayer? _player;

    // ReplayGain Settings
    private bool _isReplayGainEnabled = true;
    private float _currentReplayGainValue; // ReplayGain in dB
    // EQ Settings
    private float _equalizerVolumeCompensation = 1.0f;
    private bool _isEqualizerEnabled = true;
    private EqualizerSetting _currentEqualizerSetting = EqualizerSetting.Off;

    private PlayType? _perloadedPlayType;
    private StreamingMaxBitratePreference? _perloadedStreamingBitrate;
    private StreamingFormatPreference? _preloadTranscodingFormat;

    /// Creates the repeating timers (elapsed time / lyrics). Defaults to <see cref="MainThread.CreateTimer"/>.
    /// Tests replace it to avoid real timers.
    public Func<TimeSpan, Action, IDisposable> TimerFactory { get; set; } = (interval, tick) => MainThread.CreateTimer(interval, tick);

    public bool IsOfflineMode { get; set; }
    public bool IsAutoCachePlayedItems { get; set; } = true;
    public Func<AbstractPlayable?>? NextPlayablePreloadCB { get; set; }
    public Action? TriggerReinsertPlayableCB { get; set; }
    public bool IsPlaying { get; private set; }
    public bool IsErrorOccurred { get; private set; }
    public PlayType? PlayType { get; private set; }
    public StreamingMaxBitratePreference? ActiveStreamingBitrate { get; private set; }
    public StreamingFormatPreference? ActiveTranscodingFormat { get; private set; }

    /// Linear gain currently applied to the replay gain stage (for diagnostics/tests).
    public float ReplayGainOutputVolume { get; private set; } = 1.0f;

    public StreamingMaxBitrates StreamingMaxBitrates { get; private set; } = new();

    public void SetStreamingMaxBitrates(StreamingMaxBitrates to)
    {
        var oldBitrate = StreamingMaxBitrates.GetActive(_networkMonitor);
        var newBitrate = to.GetActive(_networkMonitor);
        AmperfyLog.Info(Log, $"Update Streaming Max Bitrate: {oldBitrate.Description()} -> {newBitrate.Description()} (for next stream)");
        StreamingMaxBitrates = to;
    }

    public StreamingTranscodings StreamingTranscodings { get; private set; } = new();

    public void SetStreamingTranscodings(StreamingTranscodings to)
    {
        AmperfyLog.Info(Log, $"Update Streaming Transcoding: <Wifi: {StreamingTranscodings.Wifi.Description()}, Cellular: {StreamingTranscodings.Cellular.Description()}> -> <Wifi: {to.Wifi.Description()}, Cellular: {to.Cellular.Description()}> (for next stream)");
        StreamingTranscodings = to;
    }

    internal IBackendAudioPlayerNotifiable? Responder { get; set; }

    public float Volume
    {
        get => _volumePlayer;
        set
        {
            _volumePlayer = value;
            if (_player is not null) _player.Volume = value;
        }
    }

    public bool IsStopped => PlayType is null;

    public double ElapsedTime => _player?.Progress ?? 0.0;

    public double Duration => _player?.Duration ?? 0.0;

    public PlaybackRate PlaybackRate => _userDefinedPlaybackRate;

    public bool CanBeContinued =>
        _currentPlayUrl != "" &&
        _player?.State is AudioStreamingPlayerState.Paused or AudioStreamingPlayerState.Playing or AudioStreamingPlayerState.Buffering;

    public BackendAudioPlayer(
        Func<IAudioStreamingPlayer> createAudioStreamingPlayerCB,
        EventLogger eventLogger,
        Func<AccountInfo, IBackendApi> getBackendApiCB,
        INetworkMonitor networkMonitor,
        Func<AccountInfo, IDownloadManageable> getPlayableDownloaderCB,
        LibraryStorage cacheProxy,
        UserStatistics userStatistics)
    {
        _createAudioStreamingPlayerCB = createAudioStreamingPlayerCB;
        _getBackendApiCB = getBackendApiCB;
        _networkMonitor = networkMonitor;
        _eventLogger = eventLogger;
        _getPlayableDownloaderCB = getPlayableDownloaderCB;
        _cacheProxy = cacheProxy;
        _userStatistics = userStatistics;

        InitAudioStreamingPlayerAndNodes();
    }

    /// The URL (entry id) used for a cached file.
    public static string GetFileUrl(string absoluteFilePath) => new Uri(absoluteFilePath).AbsoluteUri;

    // --- timers --------------------------------------------------------------------------------

    private void StartTimers()
    {
        StopTimers();
        _timerElapsedTimeInterval = TimerFactory(UpdateElapsedTimeInterval, () =>
        {
            CheckForPreloadNextPlayerItem();
            Responder?.DidElapsedTimeChange();
        });
        _timerLyricsTimeInterval = TimerFactory(UpdateLyricsTimeInterval, () =>
        {
            Responder?.DidLyricsTimeChange(TimeSpan.FromMilliseconds(Math.Floor(ElapsedTime * 1000)));
        });
    }

    /// Stops the progress timers (app shutdown).
    public void Shutdown() => StopTimers();

    private void StopTimers()
    {
        _timerElapsedTimeInterval?.Dispose();
        _timerElapsedTimeInterval = null;
        _timerLyricsTimeInterval?.Dispose();
        _timerLyricsTimeInterval = null;
    }

    /// Called every second while playing: queues the next item ~10s before the current one ends (gapless).
    internal void CheckForPreloadNextPlayerItem()
    {
        if (!_isAutoStartPlayback) return;
        var elapsedTime = ElapsedTime;
        var duration = Duration;
        if (_nextPreloadedPlayable is null && double.IsFinite(elapsedTime) && elapsedTime > 0 && double.IsFinite(duration) && duration > 0)
        {
            var remainingTime = duration - elapsedTime;
            if (remainingTime > 0 && remainingTime < PreloadRemainingTimeInSec)
            {
                _nextPreloadedPlayable = NextPlayablePreloadCB?.Invoke();
                if (_nextPreloadedPlayable is not { } nextPreloadedPlayable) return;
                AmperfyLog.Info(Log, $"Preloading: {nextPreloadedPlayable.DisplayString}");
                if (nextPreloadedPlayable.IsCached)
                {
                    InsertCachedPlayable(nextPreloadedPlayable, BackendAudioQueueType.Queue);
                }
                else if (!IsOfflineMode)
                {
                    _ = PreloadStreamAsync(nextPreloadedPlayable);
                }
            }
        }
    }

    private async Task PreloadStreamAsync(AbstractPlayable nextPreloadedPlayable)
    {
        await Task.Yield(); // Swift: Task { @MainActor in ... }
        try
        {
            await InsertStreamPlayableAsync(nextPreloadedPlayable, BackendAudioQueueType.Queue);
            if (IsAutoCachePlayedItems && nextPreloadedPlayable.IsDownloadAvailable && nextPreloadedPlayable.Account?.Info is { } accountInfo)
                _getPlayableDownloaderCB(accountInfo).Download(nextPreloadedPlayable);
        }
        catch (Exception ex)
        {
            _nextPreloadedPlayable = null;
            _eventLogger.Report("Player", ex);
        }
    }

    private void ItemFinishedPlaying()
    {
        _isTriggerReinsertPlayableAllowed = true;
        _isPreviousPlaylableFinshed = true;
        if (_nextPreloadedPlayable is not null)
        {
            IsPlaying = true;
            StartTimers();
        }
        else
        {
            IsPlaying = false;
            StopTimers();
        }
        Responder?.DidItemFinishedPlaying();
    }

    private void HandleError(Exception error)
    {
        IsErrorOccurred = true;
        _wasPlayingBeforeErrorOccurred = IsPlaying;
        Pause();
        _nextPreloadedPlayable = null;
        _nextPreloadedUrl = "";
        _isPreviousPlaylableFinshed = true;
        ActiveStreamingBitrate = null;
        _perloadedStreamingBitrate = null;
        ActiveTranscodingFormat = null;
        _preloadTranscodingFormat = null;
        RestartPlayer();
        _eventLogger.Report("Player Status", error);
        Responder?.NotifyErrorOccurred(error);
        if (_isTriggerReinsertPlayableAllowed)
        {
            _isTriggerReinsertPlayableAllowed = false;
            TriggerReinsertPlayableCB?.Invoke();
        }
    }

    public void ContinuePlay()
    {
        IsPlaying = true;
        _player?.Resume();
        StartTimers();
        if (_player is not null) _player.Rate = (float)_userDefinedPlaybackRate.AsDouble();
    }

    public void Pause()
    {
        IsPlaying = false;
        _player?.Pause();
        StopTimers();
    }

    public void Stop()
    {
        IsPlaying = false;
        ClearPlayer();
    }

    public void SetPlaybackRate(PlaybackRate newValue)
    {
        _userDefinedPlaybackRate = newValue;
        if (_player is not null) _player.Rate = (float)newValue.AsDouble();
    }

    public void Seek(double toSecond)
    {
        if (_currentPlayUrl != "" && _player?.State is AudioStreamingPlayerState.Playing or AudioStreamingPlayerState.Paused)
        {
            _seekTimeWhenStarted = null;
            _player!.Seek(toSecond);
        }
        else
        {
            _seekTimeWhenStarted = toSecond;
        }
    }

    private void RestartPlayer()
    {
        if (_player is not null)
        {
            UnsubscribeEvents(_player);
            _player.Dispose();
        }
        _player = null;
        InitAudioStreamingPlayerAndNodes();
    }

    private void InitAudioStreamingPlayerAndNodes()
    {
        if (_player is not null) return;
        _player = _createAudioStreamingPlayerCB();
        _player.Volume = _volumePlayer;
        SubscribeEvents(_player);
        // Swift applied currentEqualizerSetting here even if the EQ was disabled (only relevant after a restart
        // caused by an error); the effective setting is applied instead.
        ApplyEqualizerSetting(_isEqualizerEnabled ? _currentEqualizerSetting : EqualizerSetting.Off);
        ApplyReplayGain();
        AmperfyLog.Debug_(Log, "Player setup completed with EQ and ReplayGain support");
    }

    private void SubscribeEvents(IAudioStreamingPlayer player)
    {
        player.DidStartPlaying += DidStartPlaying;
        player.DidFinishPlaying += OnDidFinishPlaying;
        player.UnexpectedError += OnUnexpectedError;
        player.DidCancel += OnDidCancel;
        player.DidReadMetadata += OnDidReadMetadata;
    }

    private void UnsubscribeEvents(IAudioStreamingPlayer player)
    {
        player.DidStartPlaying -= DidStartPlaying;
        player.DidFinishPlaying -= OnDidFinishPlaying;
        player.UnexpectedError -= OnUnexpectedError;
        player.DidCancel -= OnDidCancel;
        player.DidReadMetadata -= OnDidReadMetadata;
    }

    public bool ShouldPlaybackStart =>
        (!IsErrorOccurred && _isAutoStartPlayback) || (IsErrorOccurred && _wasPlayingBeforeErrorOccurred);

    public void RequestToPlay(AbstractPlayable playable, PlaybackRate playbackRate, bool autoStartPlayback)
    {
        _userDefinedPlaybackRate = playbackRate;
        if (_player is not null) _player.Rate = (float)_userDefinedPlaybackRate.AsDouble();
        _isAutoStartPlayback = autoStartPlayback;
        HandleRequest(playable);
    }

    private void HandleRequest(AbstractPlayable playable)
    {
        if (_isPreviousPlaylableFinshed && _nextPreloadedPlayable is { } nextPreloadedPlayable && ReferenceEquals(nextPreloadedPlayable, playable))
        {
            // Do nothing next preloaded playable has already been queued to player
            AmperfyLog.Info(Log, $"Play Preloaded: {nextPreloadedPlayable.DisplayString}");
            _currentPreparedUrl = "";
            _currentPlayUrl = _nextPreloadedUrl;
            PlayType = _perloadedPlayType;
            _perloadedPlayType = null;
            ActiveStreamingBitrate = _perloadedStreamingBitrate;
            _perloadedStreamingBitrate = null;
            ActiveTranscodingFormat = _preloadTranscodingFormat;
            _preloadTranscodingFormat = null;
            _isPreviousPlaylableFinshed = false;
            _currentReplayGainValue = nextPreloadedPlayable.ReplayGainTrackGain;
            ApplyReplayGain();
            _nextPreloadedPlayable = null;
            _nextPreloadedUrl = "";
            Responder?.NotifyItemPreparationFinished();
        }
        else if (playable.RelFilePath is { } relFilePath && CacheFileManager.Shared.FileExists(relFilePath))
        {
            _currentPlayUrl = "";
            _nextPreloadedPlayable = null;
            _nextPreloadedUrl = "";
            ActiveStreamingBitrate = null;
            _perloadedStreamingBitrate = null;
            ActiveTranscodingFormat = null;
            _preloadTranscodingFormat = null;
            if (!playable.IsPlayableOnWindows)
            {
                ReactToIncompatibleContentType(playable.FileContentType ?? "", playable.DisplayString);
                return;
            }
            _currentReplayGainValue = playable.ReplayGainTrackGain;
            ApplyReplayGain();
            InsertCachedPlayable(playable);
            IsPlaying = ShouldPlaybackStart;
            Responder?.NotifyItemPreparationFinished();
        }
        else if (!IsOfflineMode)
        {
            _currentPlayUrl = "";
            _nextPreloadedPlayable = null;
            _nextPreloadedUrl = "";
            ActiveStreamingBitrate = null;
            _perloadedStreamingBitrate = null;
            ActiveTranscodingFormat = null;
            _preloadTranscodingFormat = null;
            if (!(playable.IsPlayableOnWindows || StreamingTranscodings.IsTranscodingActive(_networkMonitor)))
            {
                ReactToIncompatibleContentType(playable.FileContentType ?? "", playable.DisplayString);
                return;
            }
            if (playable.AsRadio is { } radio)
            {
                // radios must have a valid URL
                if (!IsValidUrl(radio.Url))
                {
                    ReactToInvalidRadioUrl(playable.DisplayString);
                    return;
                }
            }
            _ = PlayStreamAsync(playable);
        }
        else
        {
            ClearPlayer();
            Responder?.NotifyItemPreparationFinished();
        }
    }

    private async Task PlayStreamAsync(AbstractPlayable playable)
    {
        await Task.Yield(); // Swift: Task { @MainActor in ... }
        try
        {
            _currentReplayGainValue = playable.ReplayGainTrackGain;
            ApplyReplayGain();
            await InsertStreamPlayableAsync(playable);
            IsPlaying = ShouldPlaybackStart;
            if (IsAutoCachePlayedItems && !playable.IsRadio && playable.Account?.Info is { } accountInfo)
                _getPlayableDownloaderCB(accountInfo).Download(playable);
            Responder?.NotifyItemPreparationFinished();
        }
        catch (Exception ex)
        {
            Responder?.NotifyErrorOccurred(ex);
            Responder?.NotifyItemPreparationFinished();
            _eventLogger.Report("Player", ex);
        }
    }

    private static bool IsValidUrl(string? urlString) =>
        !string.IsNullOrEmpty(urlString) && Uri.TryCreate(urlString, UriKind.RelativeOrAbsolute, out _);

    private void ReactToIncompatibleContentType(string contentType, string playableDisplayTitle)
    {
        ClearPlayer();
        _eventLogger.Info("Player Info", AmperfyLogStatusCode.PlayerError,
            $"Content type \"{contentType}\" of \"{playableDisplayTitle}\" is not playable via Amperfy. Activating transcoding in Settings could resolve this issue.",
            displayPopup: true);
        Responder?.NotifyItemPreparationFinished();
    }

    private void ReactToInvalidRadioUrl(string playableDisplayTitle)
    {
        ClearPlayer();
        _eventLogger.Info("Player Info", AmperfyLogStatusCode.PlayerError,
            $"Radio \"{playableDisplayTitle}\" has an invalid stream URL.", displayPopup: true);
        Responder?.NotifyItemPreparationFinished();
    }

    private void ClearPlayer()
    {
        _isPreviousPlaylableFinshed = true;
        _currentPreparedUrl = "";
        _currentPlayUrl = "";
        _nextPreloadedPlayable = null;
        _nextPreloadedUrl = "";
        PlayType = null;
        _perloadedPlayType = null;
        ActiveStreamingBitrate = null;
        _perloadedStreamingBitrate = null;
        ActiveTranscodingFormat = null;
        _preloadTranscodingFormat = null;
        _seekTimeWhenStarted = null;
        IsPlaying = false;

        StopTimers();
        _player?.Stop();
    }

    private void InsertCachedPlayable(AbstractPlayable playable, BackendAudioQueueType queueType = BackendAudioQueueType.Play)
    {
        if (_cacheProxy.GetFilePath(playable) is not { } filePath) return;
        var fileUrl = GetFileUrl(filePath);
        if (queueType == BackendAudioQueueType.Play)
        {
            PlayType = Player.PlayType.Cache;
            _perloadedPlayType = null;
            AmperfyLog.Info(Log, $"Play Cache: {playable.DisplayString} ({fileUrl})");
        }
        else
        {
            _perloadedPlayType = Player.PlayType.Cache;
            AmperfyLog.Info(Log, $"Insert Cache: {playable.DisplayString} ({fileUrl})");
        }
        if (playable.IsSong) _userStatistics.PlayedSong(isPlayedFromCache: true);
        Insert(playable, fileUrl, queueType);
    }

    private async Task InsertStreamPlayableAsync(AbstractPlayable playable, BackendAudioQueueType queueType = BackendAudioQueueType.Play)
    {
        var streamingMaxBitrate = StreamingMaxBitrates.GetActive(_networkMonitor);
        var streamingTranscodingFormat = StreamingTranscodings.GetActive(_networkMonitor);
        IReadOnlyDictionary<string, string>? httpHeaders = null;

        async Task<string> ProvideUrlAsync()
        {
            if (playable.AsRadio is { } radio)
            {
                if (!IsValidUrl(radio.Url)) throw BackendError.InvalidUrl;
                PlayType = Player.PlayType.Stream;
                return radio.Url!;
            }
            if (queueType == BackendAudioQueueType.Play)
            {
                PlayType = Player.PlayType.Stream;
                _perloadedPlayType = null;
                ActiveStreamingBitrate = streamingMaxBitrate;
                _perloadedStreamingBitrate = null;
                ActiveTranscodingFormat = streamingTranscodingFormat;
                _preloadTranscodingFormat = null;
            }
            else
            {
                _perloadedPlayType = Player.PlayType.Stream;
                _perloadedStreamingBitrate = streamingMaxBitrate;
                _preloadTranscodingFormat = streamingTranscodingFormat;
            }
            if (playable.Account?.Info is not { } accountInfo) throw BackendError.NoCredentials;
            var backendApi = _getBackendApiCB(accountInfo);
            httpHeaders = backendApi.HttpHeaders;
            var url = await backendApi.GenerateUrlForStreamingPlayableAsync(playable.Info, streamingMaxBitrate, streamingTranscodingFormat);
            return url.OriginalString;
        }

        var streamUrl = await ProvideUrlAsync();

        AmperfyLog.Info(Log, queueType == BackendAudioQueueType.Play
            ? $"Play Stream: {playable.DisplayString} ({streamingMaxBitrate.Description()})"
            : $"Insert Stream: {playable.DisplayString} ({streamingMaxBitrate.Description()})");
        if (playable.IsSong) _userStatistics.PlayedSong(isPlayedFromCache: false);
        Insert(playable, streamUrl, queueType, httpHeaders);
    }

    private void Insert(AbstractPlayable playable, string url, BackendAudioQueueType queueType, IReadOnlyDictionary<string, string>? httpHeaders = null)
    {
        if (queueType == BackendAudioQueueType.Play)
        {
            _seekTimeWhenStarted = null;
            _player?.Pause();
        }
        PlayInPlayer(url, playable.CompatibleContentType, queueType, httpHeaders);
    }

    private void PlayInPlayer(string url, string? mimeType, BackendAudioQueueType queueType, IReadOnlyDictionary<string, string>? httpHeaders)
    {
        var headers = httpHeaders is { Count: > 0 } ? httpHeaders : null;
        switch (queueType)
        {
            case BackendAudioQueueType.Play:
                _currentPreparedUrl = url;
                _player?.Play(url, mimeType, headers);
                break;
            case BackendAudioQueueType.Queue:
                _nextPreloadedUrl = url;
                _player?.Queue(url, mimeType, headers);
                break;
        }
    }

    // --- EQ -----------------------------------------------------------------------------------

    public void UpdateEqualizerEnabled(bool isEnabled)
    {
        _isEqualizerEnabled = isEnabled;
        AmperfyLog.Debug_(Log, $"Equalizer enabled: {isEnabled}");
        ApplyEqualizerToActiveContent();
    }

    public void UpdateEqualizerSetting(EqualizerSetting eqSetting)
    {
        var oldSetting = _currentEqualizerSetting;
        _currentEqualizerSetting = eqSetting;
        AmperfyLog.Debug_(Log, $"Equalizer changed from {oldSetting} to {eqSetting}");
        ApplyEqualizerToActiveContent();
    }

    private void ApplyEqualizerToActiveContent()
    {
        ApplyEqualizerSetting(_isEqualizerEnabled ? _currentEqualizerSetting : EqualizerSetting.Off);
        ApplyReplayGain();
    }

    private void ApplyEqualizerSetting(EqualizerSetting eqSetting)
    {
        if (_player is null) return;
        var gains = new float[EqualizerSetting.Frequencies.Length];
        for (var i = 0; i < gains.Length && i < eqSetting.Gains.Length; i++) gains[i] = eqSetting.Gains[i];
        _player.SetEqualizer(gains, _isEqualizerEnabled);

        _equalizerVolumeCompensation = _isEqualizerEnabled ? eqSetting.CompensatedVolume : 1.0f;

        AmperfyLog.Debug_(Log, $"   EQ '{eqSetting}'");
        AmperfyLog.Debug_(Log, $"   EQ Gains: [{string.Join(", ", eqSetting.Gains.Select(g => g.ToString("F1", CultureInfo.InvariantCulture)))}] dB");
        AmperfyLog.Debug_(Log, $"   EQ Gain Compensation: {eqSetting.GainCompensation:F1} dB");
        AmperfyLog.Debug_(Log, $"   EQ linear Volume Compensation: {eqSetting.CompensatedVolume:F2}");
        AmperfyLog.Debug_(Log, $"   Active EQ linear Volume Compensation: {_equalizerVolumeCompensation:F2}");
    }

    // --- ReplayGain ---------------------------------------------------------------------------

    public void UpdateReplayGainEnabled(bool isEnabled)
    {
        _isReplayGainEnabled = isEnabled;
        ApplyReplayGain();
    }

    /// Linear replay gain (incl. EQ compensation) for the given settings (Swift applyReplayGain()).
    public static float CalculateReplayGainVolume(bool isReplayGainEnabled, float replayGainDb, bool isEqualizerEnabled, float equalizerVolumeCompensation)
    {
        var eqCompensation = isEqualizerEnabled ? equalizerVolumeCompensation : 1.0f;
        if (isReplayGainEnabled && replayGainDb != 0.0f)
        {
            // Convert dB to linear scale: gain = pow(10, dB / 20)
            var linearGain = MathF.Pow(10.0f, replayGainDb / 20.0f);
            return linearGain * eqCompensation;
        }
        return eqCompensation;
    }

    private void ApplyReplayGain()
    {
        if (_player is null) return;
        var volume = CalculateReplayGainVolume(_isReplayGainEnabled, _currentReplayGainValue, _isEqualizerEnabled, _equalizerVolumeCompensation);
        ReplayGainOutputVolume = volume;
        _player.ReplayGainVolume = volume;
        AmperfyLog.Debug_(Log, _isReplayGainEnabled && _currentReplayGainValue != 0.0f
            ? $"ReplayGain: {_currentReplayGainValue:F2} dB -> {volume:F3} linear gain"
            : $"ReplayGain: disabled or no gain data ({volume:F2})");
    }

    // --- engine events (raised on the main thread) --------------------------------------------

    public void DidStartPlaying(string url)
    {
        if (_currentPreparedUrl != url) return;
        _currentPreparedUrl = "";
        _currentPlayUrl = url;
        if (ShouldPlaybackStart) ContinuePlay();
        else Pause();

        if (_seekTimeWhenStarted is { } seekTimeWhenStarted)
        {
            _player?.Seek(seekTimeWhenStarted);
            _seekTimeWhenStarted = null;
        }
    }

    private void OnDidFinishPlaying(string entryId)
    {
        if (_currentPlayUrl != entryId) return;
        _currentPlayUrl = "";
        ItemFinishedPlaying();
    }

    private void OnUnexpectedError(Exception error) => HandleError(error);

    private void OnDidCancel() => _currentPlayUrl = "";

    private void OnDidReadMetadata(IReadOnlyDictionary<string, string> metadata) => Responder?.DidReadStreamMetadata(metadata);
}
