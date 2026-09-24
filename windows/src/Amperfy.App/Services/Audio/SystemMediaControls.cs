using Amperfy.Core.Common;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Amperfy.App.Services.Audio;

/// <see cref="ISystemMediaControls"/> on the Windows SystemMediaTransportControls (media keys, the media flyout of
/// the volume/quick settings, lock screen). Unpackaged desktop apps have no CoreWindow, so the SMTC instance of a
/// dedicated (never playing) MediaPlayer is used with its CommandManager disabled (manual SMTC control).
/// Button presses arrive on a background thread and are posted to the main thread.
public sealed class SystemMediaControls : ISystemMediaControls, IDisposable
{
    private const string Log = "SystemMediaControls";

    private readonly MediaPlayer _hostPlayer;
    private readonly SystemMediaTransportControls _smtc;
    private NowPlayingMetadata? _displayed;
    private string? _displayedArtworkPath;
    private int _thumbnailRequest;
    private DateTime _lastTimelineUpdate = DateTime.MinValue;
    private double _lastTimelinePosition = -1;
    private double _lastTimelineDuration = -1;
    private bool _isDisposed;

    public SystemMediaControls()
    {
        _hostPlayer = new MediaPlayer();
        _hostPlayer.CommandManager.IsEnabled = false;
        _smtc = _hostPlayer.SystemMediaTransportControls;
        _smtc.IsEnabled = true;
        _smtc.IsPlayEnabled = true;
        _smtc.IsPauseEnabled = true;
        _smtc.IsStopEnabled = true;
        _smtc.IsNextEnabled = true;
        _smtc.IsPreviousEnabled = true;
        _smtc.IsFastForwardEnabled = false;
        _smtc.IsRewindEnabled = false;
        _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
        _smtc.ButtonPressed += Smtc_ButtonPressed;
        _smtc.PlaybackPositionChangeRequested += Smtc_PlaybackPositionChangeRequested;
        _smtc.PlaybackRateChangeRequested += Smtc_PlaybackRateChangeRequested;
        _smtc.ShuffleEnabledChangeRequested += Smtc_ShuffleEnabledChangeRequested;
        _smtc.AutoRepeatModeChangeRequested += Smtc_AutoRepeatModeChangeRequested;
    }

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
#pragma warning disable CS0067 // no rating/like buttons in the Windows SMTC
    public event Action<int>? RatingRequested;
    public event Action<bool>? LikeRequested;
#pragma warning restore CS0067

    // --- updates from the player --------------------------------------------------------------

    public void UpdateNowPlaying(NowPlayingMetadata? metadata)
    {
        if (_isDisposed) return;
        try
        {
            if (metadata is null)
            {
                _displayed = null;
                _displayedArtworkPath = null;
                _thumbnailRequest++;
                _smtc.DisplayUpdater.ClearAll();
                _smtc.DisplayUpdater.Update();
                UpdateTimeline(0, 0, force: true);
                return;
            }
            if (!IsSameDisplay(_displayed, metadata))
            {
                _displayed = metadata;
                var updater = _smtc.DisplayUpdater;
                updater.Type = MediaPlaybackType.Music;
                updater.MusicProperties.Title = metadata.Title;
                updater.MusicProperties.Artist = metadata.Artist;
                updater.MusicProperties.AlbumTitle = metadata.AlbumTitle;
                updater.MusicProperties.AlbumArtist = metadata.PlayableType == DerivedPlayableType.Song ? metadata.Artist : "";
                updater.Update();
                if (metadata.ArtworkPath != _displayedArtworkPath || updater.Thumbnail is null)
                {
                    _displayedArtworkPath = metadata.ArtworkPath;
                    _ = UpdateThumbnailAsync(metadata.ArtworkPath);
                }
            }
            _smtc.IsNextEnabled = true;
            UpdateTimeline(metadata.IsLiveStream ? 0 : metadata.ElapsedTime, metadata.IsLiveStream ? 0 : metadata.Duration);
            if (Math.Abs(_smtc.PlaybackRate - metadata.PlaybackRate) > 0.001 && metadata.PlaybackRate > 0) _smtc.PlaybackRate = metadata.PlaybackRate;
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(Log, $"Updating now playing failed: {ex.Message}");
        }
    }

    private static bool IsSameDisplay(NowPlayingMetadata? a, NowPlayingMetadata b) =>
        a is not null && a.Title == b.Title && a.Artist == b.Artist && a.AlbumTitle == b.AlbumTitle &&
        a.ArtworkPath == b.ArtworkPath && a.PlayableType == b.PlayableType;

    private async Task UpdateThumbnailAsync(string? artworkPath)
    {
        var request = ++_thumbnailRequest;
        RandomAccessStreamReference? reference = null;
        try
        {
            var path = artworkPath is not null && File.Exists(artworkPath)
                ? artworkPath
                : Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png");
            if (File.Exists(path))
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                reference = RandomAccessStreamReference.CreateFromFile(file);
            }
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(Log, $"Loading the artwork failed: {ex.Message}");
        }
        if (_isDisposed || request != _thumbnailRequest || _displayed is null) return;
        try
        {
            _smtc.DisplayUpdater.Thumbnail = reference;
            _smtc.DisplayUpdater.Update();
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(Log, $"Updating the artwork failed: {ex.Message}");
        }
    }

    private void UpdateTimeline(double position, double duration, bool force = false)
    {
        if (!double.IsFinite(position) || position < 0) position = 0;
        if (!double.IsFinite(duration) || duration < 0) duration = 0;
        // NowPlayingInfoHandler updates every second: only push changes (or a periodic resync)
        var now = DateTime.UtcNow;
        var expected = _lastTimelinePosition + (now - _lastTimelineUpdate).TotalSeconds * Math.Max(_smtc.PlaybackRate, 0.1);
        var isPlaying = _smtc.PlaybackStatus == MediaPlaybackStatus.Playing;
        if (!force && Math.Abs(duration - _lastTimelineDuration) < 0.5 &&
            (isPlaying ? Math.Abs(position - expected) < 1.5 : Math.Abs(position - _lastTimelinePosition) < 0.5) &&
            now - _lastTimelineUpdate < TimeSpan.FromSeconds(10))
        {
            return;
        }
        _lastTimelineUpdate = now;
        _lastTimelinePosition = position;
        _lastTimelineDuration = duration;
        var timeline = new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            MinSeekTime = TimeSpan.Zero,
            Position = TimeSpan.FromSeconds(Math.Min(position, duration > 0 ? duration : position)),
            MaxSeekTime = TimeSpan.FromSeconds(duration),
            EndTime = TimeSpan.FromSeconds(duration),
        };
        _smtc.UpdateTimelineProperties(timeline);
    }

    public void UpdatePlaybackStatus(SystemMediaPlaybackStatus status)
    {
        if (_isDisposed) return;
        try
        {
            _smtc.PlaybackStatus = status switch
            {
                SystemMediaPlaybackStatus.Playing => MediaPlaybackStatus.Playing,
                SystemMediaPlaybackStatus.Paused => MediaPlaybackStatus.Paused,
                _ => _displayed is null ? MediaPlaybackStatus.Closed : MediaPlaybackStatus.Stopped,
            };
            _lastTimelineUpdate = DateTime.MinValue; // resync the position with the next update
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(Log, $"Updating the playback status failed: {ex.Message}");
        }
    }

    public void UpdateShuffle(bool isShuffle)
    {
        if (_isDisposed) return;
        try { _smtc.ShuffleEnabled = isShuffle; } catch (Exception) { /* not supported */ }
    }

    public void UpdateRepeat(RepeatMode repeatMode)
    {
        if (_isDisposed) return;
        try
        {
            _smtc.AutoRepeatMode = repeatMode switch
            {
                RepeatMode.All => MediaPlaybackAutoRepeatMode.List,
                RepeatMode.Single => MediaPlaybackAutoRepeatMode.Track,
                _ => MediaPlaybackAutoRepeatMode.None,
            };
        }
        catch (Exception)
        {
            // not supported
        }
    }

    public void UpdateCommandStates(SystemMediaCommandStates states)
    {
        if (_isDisposed) return;
        try
        {
            _smtc.IsPlayEnabled = states.Play || states.TogglePlayPause;
            _smtc.IsPauseEnabled = states.Pause || states.TogglePlayPause;
            _smtc.IsStopEnabled = states.Stop;
            // Podcasts: previous/next skip backward/forward (RemoteCommandHandler), so keep them enabled with skip
            _smtc.IsPreviousEnabled = states.PreviousTrack || states.SkipBackward;
            _smtc.IsNextEnabled = states.NextTrack || states.SkipForward;
            _smtc.IsRewindEnabled = states.SkipBackward;
            _smtc.IsFastForwardEnabled = states.SkipForward;
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(Log, $"Updating the commands failed: {ex.Message}");
        }
    }

    public void ConfigureCapabilities(double skipBackwardInterval, double skipForwardInterval, IReadOnlyList<double> supportedPlaybackRates)
    {
        // The Windows SMTC has no configurable skip intervals / rate lists.
    }

    // --- SMTC events (background thread) -------------------------------------------------------

    private void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        Action? action = args.Button switch
        {
            SystemMediaTransportControlsButton.Play => () => PlayRequested?.Invoke(),
            SystemMediaTransportControlsButton.Pause => () => PauseRequested?.Invoke(),
            SystemMediaTransportControlsButton.Stop => () => StopRequested?.Invoke(),
            SystemMediaTransportControlsButton.Next => () => NextRequested?.Invoke(),
            SystemMediaTransportControlsButton.Previous => () => PreviousRequested?.Invoke(),
            SystemMediaTransportControlsButton.FastForward => () => SkipForwardRequested?.Invoke(),
            SystemMediaTransportControlsButton.Rewind => () => SkipBackwardRequested?.Invoke(),
            _ => null,
        };
        if (action is not null) Post(action);
    }

    private void Smtc_PlaybackPositionChangeRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args)
    {
        var seconds = args.RequestedPlaybackPosition.TotalSeconds;
        Post(() => SeekRequested?.Invoke(seconds));
    }

    private void Smtc_PlaybackRateChangeRequested(SystemMediaTransportControls sender, PlaybackRateChangeRequestedEventArgs args)
    {
        var rate = args.RequestedPlaybackRate;
        Post(() => PlaybackRateRequested?.Invoke(rate));
    }

    private void Smtc_ShuffleEnabledChangeRequested(SystemMediaTransportControls sender, ShuffleEnabledChangeRequestedEventArgs args)
    {
        var isShuffle = args.RequestedShuffleEnabled;
        Post(() => ShuffleRequested?.Invoke(isShuffle));
    }

    private void Smtc_AutoRepeatModeChangeRequested(SystemMediaTransportControls sender, AutoRepeatModeChangeRequestedEventArgs args)
    {
        var mode = args.RequestedAutoRepeatMode switch
        {
            MediaPlaybackAutoRepeatMode.List => RepeatMode.All,
            MediaPlaybackAutoRepeatMode.Track => RepeatMode.Single,
            _ => RepeatMode.Off,
        };
        Post(() => RepeatRequested?.Invoke(mode));
    }

    /// Toggles play/pause (e.g. a play/pause media key that isn't delivered as separate play/pause button).
    public void RequestTogglePlayPause() => Post(() => TogglePlayPauseRequested?.Invoke());

    private void Post(Action action) => MainThread.Post(() =>
    {
        if (_isDisposed) return;
        try { action(); }
        catch (Exception ex) { AmperfyLog.Error(Log, $"Remote command failed: {ex}"); }
    });

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        try
        {
            _smtc.ButtonPressed -= Smtc_ButtonPressed;
            _smtc.PlaybackPositionChangeRequested -= Smtc_PlaybackPositionChangeRequested;
            _smtc.PlaybackRateChangeRequested -= Smtc_PlaybackRateChangeRequested;
            _smtc.ShuffleEnabledChangeRequested -= Smtc_ShuffleEnabledChangeRequested;
            _smtc.AutoRepeatModeChangeRequested -= Smtc_AutoRepeatModeChangeRequested;
            _smtc.DisplayUpdater.ClearAll();
            _smtc.IsEnabled = false;
            _hostPlayer.Dispose();
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(Log, $"Dispose failed: {ex.Message}");
        }
    }
}
