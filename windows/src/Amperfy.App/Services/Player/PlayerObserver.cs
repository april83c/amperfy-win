using Amperfy.Core.Player;

namespace Amperfy.App.Services.Player;

/// Forwards the player notifications (<see cref="IMusicPlayable"/>) as events. The player keeps its observers as
/// weak references: the owning view keeps the observer in a field and sets <see cref="IsActive"/> to false while it
/// isn't loaded. Use <see cref="Register"/> once per observer.
public sealed class PlayerObserver : IMusicPlayable
{
    private bool _isRegistered;

    /// Events are only raised while active.
    public bool IsActive { get; set; } = true;

    public event Action? StartedPlayingFromBeginning;
    public event Action? StartedPlaying;
    public event Action? Paused;
    public event Action? Stopped;
    public event Action? ElapsedTimeChanged;
    public event Action<TimeSpan>? LyricsTimeChanged;
    public event Action? PlaylistChanged;
    public event Action? ArtworkChanged;
    public event Action? NowPlayingInfoChanged;
    public event Action? ShuffleChanged;
    public event Action? RepeatChanged;
    public event Action? PlaybackRateChanged;
    public event Action<Exception>? Error;

    /// Raised for every notification except the high frequency time updates (a simple "refresh all" hook).
    public event Action? AnyChanged;

    /// Adds the observer to the player (once).
    public PlayerObserver Register()
    {
        if (_isRegistered) return this;
        _isRegistered = true;
        AppServices.Instance.Player.AddNotifier(this);
        return this;
    }

    private void Raise(Action? handler, bool isStateChange = true)
    {
        if (!IsActive) return;
        handler?.Invoke();
        if (isStateChange) AnyChanged?.Invoke();
    }

    void IMusicPlayable.DidStartPlayingFromBeginning() => Raise(StartedPlayingFromBeginning);
    void IMusicPlayable.DidStartPlaying() => Raise(StartedPlaying);
    void IMusicPlayable.DidPause() => Raise(Paused);
    void IMusicPlayable.DidStopPlaying() => Raise(Stopped);
    void IMusicPlayable.DidElapsedTimeChange() => Raise(ElapsedTimeChanged, isStateChange: false);

    void IMusicPlayable.DidLyricsTimeChange(TimeSpan time)
    {
        if (IsActive) LyricsTimeChanged?.Invoke(time);
    }

    void IMusicPlayable.DidPlaylistChange() => Raise(PlaylistChanged);
    void IMusicPlayable.DidArtworkChange() => Raise(ArtworkChanged);
    void IMusicPlayable.DidNowPlayingInfoChange() => Raise(NowPlayingInfoChanged);
    void IMusicPlayable.DidShuffleChange() => Raise(ShuffleChanged);
    void IMusicPlayable.DidRepeatChange() => Raise(RepeatChanged);
    void IMusicPlayable.DidPlaybackRateChange() => Raise(PlaybackRateChanged);

    void IMusicPlayable.ErrorOccurred(Exception error)
    {
        if (!IsActive) return;
        Error?.Invoke(error);
        AnyChanged?.Invoke();
    }
}
