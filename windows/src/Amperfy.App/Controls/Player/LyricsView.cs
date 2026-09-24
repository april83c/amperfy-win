using Amperfy.App.Helpers;
using Amperfy.App.Services;
using Amperfy.App.Services.Player;
using Amperfy.Core.Api;
using Amperfy.Core.Common;
using Amperfy.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Amperfy.App.Controls.Player;

/// Lyrics of the current song (port of LyricsVC + LyricsView): synced lyrics highlight the current line and scroll
/// it to the middle (paused for a few seconds after the user scrolled); clicking a synced line seeks to it.
/// Unsynced lyrics are displayed as text. The lyrics' own offset and a user adjustable offset are applied.
public sealed partial class LyricsView : UserControl
{
    private static readonly TimeSpan AutoScrollSuppression = TimeSpan.FromSeconds(5);
    private const double InactiveOpacity = 0.45;
    private const int OffsetStepMs = 250;

    private readonly PlayerObserver _observer = new();
    private readonly ScrollViewer _scrollViewer;
    private readonly StackPanel _linesPanel;
    private readonly Grid _titleBar;
    private readonly TextBlock _offsetText;
    private readonly List<TextBlock> _lineViews = [];
    private StructuredLyrics? _lyrics;
    private Song? _song;
    private int _currentLine = -1;
    private DateTime _suppressAutoScrollUntil = DateTime.MinValue;
    private DateTime _programmaticScrollUntil = DateTime.MinValue;
    private int _loadRequest;
    private bool _isLoaded;
    private double _fontSize = 18;

    /// User offset in milliseconds (positive: lyrics appear sooner), for this session.
    private static int _userOffsetMs;

    public LyricsView()
    {
        _linesPanel = new StackPanel { Spacing = 14, Padding = new Thickness(24, 0, 24, 0) };
        _scrollViewer = new ScrollViewer
        {
            Content = _linesPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _scrollViewer.ViewChanged += (_, e) =>
        {
            if (DateTime.UtcNow > _programmaticScrollUntil) _suppressAutoScrollUntil = DateTime.UtcNow + AutoScrollSuppression;
        };
        _scrollViewer.AddHandler(PointerWheelChangedEvent, new PointerEventHandler((_, _) => SuppressAutoScroll()), true);
        _scrollViewer.SizeChanged += (_, _) => UpdatePadding();

        var title = new TextBlock { Text = "Lyrics", VerticalAlignment = VerticalAlignment.Center };
        if (Application.Current.Resources.TryGetValue("SubtitleTextBlockStyle", out var style) && style is Style s) title.Style = s;
        _offsetText = new TextBlock { FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
        var earlier = PlayerUi.CreateIconButton("", "Lyrics earlier (offset -0.25 s)", () => ChangeOffset(-OffsetStepMs), 32, 12);
        var later = PlayerUi.CreateIconButton("", "Lyrics later (offset +0.25 s)", () => ChangeOffset(OffsetStepMs), 32, 12);
        var close = PlayerUi.CreateIconButton(Icons.Clear, "Close", () => PlayerUi.ShowLyricsPane(false), 32);
        _titleBar = new Grid { Padding = new Thickness(16, 12, 8, 8), ColumnSpacing = 2, Visibility = Visibility.Collapsed };
        for (var i = 0; i < 5; i++)
        {
            _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        }
        Grid.SetColumn(earlier, 1);
        Grid.SetColumn(_offsetText, 2);
        Grid.SetColumn(later, 3);
        Grid.SetColumn(close, 4);
        _titleBar.Children.Add(title);
        _titleBar.Children.Add(earlier);
        _titleBar.Children.Add(_offsetText);
        _titleBar.Children.Add(later);
        _titleBar.Children.Add(close);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_scrollViewer, 1);
        root.Children.Add(_titleBar);
        root.Children.Add(_scrollViewer);
        Content = root;

        _observer.StartedPlayingFromBeginning += FetchSongInfoAndRefresh;
        _observer.StartedPlaying += () => RefreshLyrics(force: false);
        _observer.Stopped += () => RefreshLyrics(force: false);
        _observer.PlaylistChanged += () => RefreshLyrics(force: false);
        _observer.LyricsTimeChanged += ScrollTo;
        Loaded += (_, _) =>
        {
            _isLoaded = true;
            _observer.Register().IsActive = true;
            _titleBar.Visibility = ShowsTitle ? Visibility.Visible : Visibility.Collapsed;
            UpdateOffsetText();
            RefreshLyrics(force: false);
        };
        Unloaded += (_, _) =>
        {
            _isLoaded = false;
            _observer.IsActive = false;
        };
    }

    /// Shows the title bar with offset buttons and close button (side pane).
    public bool ShowsTitle { get; set; }

    /// Font size of the lines (the now playing page uses a larger one).
    public double LineFontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = value;
            foreach (var line in _lineViews) line.FontSize = value;
        }
    }

    private void SuppressAutoScroll() => _suppressAutoScrollUntil = DateTime.UtcNow + AutoScrollSuppression;

    private void ChangeOffset(int deltaMs)
    {
        _userOffsetMs += deltaMs;
        UpdateOffsetText();
        _currentLine = -1;
        ScrollTo(TimeSpan.FromSeconds(PlayerUi.Player.ElapsedTime));
    }

    private void UpdateOffsetText() =>
        _offsetText.Text = _userOffsetMs == 0 ? "±0.00 s" : $"{(_userOffsetMs > 0 ? "+" : "")}{_userOffsetMs / 1000.0:0.00} s";

    private void UpdatePadding()
    {
        var half = Math.Max(0, _scrollViewer.ActualHeight / 2 - 20);
        var isSynced = _lyrics?.Synced == true;
        _linesPanel.Padding = new Thickness(24, isSynced ? half : 16, 24, isSynced ? half : 16);
    }

    /// Swift fetchSongInfoAndUpdateLyrics: sync the song (downloads its lyrics) and show them.
    private async void FetchSongInfoAndRefresh()
    {
        RefreshLyrics(force: false);
        var services = AppServices.Instance;
        if (services.Settings.User.IsOfflineMode || PlayerUi.Player.CurrentlyPlaying?.AsSong is not { } song || song.Account?.Info is not { } info) return;
        try
        {
            await services.Kit.GetMeta(info).LibrarySyncer.SyncAsync(song);
            if (ReferenceEquals(PlayerUi.Player.CurrentlyPlaying, song)) RefreshLyrics(force: true);
        }
        catch (Exception ex)
        {
            services.EventLogger.Report("Song Info", ex, displayPopup: false);
        }
    }

    /// Loads and shows the lyrics of the current song (Swift refreshLyrics).
    public async void RefreshLyrics(bool force)
    {
        if (!_isLoaded) return;
        var song = PlayerUi.Player.CurrentlyPlaying?.AsSong;
        if (!force && ReferenceEquals(song, _song) && _lyrics is not null) return;
        _song = song;
        var request = ++_loadRequest;
        if (song?.LyricsRelFilePath is not { } relFilePath || song.Account?.Info is not { } info)
        {
            ShowNotAvailable();
            return;
        }
        try
        {
            var lyricsList = await AppServices.Instance.Kit.GetMeta(info).LibrarySyncer.ParseLyricsAsync(relFilePath);
            if (request != _loadRequest) return;
            if (ReferenceEquals(song, PlayerUi.Player.CurrentlyPlaying?.AsSong) && lyricsList.GetFirstSyncedLyricsOrUnsyncedAsDefault() is { } lyrics)
            {
                Show(lyrics);
            }
            else
            {
                ShowNotAvailable();
            }
        }
        catch (Exception)
        {
            if (request == _loadRequest) ShowNotAvailable();
        }
    }

    private void ShowNotAvailable()
    {
        var lyrics = new StructuredLyrics { Synced = false };
        lyrics.Line.Add(new LyricsLine { Value = PlayerUi.Player.CurrentlyPlaying is null ? "Nothing playing" : "No Lyrics" });
        Show(lyrics, highlightAll: true);
        _lyrics = null; // retry on the next refresh
    }

    private void Show(StructuredLyrics lyrics, bool highlightAll = false)
    {
        _lyrics = lyrics;
        _currentLine = -1;
        _lineViews.Clear();
        _linesPanel.Children.Clear();
        foreach (var line in lyrics.Line)
        {
            var view = new TextBlock
            {
                Text = line.Value,
                TextWrapping = TextWrapping.WrapWholeWords,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                FontSize = _fontSize,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Opacity = lyrics.Synced && !highlightAll ? InactiveOpacity : 1.0,
                IsTextSelectionEnabled = !lyrics.Synced,
            };
            if (lyrics.Synced && line.Start is not null)
            {
                var start = line.Start.Value;
                view.Tapped += (_, _) =>
                {
                    SuppressAutoScroll();
                    var seconds = Math.Max(0, (start - TotalOffsetMs(lyrics)) / 1000.0);
                    PlayerUi.Player.Seek(seconds);
                };
                ToolTipService.SetToolTip(view, TimeSpan.FromMilliseconds(start).TotalSeconds.AsColonDurationString());
            }
            _lineViews.Add(view);
            _linesPanel.Children.Add(view);
        }
        UpdatePadding();
        _programmaticScrollUntil = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        _scrollViewer.ChangeView(null, 0, null, disableAnimation: true);
        _suppressAutoScrollUntil = DateTime.MinValue;
        if (lyrics.Synced) ScrollTo(TimeSpan.FromSeconds(PlayerUi.Player.ElapsedTime));
    }

    private static int TotalOffsetMs(StructuredLyrics lyrics) => lyrics.Offset + _userOffsetMs;

    /// Index of the line active at <paramref name="timeMs"/> (Swift: the line before the first line starting at or
    /// after the time; the first line before the lyrics start; the last line after the last start).
    public static int ActiveLineIndex(IReadOnlyList<LyricsLine> lines, double timeMs)
    {
        if (lines.Count == 0) return -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if ((lines[i].Start ?? 0) >= timeMs) return Math.Max(i - 1, 0);
        }
        return lines.Count - 1;
    }

    /// Highlights the current line and scrolls it to the middle (Swift scroll(toTime:)).
    private void ScrollTo(TimeSpan time)
    {
        if (!_isLoaded || _lyrics is not { Synced: true } lyrics || _lineViews.Count == 0) return;
        var index = ActiveLineIndex(lyrics.Line, time.TotalMilliseconds + TotalOffsetMs(lyrics));
        if (index < 0 || index >= _lineViews.Count || index == _currentLine) return;
        if (_currentLine >= 0 && _currentLine < _lineViews.Count) _lineViews[_currentLine].Opacity = InactiveOpacity;
        _currentLine = index;
        var view = _lineViews[index];
        view.Opacity = 1.0;
        if (DateTime.UtcNow < _suppressAutoScrollUntil) return;
        var smooth = AppServices.Instance.Settings.User.IsLyricsSmoothScrolling;
        _programmaticScrollUntil = DateTime.UtcNow + TimeSpan.FromSeconds(smooth ? 1.2 : 0.3);
        view.StartBringIntoView(new BringIntoViewOptions
        {
            VerticalAlignmentRatio = 0.5,
            AnimationDesired = smooth,
        });
    }
}
