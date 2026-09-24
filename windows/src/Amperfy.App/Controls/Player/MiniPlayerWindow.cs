using Amperfy.App.Helpers;
using Amperfy.App.Services;
using Amperfy.App.Services.Player;
using Amperfy.Core.Common;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Amperfy.App.Controls.Player;

/// Small player window (port of the macOS mini player, MiniPlayerSceneDelegate / MiniPlayerView): artwork, title,
/// transport controls and seek bar, optionally the queue or the lyrics below. "Always on top" uses the compact overlay presenter (picture in picture);
/// otherwise a small normal window. Showing it minimizes the main window, closing it restores the main window.
public sealed partial class MiniPlayerWindow : Window
{
    private const string WindowTitle = "Amperfy Mini Player";
    private static MiniPlayerWindow? _instance;
    private static bool _isAppClosing;

    private readonly PlayerObserver _observer = new();
    private readonly ArtworkImage _artwork;
    private readonly TextBlock _title;
    private readonly TextBlock _artist;
    private readonly ToggleButton _pin;
    private readonly Grid _root;
    private readonly ToggleButton _queueToggle;
    private readonly ToggleButton _lyricsToggle;
    private readonly Border _expandHost;
    private QueueView? _queueView;
    private LyricsView? _lyricsView;
    private bool _restoreMainWindowOnClose = true;

    public static bool IsOpen => _instance is not null;

    public static MiniPlayerWindow? Instance => _instance;

    /// Opens (or activates) the mini player.
    public static MiniPlayerWindow Show(bool minimizeMainWindow = true)
    {
        if (_instance is null)
        {
            _instance = new MiniPlayerWindow();
            PlayerUi.NotifyUiStateChanged();
        }
        _instance.Activate();
        if (minimizeMainWindow)
        {
            try
            {
                if (AppServices.Instance.MainWindow.AppWindow.Presenter is OverlappedPresenter presenter) presenter.Minimize();
            }
            catch (Exception ex)
            {
                AmperfyLog.Warning("MiniPlayer", $"Minimizing the main window failed: {ex.Message}");
            }
        }
        return _instance;
    }

    public static void Toggle()
    {
        if (_instance is { } window) window.Close();
        else Show();
    }

    /// Closes the mini player without restoring the main window (app shutdown).
    public static void CloseForShutdown()
    {
        _isAppClosing = true;
        if (_instance is { } window)
        {
            window._restoreMainWindowOnClose = false;
            window.Close();
        }
    }

    private MiniPlayerWindow()
    {
        Title = WindowTitle;
        try { SystemBackdrop = new MicaBackdrop(); } catch (Exception) { /* not supported */ }

        _artwork = new ArtworkImage { Width = 88, Height = 88, DecodeSize = 176, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Top };
        _title = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        _artist = new TextBlock { FontSize = 12, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Margin = new Thickness(12, 0, 4, 0) };
        texts.Children.Add(_title);
        texts.Children.Add(_artist);
        _artwork.ContextFlyout = PlayerUi.CreateCurrentItemFlyout();
        texts.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); // hit testable for the context menu
        texts.ContextFlyout = PlayerUi.CreateCurrentItemFlyout();

        _pin = PlayerUi.CreateToggleButton(PlayerGlyphs.Pin, "Always on top", TogglePin, 32, 14);
        var openMain = PlayerUi.CreateIconButton(PlayerGlyphs.OpenMainWindow, "Open main window", () =>
        {
            PlayerUi.BringMainWindowToFront();
        }, 32, 14);
        var more = PlayerUi.CreateIconButton(Icons.More, "Player options", () => { }, 32, 14);
        more.Flyout = PlayerUi.CreatePlayerOptionsFlyout();
        var topButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Top };
        topButtons.Children.Add(_pin);
        topButtons.Children.Add(openMain);
        topButtons.Children.Add(more);

        var info = new Grid { ColumnSpacing = 0 };
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(topButtons, 2);
        info.Children.Add(_artwork);
        info.Children.Add(texts);
        info.Children.Add(topButtons);

        var transport = new PlayerTransportControls(TransportSize.Compact, showsSeekBar: true, showsShuffleRepeat: true, showsAudioInfo: false);

        var extras = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 2 };
        extras.Children.Add(PlayerUi.CreateVolumeButton(32));
        extras.Children.Add(PlayerUi.CreatePlaybackRateButton(32));
        extras.Children.Add(PlayerUi.CreateSleepTimerButton(32));
        // queue / lyrics below the controls (the macOS mini player window is the full popup player)
        _queueToggle = PlayerUi.CreateToggleButton(Icons.Queue, "Queue", () => ToggleExpanded(lyrics: false), 32, 14);
        _lyricsToggle = PlayerUi.CreateToggleButton(Icons.Lyrics, "Lyrics", () => ToggleExpanded(lyrics: true), 32, 14);
        extras.Children.Add(_lyricsToggle);
        extras.Children.Add(_queueToggle);
        _expandHost = new Border { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };

        _root = new Grid { Padding = new Thickness(12, 8, 12, 8), RowSpacing = 4 };
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(transport, 1);
        Grid.SetRow(extras, 2);
        Grid.SetRow(_expandHost, 3);
        _root.Children.Add(info);
        _root.Children.Add(transport);
        _root.Children.Add(extras);
        _root.Children.Add(_expandHost);
        _root.RequestedTheme = AppServices.Instance.RequestedElementTheme;
        if (!Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
        {
            // no Mica (Windows 10): solid background in the effective theme
            var isDark = _root.RequestedTheme == ElementTheme.Dark ||
                         (_root.RequestedTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
            _root.Background = new SolidColorBrush(isDark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 243, 243, 243));
        }
        Content = _root;

        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")); } catch (Exception) { /* ignore */ }
        ApplyPresenter(AppServices.Instance.Settings.User.IsMiniPlayerAlwaysOnTop);
        PlayerKeyboardShortcuts.Attach(this, () => true);

        _observer.AnyChanged += Refresh;
        _observer.ArtworkChanged += RefreshArtwork;
        _observer.Register();
        PlayerUi.UiStateChanged += Refresh;
        PlayerUi.CurrentArtworkChanged += RefreshArtwork;
        Closed += OnClosed;
        Refresh();
    }

    private void ApplyPresenter(bool alwaysOnTop)
    {
        try
        {
            if (alwaysOnTop)
            {
                var presenter = CompactOverlayPresenter.Create();
                presenter.InitialSize = CompactOverlaySize.Medium;
                AppWindow.SetPresenter(presenter);
            }
            else
            {
                var presenter = OverlappedPresenter.Create();
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = true;
                presenter.IsResizable = true;
                presenter.IsAlwaysOnTop = false;
                AppWindow.SetPresenter(presenter);
            }
            ApplySize();
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("MiniPlayer", $"Configuring the window failed: {ex.Message}");
        }
        _pin.IsChecked = alwaysOnTop;
    }

    private bool IsExpanded => _expandHost.Visibility == Visibility.Visible;

    private void ApplySize()
    {
        try
        {
            AppWindow.Resize(new SizeInt32(Scale(440), Scale(IsExpanded ? 620 : 250)));
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning("MiniPlayer", $"Resizing the window failed: {ex.Message}");
        }
    }

    /// Shows / hides the queue or the lyrics below the player controls (the window grows).
    private void ToggleExpanded(bool lyrics)
    {
        FrameworkElement content = lyrics
            ? _lyricsView ??= new LyricsView { ShowsTitle = false }
            : _queueView ??= new QueueView { ShowsTitle = false };
        var hide = IsExpanded && ReferenceEquals(_expandHost.Child, content);
        if (hide)
        {
            _expandHost.Child = null;
            _expandHost.Visibility = Visibility.Collapsed;
        }
        else
        {
            PlayerUi.DetachFromParent(content);
            _expandHost.Child = content;
            _expandHost.Visibility = Visibility.Visible;
        }
        RefreshExpandToggles();
        ApplySize();
    }

    private void RefreshExpandToggles()
    {
        _queueToggle.IsChecked = IsExpanded && _expandHost.Child is QueueView;
        _lyricsToggle.IsChecked = IsExpanded && _expandHost.Child is LyricsView;
        var lyricsAvailable = PlayerUi.IsLyricsAvailable;
        _lyricsToggle.Visibility = lyricsAvailable || _lyricsToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private int Scale(double value)
    {
        var scale = _root?.XamlRoot?.RasterizationScale ?? AppServices.Instance.MainWindow.Content?.XamlRoot?.RasterizationScale ?? 1.0;
        return (int)Math.Round(value * scale);
    }

    private void TogglePin()
    {
        var alwaysOnTop = !AppServices.Instance.Settings.User.IsMiniPlayerAlwaysOnTop;
        AppServices.Instance.Settings.User.IsMiniPlayerAlwaysOnTop = alwaysOnTop;
        ApplyPresenter(alwaysOnTop);
    }

    private void Refresh()
    {
        if (_instance != this) return;
        var info = PlayerUi.CurrentInfo();
        _title.Text = info.Title;
        _artist.Text = string.IsNullOrEmpty(info.Album) || info.Album == info.Title ? info.Artist : $"{info.Artist} · {info.Album}";
        ToolTipService.SetToolTip(_title, info.Title);
        var playable = PlayerUi.Player.CurrentlyPlaying;
        if (!ReferenceEquals(_artwork.Entity, playable)) _artwork.Entity = playable;
        _pin.IsChecked = AppServices.Instance.Settings.User.IsMiniPlayerAlwaysOnTop;
        RefreshExpandToggles();
    }

    private void RefreshArtwork() => _artwork.Refresh();

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _observer.IsActive = false;
        PlayerUi.UiStateChanged -= Refresh;
        PlayerUi.CurrentArtworkChanged -= RefreshArtwork;
        if (_instance == this) _instance = null;
        if (_isAppClosing) return;
        if (_restoreMainWindowOnClose) PlayerUi.BringMainWindowToFront();
        PlayerUi.NotifyUiStateChanged();
    }
}
