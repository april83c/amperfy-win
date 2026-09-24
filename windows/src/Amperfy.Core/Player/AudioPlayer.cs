namespace Amperfy.Core.Player;

/// Port of AudioPlayer.swift: the player logic (play/pause/next/previous/repeat, notifications of the
/// observers). Main thread only.
public sealed class AudioPlayer : IBackendAudioPlayerNotifiable
{
    public const double ReplayInsteadPlayPreviousTimeInSec = 5.0;
    internal const double ProgressTimeStartThreshold = 15.0;
    internal const double ProgressTimeEndThreshold = 15.0;

    private readonly IPlayerStatusPersistent _playerStatus;
    private readonly PlayQueueHandler _queueHandler;
    private readonly BackendAudioPlayer _backendAudioPlayer;
    private readonly AmperfySettings _settings;
    private readonly UserStatistics _userStatistics;
    private List<WeakReference<IMusicPlayable>> _notifierList = [];
    private string? _lastRadioStreamTitle;

    public AbstractPlayable? CurrentlyPlaying => _queueHandler.CurrentlyPlaying;
    public AbstractPlayable? CurrentMusicItem => _queueHandler.CurrentMusicItem;
    public AbstractPlayable? CurrentPodcastItem => _queueHandler.CurrentPodcastItem;

    public bool IsShouldPauseAfterFinishedPlaying { get; set; }

    /// Returns similar songs for autoplay (Swift autoplayCB).
    public Func<Song, Task<List<Song>>>? AutoplayCB { get; set; }

    public RadioNowPlayingInfo? CurrentRadioNowPlaying { get; private set; }

    public AudioPlayer(IPlayerStatusPersistent coreData, PlayQueueHandler queueHandler, BackendAudioPlayer backendAudioPlayer,
        AmperfySettings settings, UserStatistics userStatistics)
    {
        _playerStatus = coreData;
        _queueHandler = queueHandler;
        _backendAudioPlayer = backendAudioPlayer;
        _backendAudioPlayer.IsAutoCachePlayedItems = coreData.IsAutoCachePlayedItems;
        _settings = settings;
        _userStatistics = userStatistics;
        _backendAudioPlayer.Responder = this;
        _backendAudioPlayer.NextPlayablePreloadCB = () =>
        {
            if (IsShouldPauseAfterFinishedPlaying) return null;
            if (_playerStatus.RepeatMode == RepeatMode.Single) return null;
            if (NextPlayerIndex is not { } nextPlayerIndex) return null;
            return _queueHandler.GetPlayable(nextPlayerIndex);
        };
    }

    private bool ShouldCurrentItemReplayedInsteadOfPrevious()
    {
        if (CurrentlyPlaying is { IsRadio: true }) return false;
        if (!_backendAudioPlayer.CanBeContinued) return false;
        return _backendAudioPlayer.ElapsedTime >= ReplayInsteadPlayPreviousTimeInSec;
    }

    private void ReplayCurrentItem()
    {
        AmperfyLog.Debug_("AudioPlayer", "Replay");
        if (CurrentlyPlaying is { } currentPlayable) InsertIntoPlayer(currentPlayable);
        NotifyItemStartedPlayingFromBeginning();
    }

    private void InsertIntoPlayer(AbstractPlayable playable)
    {
        _userStatistics.PlayedItem(_playerStatus.RepeatMode, _playerStatus.IsShuffle);
        playable.CountPlayed();
        _backendAudioPlayer.RequestToPlay(playable, _playerStatus.PlaybackRate, !_settings.User.IsPlaybackStartOnlyOnPlay);
    }

    // BackendAudioPlayerNotifiable
    public void NotifyItemPreparationFinished()
    {
        HandleRadioStartIfNeeded();
        NotifyItemStartedPlayingFromBeginning();
        NotifyItemStartedPlaying();
    }

    // BackendAudioPlayerNotifiable
    public void DidItemFinishedPlaying()
    {
        if (IsShouldPauseAfterFinishedPlaying)
        {
            IsShouldPauseAfterFinishedPlaying = false;
            Pause();
        }
        else if (_playerStatus.RepeatMode == RepeatMode.Single ||
                 // repeat mode all and only one song is in player -> repeat
                 (_playerStatus.RepeatMode == RepeatMode.All && _queueHandler.PrevQueueCount == 0 &&
                  _queueHandler.UserQueueCount == 0 && _queueHandler.NextQueueCount == 0))
        {
            ReplayCurrentItem();
        }
        else if (!_settings.User.IsPlaybackStartOnlyOnPlay)
        {
            PlayNext();
        }
    }

    public void Play()
    {
        if (!_backendAudioPlayer.CanBeContinued)
        {
            if (CurrentlyPlaying is { } currentPlayable) InsertIntoPlayer(currentPlayable);
        }
        else
        {
            _backendAudioPlayer.ContinuePlay();
            NotifyItemStartedPlaying();
        }
    }

    public void Play(PlayContext context)
    {
        if (context.GetActivePlayable() is not { } activePlayable) return;
        var topUserQueueItem = _queueHandler.GetUserQueueItem(0);
        var wasUserQueuePlaying = _queueHandler.IsUserQueuePlaying;
        _queueHandler.ClearActiveQueue();
        _queueHandler.AppendActiveQueue(context.Playables);
        if (context.Type == PlayerMode.Music) _queueHandler.SetContextName(context.Name);

        if (_queueHandler.IsUserQueuePlaying)
        {
            Play(new PlayerIndex(PlayerQueueType.Next, context.Index));
            if (!wasUserQueuePlaying && topUserQueueItem is not null)
                _queueHandler.InsertUserQueue([topUserQueueItem]);
        }
        else if (context.Index == 0)
        {
            InsertIntoPlayer(activePlayable);
        }
        else
        {
            Play(new PlayerIndex(PlayerQueueType.Next, context.Index - 1));
        }
    }

    public void Play(PlayerIndex playerIndex)
    {
        if (_queueHandler.MarkAndGetPlayableAsPlaying(playerIndex) is not { } playable)
        {
            Stop();
            return;
        }
        InsertIntoPlayer(playable);
    }

    public void PlayPreviousOrReplay()
    {
        if (ShouldCurrentItemReplayedInsteadOfPrevious()) ReplayCurrentItem();
        else PlayPrevious();
    }

    // BackendAudioPlayerNotifiable
    public void PlayPrevious()
    {
        if (_queueHandler.PrevQueueCount > 0)
            Play(new PlayerIndex(PlayerQueueType.Prev, _queueHandler.PrevQueueCount - 1));
        else if (_playerStatus.RepeatMode == RepeatMode.All && _queueHandler.NextQueueCount > 0)
            Play(new PlayerIndex(PlayerQueueType.Next, _queueHandler.NextQueueCount - 1));
        else
            ReplayCurrentItem();
    }

    // BackendAudioPlayerNotifiable
    public void PlayNext()
    {
        if (NextPlayerIndex is { } nextPlayerIndex)
        {
            Play(nextPlayerIndex);
        }
        else if (_settings.User.IsAutoplayEnabled && CurrentlyPlaying?.AsSong is { } song && AutoplayCB is { } cb &&
                 !_backendAudioPlayer.IsOfflineMode)
        {
            _ = AutoplayAsync(song, cb);
        }
        else
        {
            Stop();
        }
    }

    private async Task AutoplayAsync(Song song, Func<Song, Task<List<Song>>> cb)
    {
        await Task.Yield(); // Swift: Task { @MainActor ... }
        try
        {
            var similarSongs = await cb(song);
            if (similarSongs.Count == 0)
            {
                Stop();
                return;
            }
            _queueHandler.AppendContextQueue(similarSongs);
            Play(new PlayerIndex(PlayerQueueType.Next, 0));
        }
        catch (Exception)
        {
            Stop();
        }
    }

    private PlayerIndex? NextPlayerIndex
    {
        get
        {
            if (_queueHandler.UserQueueCount > 0) return new PlayerIndex(PlayerQueueType.User, 0);
            if (_queueHandler.NextQueueCount > 0) return new PlayerIndex(PlayerQueueType.Next, 0);
            if (_playerStatus.RepeatMode == RepeatMode.All && _queueHandler.PrevQueueCount > 0)
                return new PlayerIndex(PlayerQueueType.Prev, 0);
            return null;
        }
    }

    public void Pause()
    {
        if (CurrentlyPlaying is { IsRadio: true })
        {
            StopButRemainIndex();
        }
        else
        {
            _backendAudioPlayer.Pause();
            NotifyItemPaused();
        }
    }

    // BackendAudioPlayerNotifiable
    public void Stop()
    {
        _backendAudioPlayer.Stop();
        _playerStatus.Stop();
        NotifyPlayerStopped();
    }

    public void StopButRemainIndex()
    {
        _backendAudioPlayer.Stop();
        NotifyPlayerStopped();
    }

    public void TogglePlayPause()
    {
        if (_backendAudioPlayer.IsPlaying) Pause();
        else Play();
    }

    private void SeekToLastStoppedPlayTime()
    {
        if (CurrentlyPlaying is { } playable && playable.PlayProgress > 0 &&
            (playable.IsPodcastEpisode ||
             ((playable.IsSong || _backendAudioPlayer.IsErrorOccurred) && _settings.User.IsPlayerSongPlaybackResumeEnabled)))
        {
            _backendAudioPlayer.Seek(playable.PlayProgress);
        }
    }

    // BackendAudioPlayerNotifiable
    public void DidElapsedTimeChange()
    {
        NotifyElapsedTimeChanged();
        if (CurrentlyPlaying is { } currentItem) SavePlayInformation(currentItem);
    }

    // BackendAudioPlayerNotifiable
    public void DidLyricsTimeChange(TimeSpan time) => NotifyLyricsTimeChanged(time);

    // BackendAudioPlayerNotifiable
    public void DidReadStreamMetadata(IReadOnlyDictionary<string, string> metadata)
    {
        if (CurrentlyPlaying?.AsRadio is not { } radio) return;
        UpdateRadioNowPlaying(metadata, radio);
    }

    private void SavePlayInformation(AbstractPlayable playable)
    {
        var playDuration = _backendAudioPlayer.Duration;
        var playProgress = _backendAudioPlayer.ElapsedTime;
        if (playDuration != 0.0 && playProgress != 0.0 && ReferenceEquals(playable, CurrentlyPlaying))
        {
            playable.PlayDuration = (int)playDuration;
            if (playProgress > ProgressTimeStartThreshold && playProgress < playDuration - ProgressTimeEndThreshold)
                playable.PlayProgress = (int)playProgress;
            else
                playable.PlayProgress = 0;
        }
    }

    // --- notifiers ----------------------------------------------------------------------------

    public void AddNotifier(IMusicPlayable notifier) => _notifierList.Add(new WeakReference<IMusicPlayable>(notifier));

    public void RemoveAllNotifier() => _notifierList.Clear();

    private void ForEachNotifier(Action<IMusicPlayable> action)
    {
        _notifierList = _notifierList.Where(w => w.TryGetTarget(out _)).ToList();
        foreach (var weak in _notifierList.ToList())
        {
            if (weak.TryGetTarget(out var notifier)) action(notifier);
        }
    }

    public void NotifyItemStartedPlayingFromBeginning()
    {
        ForEachNotifier(n => n.DidStartPlayingFromBeginning());
        SeekToLastStoppedPlayTime();
    }

    public void NotifyItemStartedPlaying() => ForEachNotifier(n => n.DidStartPlaying());

    // BackendAudioPlayerNotifiable
    public void NotifyErrorOccurred(Exception error) => ForEachNotifier(n => n.ErrorOccurred(error));

    public void NotifyItemPaused() => ForEachNotifier(n => n.DidPause());

    public void NotifyPlayerStopped() => ForEachNotifier(n => n.DidStopPlaying());

    public void NotifyArtworkChanged() => ForEachNotifier(n => n.DidArtworkChange());

    public void NotifyElapsedTimeChanged() => ForEachNotifier(n => n.DidElapsedTimeChange());

    public void NotifyLyricsTimeChanged(TimeSpan time) => ForEachNotifier(n => n.DidLyricsTimeChange(time));

    public void NotifyPlaylistUpdated() => ForEachNotifier(n => n.DidPlaylistChange());

    public void NotifyNowPlayingInfoChanged() => ForEachNotifier(n => n.DidNowPlayingInfoChange());

    public void NotifyShuffleUpdated() => ForEachNotifier(n => n.DidShuffleChange());

    public void NotifyRepeatUpdated() => ForEachNotifier(n => n.DidRepeatChange());

    public void NotifyPlaybackRateUpdated() => ForEachNotifier(n => n.DidPlaybackRateChange());

    // --- radio now playing --------------------------------------------------------------------

    private void HandleRadioStartIfNeeded()
    {
        if (CurrentlyPlaying?.AsRadio is null)
        {
            if (CurrentRadioNowPlaying is not null)
            {
                CurrentRadioNowPlaying = null;
                _lastRadioStreamTitle = null;
                NotifyNowPlayingInfoChanged();
            }
            return;
        }

        CurrentRadioNowPlaying = null;
        _lastRadioStreamTitle = null;
        NotifyNowPlayingInfoChanged();
    }

    private void UpdateRadioNowPlaying(IReadOnlyDictionary<string, string> metadata, Radio radio)
    {
        if (CurrentlyPlaying?.AsRadio is not { } currentRadio || !ReferenceEquals(currentRadio, radio)) return;
        var streamTitleKey = metadata.Keys.FirstOrDefault(k => k.Equals("streamtitle", StringComparison.OrdinalIgnoreCase));
        if (streamTitleKey is null || !metadata.TryGetValue(streamTitleKey, out var rawStreamTitle)) return;
        var streamTitle = rawStreamTitle.Trim();
        if (streamTitle.Length == 0) return;
        if (streamTitle == _lastRadioStreamTitle) return;

        var parsedInfo = ParseStreamTitle(streamTitle);
        if (parsedInfo.IsEmpty) return;

        _lastRadioStreamTitle = streamTitle;
        CurrentRadioNowPlaying = parsedInfo;
        NotifyNowPlayingInfoChanged();
    }

    internal static RadioNowPlayingInfo ParseStreamTitle(string streamTitle)
    {
        var cleaned = streamTitle.Trim('"', '\'');
        var parts = cleaned.Split(" - ");
        if (parts.Length >= 2)
        {
            var artist = parts[0].Trim();
            var title = string.Join(" - ", parts.Skip(1)).Trim();
            return new RadioNowPlayingInfo(title, artist);
        }
        return new RadioNowPlayingInfo(cleaned, "");
    }
}
