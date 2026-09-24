using Amperfy.Core.Api;

namespace Amperfy.Core.Player;

/// Port of RemoteCommandCenterHandler.swift (logic only): reacts to the remote commands of the system
/// media controls and enables/disables them based on the currently playing item.
public sealed class RemoteCommandHandler : IMusicPlayable
{
    private readonly IPlayerFacade _musicPlayer;
    private readonly Func<AccountInfo, ILibrarySyncer> _getLibrarySyncerCB;
    private readonly EventLogger _eventLogger;
    private readonly LibraryStorage _library;
    private readonly ISystemMediaControls _remoteCommandCenter;
    private bool _isLikeActive;

    public RemoteCommandHandler(IPlayerFacade musicPlayer, Func<AccountInfo, ILibrarySyncer> getLibrarySyncerCB, EventLogger eventLogger,
        LibraryStorage library, ISystemMediaControls remoteCommandCenter)
    {
        _musicPlayer = musicPlayer;
        _getLibrarySyncerCB = getLibrarySyncerCB;
        _eventLogger = eventLogger;
        _library = library;
        _remoteCommandCenter = remoteCommandCenter;
    }

    /// All commands enabled (Swift configureRemoteCommands initial state).
    public static SystemMediaCommandStates AllEnabled => new()
    {
        Play = true, Pause = true, TogglePlayPause = true, Stop = true, PreviousTrack = true, NextTrack = true,
        SkipBackward = true, SkipForward = true, ChangeShuffle = true, ChangeRepeat = true, Like = true,
        ChangePlaybackPosition = true, ChangePlaybackRate = true,
    };

    public void ConfigureRemoteCommands()
    {
        _remoteCommandCenter.PlayRequested += () => _musicPlayer.Play();
        _remoteCommandCenter.PauseRequested += () => _musicPlayer.Pause();
        _remoteCommandCenter.TogglePlayPauseRequested += () => _musicPlayer.TogglePlayPause();
        _remoteCommandCenter.StopRequested += () => _musicPlayer.Pause();
        _remoteCommandCenter.PreviousRequested += OnPrevious;
        _remoteCommandCenter.NextRequested += OnNext;
        _remoteCommandCenter.RepeatRequested += mode => _musicPlayer.SetRepeatMode(mode);
        _remoteCommandCenter.ShuffleRequested += OnShuffle;
        _remoteCommandCenter.SkipBackwardRequested += () => _musicPlayer.SkipBackward(_musicPlayer.SkipBackwardPodcastInterval);
        _remoteCommandCenter.SkipForwardRequested += () => _musicPlayer.SkipForward(_musicPlayer.SkipForwardPodcastInterval);
        _remoteCommandCenter.PlaybackRateRequested += rate => _musicPlayer.SetPlaybackRate(PlaybackRateExtensions.Create(rate));
        _remoteCommandCenter.SeekRequested += position => _musicPlayer.Seek(position);
        _remoteCommandCenter.RatingRequested += OnRating;
        _remoteCommandCenter.LikeRequested += OnLike;

        _remoteCommandCenter.ConfigureCapabilities(_musicPlayer.SkipBackwardPodcastInterval, _musicPlayer.SkipForwardPodcastInterval,
            Enum.GetValues<PlaybackRate>().Select(r => r.AsDouble()).ToList());
        _remoteCommandCenter.UpdateCommandStates(AllEnabled);
    }

    private void OnPrevious()
    {
        switch (_musicPlayer.PlayerMode)
        {
            case PlayerMode.Music:
                _musicPlayer.PlayPreviousOrReplay();
                break;
            case PlayerMode.Podcast:
                _musicPlayer.SkipBackward(_musicPlayer.SkipBackwardPodcastInterval);
                break;
        }
    }

    private void OnNext()
    {
        switch (_musicPlayer.PlayerMode)
        {
            case PlayerMode.Music:
                _musicPlayer.PlayNext();
                break;
            case PlayerMode.Podcast:
                _musicPlayer.SkipForward(_musicPlayer.SkipForwardPodcastInterval);
                break;
        }
    }

    private void OnShuffle(bool isShuffleRequested)
    {
        // Swift toggled on every shuffle command; the requested state is honored here (toggle only if it differs)
        if (isShuffleRequested != _musicPlayer.IsShuffle) _musicPlayer.ToggleShuffle();
        else UpdateShuffle();
    }

    /// Swift: deactivated ("#if false") on iOS; kept for platforms that offer a rating command.
    private void OnRating(int rating)
    {
        if (_musicPlayer.CurrentlyPlaying is not { IsRateable: true } currentItem || currentItem.AsSong is not { } song) return;
        _ = SetRatingAsync(song, rating);
    }

    private async Task SetRatingAsync(Song song, int rating)
    {
        try
        {
            if (song.Account?.Info is not { } accountInfo) return;
            song.Rating = rating;
            _library.SaveContext();
            await _getLibrarySyncerCB(accountInfo).SetRatingAsync(song, rating);
        }
        catch (Exception ex)
        {
            _eventLogger.Report("Song Rating Sync", ex);
        }
    }

    private void OnLike(bool isNegative)
    {
        if (_musicPlayer.CurrentlyPlaying is not { IsFavoritable: true } currentItem) return;
        if (isNegative != currentItem.IsFavorite)
        {
            _isLikeActive = currentItem.IsFavorite;
            UpdateCommandStatesForCurrentItem();
            return;
        }
        _isLikeActive = !isNegative;
        UpdateCommandStatesForCurrentItem();
        _ = ToggleFavoriteAsync(currentItem);
    }

    private async Task ToggleFavoriteAsync(AbstractPlayable currentItem)
    {
        try
        {
            if (currentItem.Account?.Info is { } accountInfo)
            {
                var librarySyncer = _getLibrarySyncerCB(accountInfo);
                await currentItem.RemoteToggleFavoriteAsync(_library, librarySyncer);
            }
        }
        catch (Exception ex)
        {
            _eventLogger.Report("Toggle Favorite", ex);
        }
    }

    public void ChangeRemoteCommandCenterControlsBasedOnCurrentPlayableType()
    {
        if (_musicPlayer.CurrentlyPlaying is not { } currentItem) return;
        _isLikeActive = currentItem.DerivedType == DerivedPlayableType.Song && currentItem.IsFavorite;
        UpdateCommandStatesForCurrentItem();
        UpdateShuffle();
        UpdateRepeat();
    }

    /// Command states for the given playable type (Swift changeRemoteCommandCenterControlsBasedOnCurrentPlayableType).
    public static SystemMediaCommandStates GetCommandStates(DerivedPlayableType type, bool isLikeActive) => type switch
    {
        DerivedPlayableType.Song => new SystemMediaCommandStates
        {
            Play = true, Pause = true, TogglePlayPause = true, Stop = false, PreviousTrack = true, NextTrack = true,
            SkipBackward = false, SkipForward = false, ChangeShuffle = true, ChangeRepeat = true, Like = true,
            IsLikeActive = isLikeActive, ChangePlaybackPosition = true, ChangePlaybackRate = true,
        },
        DerivedPlayableType.PodcastEpisode => new SystemMediaCommandStates
        {
            Play = true, Pause = true, TogglePlayPause = true, Stop = false, PreviousTrack = false, NextTrack = false,
            SkipBackward = true, SkipForward = true, ChangeShuffle = false, ChangeRepeat = false, Like = false,
            IsLikeActive = false, ChangePlaybackPosition = true, ChangePlaybackRate = true,
        },
        _ => new SystemMediaCommandStates
        {
            Play = true, Pause = false, TogglePlayPause = false, Stop = true, PreviousTrack = true, NextTrack = true,
            SkipBackward = false, SkipForward = false, ChangeShuffle = true, ChangeRepeat = true, Like = false,
            IsLikeActive = false, ChangePlaybackPosition = false, ChangePlaybackRate = false,
        },
    };

    private void UpdateCommandStatesForCurrentItem()
    {
        if (_musicPlayer.CurrentlyPlaying is not { } currentItem) return;
        _remoteCommandCenter.UpdateCommandStates(GetCommandStates(currentItem.DerivedType, _isLikeActive));
    }

    private void UpdateShuffle() => _remoteCommandCenter.UpdateShuffle(_musicPlayer.IsShuffle);

    private void UpdateRepeat() => _remoteCommandCenter.UpdateRepeat(_musicPlayer.RepeatMode);

    // --- MusicPlayable ------------------------------------------------------------------------

    public void DidStartPlaying() => ChangeRemoteCommandCenterControlsBasedOnCurrentPlayableType();

    public void DidShuffleChange() => UpdateShuffle();

    public void DidRepeatChange() => UpdateRepeat();
}
