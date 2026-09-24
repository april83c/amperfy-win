namespace Amperfy.Core.Player;

public enum SystemMediaPlaybackStatus
{
    Stopped,
    Playing,
    Paused,
}

/// Now playing information (iOS: MPNowPlayingInfoCenter.nowPlayingInfo).
public sealed record NowPlayingMetadata(
    string Title,
    string Artist,
    string AlbumTitle,
    // Absolute path of the artwork image to display (null: app shows its placeholder)
    string? ArtworkPath,
    // seconds
    double Duration,
    // seconds
    double ElapsedTime,
    double PlaybackRate,
    bool IsLiveStream,
    // true if the item is streamed (not cached)
    bool IsCloudItem,
    DerivedPlayableType PlayableType);

/// Which remote commands are currently available (iOS: MPRemoteCommand.isEnabled / isActive).
public sealed record SystemMediaCommandStates
{
    public bool Play { get; init; }
    public bool Pause { get; init; }
    public bool TogglePlayPause { get; init; }
    public bool Stop { get; init; }
    public bool PreviousTrack { get; init; }
    public bool NextTrack { get; init; }
    public bool SkipBackward { get; init; }
    public bool SkipForward { get; init; }
    public bool ChangeShuffle { get; init; }
    public bool ChangeRepeat { get; init; }
    public bool Like { get; init; }
    /// "like" button state (current item is a favorite)
    public bool IsLikeActive { get; init; }
    public bool ChangePlaybackPosition { get; init; }
    public bool ChangePlaybackRate { get; init; }
}

/// Platform system media controls (iOS: MPNowPlayingInfoCenter + MPRemoteCommandCenter,
/// Windows: SystemMediaTransportControls). Implemented by the app. Members are called on the main thread
/// and the implementation must raise the events on the main thread.
public interface ISystemMediaControls
{
    /// Updates the displayed metadata (null clears it).
    void UpdateNowPlaying(NowPlayingMetadata? metadata);
    void UpdatePlaybackStatus(SystemMediaPlaybackStatus status);
    void UpdateShuffle(bool isShuffle);
    void UpdateRepeat(RepeatMode repeatMode);
    void UpdateCommandStates(SystemMediaCommandStates states);
    /// Supported skip intervals (seconds) and playback rates.
    void ConfigureCapabilities(double skipBackwardInterval, double skipForwardInterval, IReadOnlyList<double> supportedPlaybackRates);

    event Action? PlayRequested;
    event Action? PauseRequested;
    event Action? TogglePlayPauseRequested;
    event Action? StopRequested;
    event Action? PreviousRequested;
    event Action? NextRequested;
    event Action? SkipBackwardRequested;
    event Action? SkipForwardRequested;
    /// requested position in seconds
    event Action<double>? SeekRequested;
    /// requested shuffle state
    event Action<bool>? ShuffleRequested;
    event Action<RepeatMode>? RepeatRequested;
    event Action<double>? PlaybackRateRequested;
    /// rating 0...5
    event Action<int>? RatingRequested;
    /// isNegative: true = "dislike"/remove favorite, false = like
    event Action<bool>? LikeRequested;
}
