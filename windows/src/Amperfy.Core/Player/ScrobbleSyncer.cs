using Amperfy.Core.Api;

namespace Amperfy.Core.Player;

/// Port of ScrobbleSyncer.swift: reports "now playing" and scrobbles songs that were listened to long
/// enough (4 min or 50% of the duration). Offline scrobbles are cached as ScrobbleEntry and uploaded later.
public sealed class ScrobbleSyncer : IMusicPlayable
{
    private const string Log = "ScrobbleSyncer";
    /// scrobble at 4 min or 50% of duration
    private const double MaximumWaitDurationInSec = 240;

    private readonly IPlayerFacade _player;
    private readonly INetworkMonitor _networkMonitor;
    private readonly Account _account;
    private readonly LibraryStorage _library;
    private readonly AmperfySettings _settings;
    private readonly ILibrarySyncer _librarySyncer;
    private readonly EventLogger _eventLogger;
    private bool _isRunning;
    private bool _isActive;
    private IDisposable? _scrobbleTimer;

    // Track how long the song has actually been played
    private double _accumulatedPlayTime;
    private DateTime? _playStartTimestamp;
    private double _currentSongThreshold;

    private Song? _songToBeScrobbled;
    private bool _songHasBeenListendEnough;

    /// One-shot timer factory (defaults to <see cref="MainThread.CreateTimer"/>); replaceable for tests.
    public Func<TimeSpan, Action, IDisposable> OneShotTimerFactory { get; set; } =
        (interval, tick) => MainThread.CreateTimer(interval, tick, repeats: false);

    public ScrobbleSyncer(IPlayerFacade player, INetworkMonitor networkMonitor, Account account, LibraryStorage library,
        AmperfySettings settings, ILibrarySyncer librarySyncer, EventLogger eventLogger)
    {
        _player = player;
        _networkMonitor = networkMonitor;
        _account = account;
        _library = library;
        _settings = settings;
        _librarySyncer = librarySyncer;
        _eventLogger = eventLogger;
    }

    public void Start()
    {
        if (_library.GetUploadableScrobbleEntryCount(_account) <= 0) return;
        _isRunning = true;
        if (!_isActive)
        {
            _isActive = true;
            _ = UploadInBackgroundAsync();
        }
    }

    public void Stop() => _isRunning = false;

    private void Scrobble(Song playedSong, NowPlayingSongPosition songPosition)
    {
        switch (songPosition)
        {
            case NowPlayingSongPosition.Start:
                if (_settings.User.IsOnlineMode && _networkMonitor.IsConnectedToNetwork)
                    _ = NowPlayingToServerAsync(playedSong, NowPlayingSongPosition.Start, null);
                break;
            case NowPlayingSongPosition.End:
                if (_settings.User.IsOnlineMode && _networkMonitor.IsConnectedToNetwork)
                {
                    _ = NowPlayingToServerAsync(playedSong, NowPlayingSongPosition.End, (song, success) =>
                    {
                        CacheScrobbleRequest(song, success);
                        if (!success) return;
                        Start(); // send cached request to server
                    });
                }
                else
                {
                    CacheScrobbleRequest(playedSong, isUploaded: false);
                }
                break;
        }
    }

    private async Task NowPlayingToServerAsync(Song playedSong, NowPlayingSongPosition songPosition, Action<Song, bool>? finallyCB)
    {
        await Task.Yield();
        var success = false;
        try
        {
            await _librarySyncer.SyncNowPlayingAsync(playedSong, songPosition);
            success = true;
        }
        catch (Exception ex)
        {
            AmperfyLog.Info(Log, $"Now Playing Sync Failed: {playedSong.DisplayString}");
            _eventLogger.Report("Scrobble Sync", ex, displayPopup: false);
        }
        finallyCB?.Invoke(playedSong, success);
    }

    private async Task UploadInBackgroundAsync()
    {
        await Task.Yield();
        AmperfyLog.Info(Log, "start");
        while (_isRunning && _settings.User.IsOnlineMode && _networkMonitor.IsConnectedToNetwork)
        {
            try
            {
                var entry = _library.GetFirstUploadableScrobbleEntry(_account);
                if (entry is null)
                {
                    _isRunning = false;
                    continue;
                }
                if (entry.Playable?.AsSong is not { } song || entry.Date is not { } date)
                {
                    entry.IsUploaded = true;
                    _library.SaveContext();
                    continue;
                }
                await _librarySyncer.ScrobbleAsync(song, date);
                entry.IsUploaded = true;
                _library.SaveContext();
            }
            catch (Exception ex)
            {
                _isRunning = false;
                _eventLogger.Report("Scrobble Sync", ex, displayPopup: false);
            }
        }
        AmperfyLog.Info(Log, "stopped");
        _isActive = false;
    }

    private void CacheScrobbleRequest(Song playedSong, bool isUploaded)
    {
        if (!isUploaded) AmperfyLog.Info(Log, $"Scrobble cache: {playedSong.DisplayString}");
        var scrobbleEntry = _library.CreateScrobbleEntry(_account);
        scrobbleEntry.Date = DateTime.UtcNow;
        scrobbleEntry.Playable = playedSong;
        scrobbleEntry.IsUploaded = isUploaded;
        _library.SaveContext();
    }

    private bool IsOwnSong(AbstractPlayable? playable, out Song song)
    {
        song = null!;
        if (playable?.AsSong is not { } s) return false;
        if (!(ReferenceEquals(s.Account, _account) || (s.Account is not null && s.Account.Pk == _account.Pk))) return false;
        song = s;
        return true;
    }

    private async Task StartSongPlayedAsync()
    {
        await Task.Yield();
        SyncSongStopped(clearCurPlaying: true);

        if (!IsOwnSong(_player.CurrentlyPlaying, out var curPlayingSong)) return;

        _songToBeScrobbled = curPlayingSong;

        // Reset tracking variables
        _accumulatedPlayTime = 0;
        _playStartTimestamp = DateTime.UtcNow;

        // Calculate threshold time (half of duration or maximum)
        var waitDuration = curPlayingSong.Duration / 2.0;
        _currentSongThreshold = waitDuration > MaximumWaitDurationInSec ? MaximumWaitDurationInSec : waitDuration;

        StartScrobbleTimer(curPlayingSong, _currentSongThreshold);
    }

    private void StartScrobbleTimer(Song song, double duration)
    {
        _scrobbleTimer?.Dispose();
        _scrobbleTimer = OneShotTimerFactory(TimeSpan.FromSeconds(Math.Max(0, duration)), () =>
        {
            if (!ReferenceEquals(song, _player.CurrentlyPlaying)) return;
            if (!(_player.PlayType == PlayType.Cache || _settings.Accounts.GetSetting(_account.Info).IsScrobbleStreamedItems)) return;
            _songHasBeenListendEnough = true;
        });
    }

    private void SyncSongStopped(bool clearCurPlaying)
    {
        void ClearingCurPlaying()
        {
            _songHasBeenListendEnough = false;
            _scrobbleTimer?.Dispose();
            _scrobbleTimer = null;
            _songToBeScrobbled = null;
            _accumulatedPlayTime = 0;
            _playStartTimestamp = null;
            _currentSongThreshold = 0;
        }

        if (_songToBeScrobbled is { } oldSong && _songHasBeenListendEnough)
        {
            Scrobble(oldSong, NowPlayingSongPosition.End);
            ClearingCurPlaying();
        }
        else if (clearCurPlaying)
        {
            ClearingCurPlaying();
        }
    }

    // --- MusicPlayable ------------------------------------------------------------------------

    public void DidStartPlayingFromBeginning() => _ = StartSongPlayedAsync();

    public void DidStartPlaying()
    {
        if (!IsOwnSong(_player.CurrentlyPlaying, out var curPlayingSong)) return;

        // Check if it's still the same song after a pause
        if (_songToBeScrobbled is { } songToBeScrobbled && ReferenceEquals(songToBeScrobbled, curPlayingSong))
        {
            // Record when we started playing again
            _playStartTimestamp = DateTime.UtcNow;

            // Only restart the timer if we haven't already hit the threshold
            if (!_songHasBeenListendEnough)
            {
                // Calculate remaining time needed
                var remainingTime = _currentSongThreshold - _accumulatedPlayTime;
                if (remainingTime > 0)
                    StartScrobbleTimer(curPlayingSong, remainingTime); // Start a new timer with just the remaining time needed
                else
                    _songHasBeenListendEnough = true; // Threshold met, mark it immediately
            }
        }

        _ = ScrobbleStartAsync(curPlayingSong);
    }

    private async Task ScrobbleStartAsync(Song song)
    {
        await Task.Yield();
        Scrobble(song, NowPlayingSongPosition.Start);
    }

    public void DidPause()
    {
        // Update the accumulated play time when paused
        if (_playStartTimestamp is { } startTime)
        {
            _accumulatedPlayTime += (DateTime.UtcNow - startTime).TotalSeconds;
            _playStartTimestamp = null;
        }

        // Cancel the current timer
        _scrobbleTimer?.Dispose();
        _scrobbleTimer = null;

        _ = SyncSongStoppedAsync(clearCurPlaying: false);
    }

    public void DidStopPlaying() => _ = SyncSongStoppedAsync(clearCurPlaying: true);

    private async Task SyncSongStoppedAsync(bool clearCurPlaying)
    {
        await Task.Yield();
        SyncSongStopped(clearCurPlaying);
    }
}
