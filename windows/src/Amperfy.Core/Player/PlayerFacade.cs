namespace Amperfy.Core.Player;

/// Port of Swift StreamingMaxBitrates
public sealed record StreamingMaxBitrates(
    StreamingMaxBitratePreference Wifi = StreamingMaxBitratePreference.NoLimit,
    StreamingMaxBitratePreference Cellular = StreamingMaxBitratePreference.NoLimit)
{
    public StreamingMaxBitratePreference GetActive(INetworkMonitor networkMonitor) =>
        networkMonitor.IsWifiOrEthernet ? Wifi : Cellular;
}

/// Port of Swift StreamingTranscodings
public sealed record StreamingTranscodings(
    StreamingFormatPreference Wifi = StreamingFormatPreference.ServerConfig,
    StreamingFormatPreference Cellular = StreamingFormatPreference.ServerConfig)
{
    public StreamingFormatPreference GetActive(INetworkMonitor networkMonitor) =>
        networkMonitor.IsWifiOrEthernet ? Wifi : Cellular;

    public bool IsTranscodingActive(INetworkMonitor networkMonitor)
    {
        if (networkMonitor.IsCellular)
        {
            if (Cellular != StreamingFormatPreference.Raw) return true;
        }
        else
        {
            if (Wifi != StreamingFormatPreference.Raw) return true;
        }
        return false;
    }
}

/// Port of Swift PlayContext
public sealed class PlayContext
{
    public int Index { get; }
    public string Name { get; }
    public IReadOnlyList<AbstractPlayable> Playables { get; }
    public PlayerMode Type { get; }
    public bool IsKeepIndexDuringShuffle { get; set; }

    public PlayContext()
    {
        Name = "";
        Index = 0;
        Playables = [];
        Type = PlayerMode.Music;
    }

    public PlayContext(IPlayableContainable containable)
    {
        Name = containable.Name;
        Index = 0;
        Playables = containable.Playables;
        Type = containable.PlayContextType;
        containable.PlayedViaContext();
    }

    public PlayContext(string name, IReadOnlyList<AbstractPlayable> playables) : this(name, 0, playables) { }

    public PlayContext(string name, int index, IReadOnlyList<AbstractPlayable> playables)
    {
        Name = name;
        Index = index;
        Playables = playables;
        Type = PlayerMode.Music;
    }

    public PlayContext(string name, PlayerMode type, IReadOnlyList<AbstractPlayable> playables) : this(name, type, 0, playables) { }

    public PlayContext(string name, PlayerMode type, int index, IReadOnlyList<AbstractPlayable> playables)
    {
        Name = name;
        Index = index;
        Playables = playables;
        Type = type;
    }

    public PlayContext(IPlayableContainable containable, int index, IReadOnlyList<AbstractPlayable> playables)
    {
        Name = containable.Name;
        Index = index;
        Playables = playables;
        Type = containable.PlayContextType;
        containable.PlayedViaContext();
    }

    public AbstractPlayable? GetActivePlayable()
    {
        if (Playables.Count == 0 || Index >= Playables.Count || Index < 0) return null;
        return Playables[Index];
    }

    public PlayContext GetWithShuffledIndex()
    {
        if (IsKeepIndexDuringShuffle) return this;
        return new PlayContext(Name, Playables.Count < 2 ? 0 : Random.Shared.Next(0, Playables.Count), Playables);
    }
}

/// Port of the Swift PlayerFacade protocol (incl. its extension members).
public interface IPlayerFacade
{
    int PrevQueueCount { get; }
    List<AbstractPlayable> GetPrevQueueItems(int from, int? to);
    List<AbstractPlayable> GetAllPrevQueueItems();
    int UserQueueCount { get; }
    List<AbstractPlayable> GetUserQueueItems(int from, int? to);
    List<AbstractPlayable> GetAllUserQueueItems();
    int NextQueueCount { get; }
    List<AbstractPlayable> GetNextQueueItems(int from, int? to);
    List<AbstractPlayable> GetAllNextQueueItems();

    int TotalPlayDuration { get; }
    int RemainingPlayDuration { get; }
    float Volume { get; set; }
    bool IsPlaying { get; }
    AbstractPlayable? GetPlayable(PlayerIndex playerIndex);
    AbstractPlayable? CurrentlyPlaying { get; }
    AbstractPlayable? CurrentMusicItem { get; }
    AbstractPlayable? CurrentPodcastItem { get; }
    RadioNowPlayingInfo? CurrentRadioNowPlaying { get; }
    string ContextName { get; }
    double ElapsedTime { get; }
    double Duration { get; }
    bool IsShuffle { get; }
    void ToggleShuffle();
    PlaybackRate PlaybackRate { get; }
    void SetPlaybackRate(PlaybackRate newValue);
    RepeatMode RepeatMode { get; }
    void SetRepeatMode(RepeatMode newValue);
    bool IsOfflineMode { get; set; }
    bool IsShouldPauseAfterFinishedPlaying { get; set; }
    bool IsAutoCachePlayedItems { get; set; }
    bool IsPopupBarAllowedToHide { get; }
    int MusicItemCount { get; }
    int PodcastItemCount { get; }
    PlayerMode PlayerMode { get; }
    PlayType? PlayType { get; }
    StreamingMaxBitratePreference? ActiveStreamingBitrate { get; }
    StreamingFormatPreference? ActiveTranscodingFormat { get; }
    void SetPlayerMode(PlayerMode newValue);
    StreamingMaxBitrates StreamingMaxBitrates { get; }
    void SetStreamingMaxBitrates(StreamingMaxBitrates to);
    StreamingTranscodings StreamingTranscodings { get; }
    void SetStreamingTranscodings(StreamingTranscodings to);

    void Logout(Account account);

    void InsertContextQueue(IReadOnlyList<AbstractPlayable> playables);
    void AppendContextQueue(IReadOnlyList<AbstractPlayable> playables);
    void InsertUserQueue(IReadOnlyList<AbstractPlayable> playables);
    void AppendUserQueue(IReadOnlyList<AbstractPlayable> playables);
    void InsertPodcastQueue(IReadOnlyList<AbstractPlayable> playables);
    void AppendPodcastQueue(IReadOnlyList<AbstractPlayable> playables);
    void RemovePlayable(PlayerIndex at);
    void MovePlayable(PlayerIndex from, PlayerIndex to);
    void ClearUserQueue();
    void ClearContextQueue();
    void ClearQueues();

    void Play();
    void Play(PlayContext context);
    void PlayShuffled(PlayContext context);
    void Play(PlayerIndex playerIndex);
    void Pause();
    void TogglePlayPause();
    void Stop();
    void PlayPrevious();
    void PlayPreviousOrReplay();
    void PlayNext();
    void SkipForward(double interval);
    void SkipBackward(double interval);
    void Seek(double toSecond);

    void AddNotifier(IMusicPlayable notifier);

    void UpdateEqualizerEnabled(bool isEnabled);
    void UpdateEqualizerSetting(EqualizerSetting eqSetting);
    void UpdateReplayGainEnabled(bool isEnabled);

    // --- Swift PlayerFacade extension -----------------------------------------------------------

    int MaxSongsToAddOnce => 500;
    double SkipForwardPodcastInterval => 30.0;
    double SkipBackwardPodcastInterval => 15.0;
    double SkipForwardMusicInterval => 10.0;
    double SkipBackwardMusicInterval => 10.0;

    double SkipForwardInterval => PlayerMode == PlayerMode.Music ? SkipForwardMusicInterval : SkipForwardPodcastInterval;
    double SkipBackwardInterval => PlayerMode == PlayerMode.Music ? SkipBackwardMusicInterval : SkipBackwardPodcastInterval;

    bool IsSkipAvailable => CurrentlyPlaying is not { IsRadio: true };
    bool IsStopInsteadOfPause => CurrentlyPlaying is { IsRadio: true };
}

/// Port of Swift PlayerFacadeImpl
public sealed class PlayerFacadeImpl : IPlayerFacade
{
    private readonly IPlayerStatusPersistent _playerStatus;
    private readonly PlayQueueHandler _queueHandler;
    private readonly BackendAudioPlayer _backendAudioPlayer;
    private readonly AudioPlayer _musicPlayer;
    private readonly UserStatistics _userStatistics;

    public PlayerFacadeImpl(IPlayerStatusPersistent playerStatus, PlayQueueHandler queueHandler, AudioPlayer musicPlayer,
        LibraryStorage library, BackendAudioPlayer backendAudioPlayer, UserStatistics userStatistics)
    {
        _playerStatus = playerStatus;
        _queueHandler = queueHandler;
        _backendAudioPlayer = backendAudioPlayer;
        _musicPlayer = musicPlayer;
        _userStatistics = userStatistics;
    }

    public int PrevQueueCount => _queueHandler.PrevQueueCount;
    public List<AbstractPlayable> GetPrevQueueItems(int from, int? to) => _queueHandler.GetPrevQueueItems(from, to);
    public List<AbstractPlayable> GetAllPrevQueueItems() => _queueHandler.GetAllPrevQueueItems();
    public int UserQueueCount => _queueHandler.UserQueueCount;
    public List<AbstractPlayable> GetUserQueueItems(int from, int? to) => _queueHandler.GetUserQueueItems(from, to);
    public List<AbstractPlayable> GetAllUserQueueItems() => _queueHandler.GetAllUserQueueItems();
    public int NextQueueCount => _queueHandler.NextQueueCount;
    public List<AbstractPlayable> GetNextQueueItems(int from, int? to) => _queueHandler.GetNextQueueItems(from, to);
    public List<AbstractPlayable> GetAllNextQueueItems() => _queueHandler.GetAllNextQueueItems();

    public int TotalPlayDuration => _queueHandler.TotalPlayDuration;
    public int RemainingPlayDuration => _queueHandler.RemainingPlayDuration;

    public float Volume
    {
        get => _backendAudioPlayer.Volume;
        set => _backendAudioPlayer.Volume = value;
    }

    public bool IsPlaying => _backendAudioPlayer.IsPlaying;
    public PlayType? PlayType => _backendAudioPlayer.PlayType;
    public StreamingMaxBitratePreference? ActiveStreamingBitrate => _backendAudioPlayer.ActiveStreamingBitrate;
    public StreamingFormatPreference? ActiveTranscodingFormat => _backendAudioPlayer.ActiveTranscodingFormat;

    public AbstractPlayable? GetPlayable(PlayerIndex playerIndex) => _queueHandler.GetPlayable(playerIndex);

    public AbstractPlayable? CurrentlyPlaying => _musicPlayer.CurrentlyPlaying;
    public AbstractPlayable? CurrentMusicItem => _musicPlayer.CurrentMusicItem;
    public AbstractPlayable? CurrentPodcastItem => _musicPlayer.CurrentPodcastItem;
    public RadioNowPlayingInfo? CurrentRadioNowPlaying => _musicPlayer.CurrentRadioNowPlaying;

    public string ContextName
    {
        get
        {
            if (!string.IsNullOrEmpty(_queueHandler.ContextName)) return _queueHandler.ContextName;
            if (_queueHandler.PrevQueueCount == 0 && _queueHandler.NextQueueCount == 0 &&
                (_queueHandler.CurrentlyPlaying is null || _queueHandler.IsUserQueuePlaying))
                return "";
            return "Mixed Context";
        }
    }

    public double ElapsedTime => _backendAudioPlayer.ElapsedTime;
    public double Duration => _backendAudioPlayer.Duration;

    public bool IsShuffle => _playerStatus.IsShuffle;

    public void ToggleShuffle()
    {
        _playerStatus.SetShuffle(!IsShuffle);
        _musicPlayer.NotifyShuffleUpdated();
        _musicPlayer.NotifyPlaylistUpdated();
    }

    public PlaybackRate PlaybackRate => _playerStatus.PlaybackRate;

    public void SetPlaybackRate(PlaybackRate newValue)
    {
        _playerStatus.SetPlaybackRate(newValue);
        _backendAudioPlayer.SetPlaybackRate(newValue);
        _musicPlayer.NotifyPlaybackRateUpdated();
    }

    public RepeatMode RepeatMode => _playerStatus.RepeatMode;

    public void SetRepeatMode(RepeatMode newValue)
    {
        _playerStatus.SetRepeatMode(newValue);
        _musicPlayer.NotifyRepeatUpdated();
    }

    public bool IsOfflineMode
    {
        get => _backendAudioPlayer.IsOfflineMode;
        set => _backendAudioPlayer.IsOfflineMode = value;
    }

    public bool IsShouldPauseAfterFinishedPlaying
    {
        get => _musicPlayer.IsShouldPauseAfterFinishedPlaying;
        set => _musicPlayer.IsShouldPauseAfterFinishedPlaying = value;
    }

    public bool IsAutoCachePlayedItems
    {
        get => _playerStatus.IsAutoCachePlayedItems;
        set
        {
            _playerStatus.SetAutoCachePlayedItems(value);
            _backendAudioPlayer.IsAutoCachePlayedItems = value;
        }
    }

    public bool IsPopupBarAllowedToHide => _playerStatus.IsPopupBarAllowedToHide;
    public int MusicItemCount => _playerStatus.MusicItemCount;
    public int PodcastItemCount => _playerStatus.PodcastItemCount;
    public PlayerMode PlayerMode => _playerStatus.PlayerMode;

    public void SetPlayerMode(PlayerMode newValue)
    {
        _musicPlayer.StopButRemainIndex();
        _playerStatus.SetPlayerMode(newValue);
        _musicPlayer.NotifyPlaylistUpdated();
    }

    public StreamingMaxBitrates StreamingMaxBitrates => _backendAudioPlayer.StreamingMaxBitrates;
    public void SetStreamingMaxBitrates(StreamingMaxBitrates to) => _backendAudioPlayer.SetStreamingMaxBitrates(to);

    public StreamingTranscodings StreamingTranscodings => _backendAudioPlayer.StreamingTranscodings;
    public void SetStreamingTranscodings(StreamingTranscodings to) => _backendAudioPlayer.SetStreamingTranscodings(to);

    public void UpdateEqualizerEnabled(bool isEnabled) => _backendAudioPlayer.UpdateEqualizerEnabled(isEnabled);
    public void UpdateEqualizerSetting(EqualizerSetting eqSetting) => _backendAudioPlayer.UpdateEqualizerSetting(eqSetting);
    public void UpdateReplayGainEnabled(bool isEnabled) => _backendAudioPlayer.UpdateReplayGainEnabled(isEnabled);

    public void Logout(Account account)
    {
        if (_queueHandler.Logout(account)) Stop();
    }

    public void Seek(double toSecond)
    {
        // Swift: userStatistics.usedAction(.playerSeek) (action statistics are not tracked on Windows)
        if (CurrentlyPlaying is not { } currentlyPlaying) return;
        switch (currentlyPlaying.DerivedType)
        {
            case DerivedPlayableType.PodcastEpisode:
            case DerivedPlayableType.Song:
                _backendAudioPlayer.Seek(toSecond);
                break;
            case DerivedPlayableType.Radio:
                break; // do nothing
        }
    }

    public void InsertContextQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        _queueHandler.InsertContextQueue(playables);
        _musicPlayer.NotifyPlaylistUpdated();
    }

    public void AppendContextQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        _queueHandler.AppendContextQueue(playables);
        _musicPlayer.NotifyPlaylistUpdated();
    }

    public void InsertUserQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        _queueHandler.InsertUserQueue(playables);
        _musicPlayer.NotifyPlaylistUpdated();
    }

    public void AppendUserQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        _queueHandler.AppendUserQueue(playables);
        _musicPlayer.NotifyPlaylistUpdated();
    }

    public void InsertPodcastQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        _queueHandler.InsertPodcastQueue(playables);
        _musicPlayer.NotifyPlaylistUpdated();
    }

    public void AppendPodcastQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        _queueHandler.AppendPodcastQueue(playables);
        _musicPlayer.NotifyPlaylistUpdated();
    }

    public void RemovePlayable(PlayerIndex at) => _queueHandler.RemovePlayable(at);

    public void MovePlayable(PlayerIndex from, PlayerIndex to) => _queueHandler.MovePlayable(from, to);

    public void ClearUserQueue() => _queueHandler.ClearUserQueue();

    public void ClearContextQueue()
    {
        if (!_queueHandler.IsUserQueuePlaying)
        {
            if (_queueHandler.UserQueueCount == 0) _musicPlayer.Stop();
            else Play(new PlayerIndex(PlayerQueueType.User, 0));
        }
        _queueHandler.ClearContextQueue();
    }

    public void ClearQueues()
    {
        _musicPlayer.Stop();
        _queueHandler.ClearActiveQueue();
        switch (_playerStatus.PlayerMode)
        {
            case PlayerMode.Music:
                _queueHandler.ClearUserQueue();
                break;
            case PlayerMode.Podcast:
                break;
        }
        _musicPlayer.NotifyPlayerStopped();
    }

    public void Play() => _musicPlayer.Play();

    public void Play(PlayContext context)
    {
        SetPlayerModeForContextPlay(context.Type);
        if (PlayerMode == PlayerMode.Music && _playerStatus.IsShuffle)
        {
            _playerStatus.SetShuffle(false);
            _musicPlayer.NotifyShuffleUpdated();
        }
        _musicPlayer.Play(context);
        _musicPlayer.NotifyPlaylistUpdated();
    }

    private void SetPlayerModeForContextPlay(PlayerMode newValue)
    {
        _musicPlayer.Pause();
        _playerStatus.SetPlayerMode(newValue);
    }

    public void PlayShuffled(PlayContext context)
    {
        SetPlayerModeForContextPlay(context.Type);
        if (context.Playables.Count == 0) return;
        if (_playerStatus.IsShuffle) _playerStatus.SetShuffle(false);
        var shuffleContext = context.GetWithShuffledIndex();
        _musicPlayer.Play(shuffleContext);
        _playerStatus.SetShuffle(true);
        _musicPlayer.NotifyShuffleUpdated();
        _musicPlayer.NotifyPlaylistUpdated();
    }

    public void Play(PlayerIndex playerIndex) => _musicPlayer.Play(playerIndex);

    public void Pause() => _musicPlayer.Pause();

    public void TogglePlayPause() => _musicPlayer.TogglePlayPause();

    public void Stop() => _musicPlayer.Stop();

    public void PlayPrevious() => _musicPlayer.PlayPrevious();

    public void PlayPreviousOrReplay() => _musicPlayer.PlayPreviousOrReplay();

    public void PlayNext() => _musicPlayer.PlayNext();

    public void SkipForward(double interval) => Seek(ElapsedTime + interval);

    public void SkipBackward(double interval) => Seek(ElapsedTime - interval);

    public void AddNotifier(IMusicPlayable notifier) => _musicPlayer.AddNotifier(notifier);

    // Swift PlayerFacade extension members (also reachable through IPlayerFacade)
    public int MaxSongsToAddOnce => 500;
    public double SkipForwardPodcastInterval => 30.0;
    public double SkipBackwardPodcastInterval => 15.0;
    public double SkipForwardMusicInterval => 10.0;
    public double SkipBackwardMusicInterval => 10.0;
    public double SkipForwardInterval => PlayerMode == PlayerMode.Music ? SkipForwardMusicInterval : SkipForwardPodcastInterval;
    public double SkipBackwardInterval => PlayerMode == PlayerMode.Music ? SkipBackwardMusicInterval : SkipBackwardPodcastInterval;
    public bool IsSkipAvailable => CurrentlyPlaying is not { IsRadio: true };
    public bool IsStopInsteadOfPause => CurrentlyPlaying is { IsRadio: true };
}
