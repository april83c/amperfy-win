using Amperfy.App.Helpers;
using Amperfy.App.Services;
using Amperfy.App.Services.Player;
using Amperfy.Core.Model;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Amperfy.App.Controls.Player;

/// Size variants of <see cref="PlayerTransportControls"/>.
public enum TransportSize
{
    Compact,
    Large,
}

/// Shuffle / skip back / previous / play / next / skip forward / repeat buttons plus the seek bar (port of the
/// control part of PlayerControlView / MiniPlayerView). Keeps itself up to date via the player notifications and a
/// UI timer for the progress. Create it in code (configuration via constructor).
public sealed partial class PlayerTransportControls : UserControl
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    private readonly PlayerObserver _observer = new();
    private readonly ToggleButton _shuffle;
    private readonly Button _skipBackward;
    private readonly Button _previous;
    private readonly Button _play;
    private readonly Button _next;
    private readonly Button _skipForward;
    private readonly ToggleButton _repeat;
    private readonly SeekBar? _seekBar;
    private readonly bool _showsShuffleRepeat;
    private DispatcherQueueTimer? _timer;

    public PlayerTransportControls() : this(TransportSize.Compact, showsSeekBar: true, showsShuffleRepeat: true, showsAudioInfo: false) { }

    public PlayerTransportControls(TransportSize size, bool showsSeekBar, bool showsShuffleRepeat, bool showsAudioInfo)
    {
        _showsShuffleRepeat = showsShuffleRepeat;
        var buttonSize = size == TransportSize.Large ? 48.0 : 36.0;
        var iconSize = size == TransportSize.Large ? 20.0 : 16.0;
        var playSize = size == TransportSize.Large ? 60.0 : 42.0;

        _shuffle = PlayerUi.CreateToggleButton(Icons.Shuffle, "Shuffle (Ctrl+H)", OnShuffle, buttonSize, iconSize);
        _skipBackward = PlayerUi.CreateIconButton(Icons.SkipBack, "Skip back", PlayerUi.SkipBackward, buttonSize, iconSize);
        _previous = PlayerUi.CreateIconButton(Icons.Previous, "Previous", PlayerUi.Previous, buttonSize, iconSize);
        _play = PlayerUi.CreateIconButton(Icons.Play, "Play", OnPlay, playSize, size == TransportSize.Large ? 24 : 18);
        PlayerUi.ApplyAccentStyle(_play);
        _play.CornerRadius = new CornerRadius(playSize / 2);
        _next = PlayerUi.CreateIconButton(Icons.Next, "Next", PlayerUi.Next, buttonSize, iconSize);
        _skipForward = PlayerUi.CreateIconButton(Icons.SkipForward, "Skip forward", PlayerUi.SkipForward, buttonSize, iconSize);
        _repeat = PlayerUi.CreateToggleButton(Icons.Repeat, "Repeat", OnRepeat, buttonSize, iconSize);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = size == TransportSize.Large ? 12 : 4,
        };
        foreach (var b in new Control[] { _shuffle, _skipBackward, _previous, _play, _next, _skipForward, _repeat })
        {
            b.VerticalAlignment = VerticalAlignment.Center;
            buttons.Children.Add(b);
        }

        var root = new StackPanel { Spacing = size == TransportSize.Large ? 8 : 0, VerticalAlignment = VerticalAlignment.Center };
        root.Children.Add(buttons);
        if (showsSeekBar)
        {
            _seekBar = new SeekBar { ShowsAudioInfo = showsAudioInfo };
            root.Children.Add(_seekBar);
        }
        Content = root;

        _observer.AnyChanged += Refresh;
        _observer.ElapsedTimeChanged += RefreshProgress;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public SeekBar? SeekBar => _seekBar;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _observer.Register().IsActive = true;
        PlayerUi.UiStateChanged += Refresh;
        if (_seekBar is not null && DispatcherQueue is { } queue)
        {
            _timer ??= queue.CreateTimer();
            _timer.Interval = ProgressInterval;
            _timer.IsRepeating = true;
            _timer.Tick -= Timer_Tick;
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _observer.IsActive = false;
        PlayerUi.UiStateChanged -= Refresh;
        _timer?.Stop();
    }

    private void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (PlayerUi.Player.IsPlaying) RefreshProgress();
    }

    private void OnPlay()
    {
        PlayerUi.TogglePlayPause();
        Refresh();
    }

    private void OnShuffle()
    {
        PlayerUi.ToggleShuffle();
        Refresh();
    }

    private void OnRepeat()
    {
        PlayerUi.CycleRepeat();
        Refresh();
    }

    private void RefreshProgress() => _seekBar?.Refresh();

    public void Refresh()
    {
        var player = PlayerUi.Player;
        var settings = AppServices.Instance.Settings.User;
        var hasItem = player.CurrentlyPlaying is not null;
        var isMusic = player.PlayerMode == PlayerMode.Music;

        PlayerUi.SetGlyph(_play, PlayerUi.PlayGlyph, PlayerUi.PlayTooltip);
        _play.IsEnabled = hasItem || player.NextQueueCount > 0 || player.UserQueueCount > 0;
        PlayerUi.SetGlyph(_previous, PlayerUi.PreviousGlyph, PlayerUi.PreviousTooltip);
        PlayerUi.SetGlyph(_next, PlayerUi.NextGlyph, PlayerUi.NextTooltip);
        _previous.IsEnabled = hasItem;
        _next.IsEnabled = hasItem;

        // skip buttons: music mode only (podcast mode uses previous/next for skipping), if enabled in the settings
        var showSkip = isMusic && settings.IsShowMusicPlayerSkipButtons;
        _skipBackward.Visibility = showSkip ? Visibility.Visible : Visibility.Collapsed;
        _skipForward.Visibility = showSkip ? Visibility.Visible : Visibility.Collapsed;
        _skipBackward.IsEnabled = hasItem && player.IsSkipAvailable;
        _skipForward.IsEnabled = hasItem && player.IsSkipAvailable;
        var skipBack = $"Skip back {(int)player.SkipBackwardInterval} s (Ctrl+Shift+Left)";
        var skipForward = $"Skip forward {(int)player.SkipForwardInterval} s (Ctrl+Shift+Right)";
        PlayerUi.SetGlyph(_skipBackward, Icons.SkipBack, skipBack);
        PlayerUi.SetGlyph(_skipForward, Icons.SkipForward, skipForward);

        var showShuffleRepeat = _showsShuffleRepeat && isMusic;
        _shuffle.Visibility = showShuffleRepeat ? Visibility.Visible : Visibility.Collapsed;
        _repeat.Visibility = showShuffleRepeat ? Visibility.Visible : Visibility.Collapsed;
        _shuffle.IsChecked = player.IsShuffle;
        _shuffle.IsEnabled = PlayerUi.IsShuffleEnabled;
        _repeat.IsChecked = player.RepeatMode != RepeatMode.Off;
        PlayerUi.SetGlyph(_repeat, PlayerUi.RepeatGlyph, PlayerUi.RepeatTooltip);

        RefreshProgress();
    }
}
