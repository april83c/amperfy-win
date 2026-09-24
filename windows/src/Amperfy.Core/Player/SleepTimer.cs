namespace Amperfy.Core.Player;

/// Sleep timer option (port of the iOS sleep timer menu in AppDelegateMainMenuExtension.swift).
public sealed record SleepTimerOption(string Title, TimeSpan? Duration)
{
    /// "End of Song or Podcast Episode" (uses <see cref="IPlayerFacade.IsShouldPauseAfterFinishedPlaying"/>)
    public bool IsEndOfTrack => Duration is null;
}

/// Sleep timer: pauses playback after a duration or at the end of the current song/podcast episode.
/// The iOS app implemented it in the UI layer (AppDelegate.sleepTimer + player.isShouldPauseAfterFinishedPlaying).
/// Main thread only.
public sealed class SleepTimer : IDisposable
{
    public static readonly IReadOnlyList<SleepTimerOption> Options =
    [
        new("End of Song or Podcast Episode", null),
        new("5 Minutes", TimeSpan.FromMinutes(5)),
        new("10 Minutes", TimeSpan.FromMinutes(10)),
        new("15 Minutes", TimeSpan.FromMinutes(15)),
        new("30 Minutes", TimeSpan.FromMinutes(30)),
        new("45 Minutes", TimeSpan.FromMinutes(45)),
        new("1 Hour", TimeSpan.FromHours(1)),
        new("2 Hours", TimeSpan.FromHours(2)),
        new("4 Hours", TimeSpan.FromHours(4)),
    ];

    private readonly IPlayerFacade _player;
    private readonly EventLogger? _eventLogger;
    private IDisposable? _timer;

    /// One-shot timer factory (defaults to <see cref="MainThread.CreateTimer"/>); replaceable for tests.
    public Func<TimeSpan, Action, IDisposable> TimerFactory { get; set; } =
        (interval, tick) => MainThread.CreateTimer(interval, tick, repeats: false);

    /// Raised when the state changed (activated, deactivated or fired).
    public event Action? StateChanged;

    public SleepTimer(IPlayerFacade player, EventLogger? eventLogger)
    {
        _player = player;
        _eventLogger = eventLogger;
    }

    /// Time when the timer pauses playback (null if no duration timer is active).
    public DateTime? FireDate { get; private set; }

    public bool IsTimerActive => _timer is not null;

    public bool IsEndOfTrackActive => _player.IsShouldPauseAfterFinishedPlaying;

    public bool IsActive => IsTimerActive || IsEndOfTrackActive;

    /// Menu subtitle like on iOS ("Pause at: 22:15" / "Pause at end of Song").
    public string? StatusDescription
    {
        get
        {
            if (FireDate is { } fireDate) return $"Pause at: {fireDate.ToLocalTime():t}";
            if (IsEndOfTrackActive) return $"Pause at end of {_player.PlayerMode.PlayableName()}";
            return null;
        }
    }

    public void Activate(SleepTimerOption option)
    {
        if (option.Duration is { } duration) Activate(duration);
        else ActivateEndOfTrack();
    }

    public void ActivateEndOfTrack()
    {
        _player.IsShouldPauseAfterFinishedPlaying = true;
        StateChanged?.Invoke();
    }

    public void Activate(TimeSpan timeInterval)
    {
        InvalidateTimer();
        FireDate = DateTime.UtcNow + timeInterval;
        _timer = TimerFactory(timeInterval, Fire);
        StateChanged?.Invoke();
    }

    /// Turns the sleep timer off (both the duration timer and "end of track").
    public void Deactivate()
    {
        InvalidateTimer();
        _player.IsShouldPauseAfterFinishedPlaying = false;
        StateChanged?.Invoke();
    }

    internal void Fire()
    {
        _player.Pause();
        _eventLogger?.Info("Sleep Timer", "Sleep Timer paused playback.");
        InvalidateTimer();
        StateChanged?.Invoke();
    }

    private void InvalidateTimer()
    {
        _timer?.Dispose();
        _timer = null;
        FireDate = null;
    }

    public void Dispose() => InvalidateTimer();
}
