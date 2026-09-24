using Amperfy.App.Controls.Player;
using Amperfy.App.Helpers;
using Amperfy.App.Services;
using Amperfy.App.Services.Player;
using Amperfy.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Amperfy.App.Controls;

/// The player bar at the bottom of the shell (port of the macOS MiniPlayerView / iOS mini player + the options of
/// PlayerControlView): current item (click: now playing view; title/artist: album/artist page), transport controls
/// with seek bar, playback rate, sleep timer, music/podcast mode, lyrics and queue side panes, volume, mini player
/// and the player options menu. Hidden while the player is empty (IsPopupBarAllowedToHide).
public sealed partial class PlayerBar : UserControl
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly PlayerObserver _observer = new();
    private readonly PlayerTransportControls _transport;
    private readonly Button _favorite;
    private readonly Button _rate;
    private readonly ToggleButton _sleep;
    private readonly Button _mode;
    private readonly ToggleButton _lyrics;
    private readonly ToggleButton _queue;
    private readonly Button _volume;
    private readonly Button _miniPlayer;
    private readonly Button _nowPlaying;
    private readonly Button _more;

    public PlayerBar()
    {
        InitializeComponent();
        _transport = new PlayerTransportControls(TransportSize.Compact, showsSeekBar: true, showsShuffleRepeat: true, showsAudioInfo: false);
        TransportHost.Child = _transport;

        _favorite = PlayerUi.CreateIconButton(Icons.Heart, "Favorite", () => _ = ToggleFavoriteAsync(), 32);
        FavoriteHost.Child = _favorite;

        _rate = PlayerUi.CreatePlaybackRateButton();
        _sleep = PlayerUi.CreateSleepTimerButton();
        _mode = PlayerUi.CreateIconButton(PlayerGlyphs.MusicMode, "Switch Music/Podcast mode (Ctrl+M)", PlayerUi.SwitchPlayerMode);
        _lyrics = PlayerUi.CreateToggleButton(Icons.Lyrics, "Lyrics (Ctrl+Shift+L)", PlayerUi.ToggleLyricsPane);
        _queue = PlayerUi.CreateToggleButton(Icons.Queue, "Queue (Ctrl+Shift+Q)", PlayerUi.ToggleQueuePane);
        _volume = PlayerUi.CreateVolumeButton();
        _miniPlayer = PlayerUi.CreateIconButton(Icons.MiniPlayer, "Mini Player (Ctrl+Shift+M)", PlayerUi.ToggleMiniPlayer);
        _nowPlaying = PlayerUi.CreateIconButton(PlayerGlyphs.FullScreen, "Now Playing (Ctrl+Shift+P)", PlayerUi.ToggleNowPlaying);
        _more = PlayerUi.CreateIconButton(Icons.More, "Player options", () => { });
        _more.Flyout = PlayerUi.CreatePlayerOptionsFlyout();
        foreach (var element in new FrameworkElement[] { _rate, _sleep, _mode, _lyrics, _queue, _volume, _miniPlayer, _nowPlaying, _more })
        {
            ExtrasPanel.Children.Add(element);
        }

        ArtworkButton.Click += (_, _) => PlayerUi.ToggleNowPlaying();
        ToolTipService.SetToolTip(ArtworkButton, "Now Playing (Ctrl+Shift+P)");
        TitleButton.Click += (_, _) => PlayerUi.ShowAlbum(PlayerUi.Player.CurrentlyPlaying);
        ArtistButton.Click += (_, _) => PlayerUi.ShowArtist(PlayerUi.Player.CurrentlyPlaying);
        Artwork.CornerRadius = new CornerRadius(6);

        _observer.AnyChanged += Refresh;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => UpdateLayoutForWidth();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _observer.Register().IsActive = true;
        PlayerUi.UiStateChanged += Refresh;
        _services.Navigation.Navigated += Refresh;
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _observer.IsActive = false;
        PlayerUi.UiStateChanged -= Refresh;
        _services.Navigation.Navigated -= Refresh;
    }

    private async Task ToggleFavoriteAsync()
    {
        await PlayerUi.ToggleFavoriteAsync(PlayerUi.Player.CurrentlyPlaying);
        Refresh();
    }

    /// Hides the less important buttons on narrow windows (they are in the options menu too).
    private void UpdateLayoutForWidth()
    {
        var width = ActualWidth;
        var isMusic = PlayerUi.Player.PlayerMode == PlayerMode.Music;
        _rate.Visibility = width >= 1150 ? Visibility.Visible : Visibility.Collapsed;
        _sleep.Visibility = width >= 1150 ? Visibility.Visible : Visibility.Collapsed;
        _mode.Visibility = width >= 1000 && PlayerUi.IsPlayerModeSwitchVisible ? Visibility.Visible : Visibility.Collapsed;
        _miniPlayer.Visibility = width >= 900 ? Visibility.Visible : Visibility.Collapsed;
        _lyrics.Visibility = width >= 800 && isMusic ? Visibility.Visible : Visibility.Collapsed;
        _volume.Visibility = width >= 700 ? Visibility.Visible : Visibility.Collapsed;
        _nowPlaying.Visibility = width >= 760 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh()
    {
        var player = PlayerUi.Player;
        Visibility = player.IsPopupBarAllowedToHide && player.CurrentlyPlaying is null ? Visibility.Collapsed : Visibility.Visible;
        if (Visibility == Visibility.Collapsed) return;

        var info = PlayerUi.CurrentInfo();
        var playable = player.CurrentlyPlaying;
        TitleText.Text = info.Title;
        ArtistText.Text = info.Artist;
        ArtistButton.Visibility = string.IsNullOrEmpty(info.Artist) ? Visibility.Collapsed : Visibility.Visible;
        AlbumText.Text = info.Album;
        AlbumText.Visibility = string.IsNullOrEmpty(info.Album) || info.Album == info.Title ? Visibility.Collapsed : Visibility.Visible;
        TitleButton.IsEnabled = info.IsAlbumAvailable;
        ArtistButton.IsEnabled = info.IsArtistAvailable;
        ToolTipService.SetToolTip(TitleButton, info.IsAlbumAvailable ? $"{info.Title}\nShow album" : info.Title);
        ToolTipService.SetToolTip(ArtistButton, info.IsArtistAvailable ? $"{info.Artist}\nShow artist" : info.Artist);
        if (!ReferenceEquals(Artwork.Entity, playable)) Artwork.Entity = playable;
        else Artwork.Refresh();

        // favorite (songs) / radio web site
        if (playable is { IsFavoritable: true } && !_services.Settings.User.IsOfflineMode)
        {
            _favorite.Visibility = Visibility.Visible;
            PlayerUi.SetGlyph(_favorite, playable.IsFavorite ? Icons.HeartFill : Icons.Heart, playable.IsFavorite ? "Unmark Favorite" : "Mark as Favorite");
        }
        else if (playable?.AsRadio is { SiteUrl.Length: > 0 })
        {
            _favorite.Visibility = Visibility.Visible;
            PlayerUi.SetGlyph(_favorite, Icons.Link, "Open the radio's web site");
        }
        else
        {
            _favorite.Visibility = Visibility.Collapsed;
        }

        var isMusic = player.PlayerMode == PlayerMode.Music;
        PlayerUi.SetGlyph(_mode, isMusic ? PlayerGlyphs.MusicMode : PlayerGlyphs.PodcastMode,
            isMusic ? "Music mode: switch to Podcast mode (Ctrl+M)" : "Podcast mode: switch to Music mode (Ctrl+M)");
        _lyrics.IsChecked = PlayerUi.IsLyricsPaneVisible;
        _queue.IsChecked = PlayerUi.IsQueuePaneVisible;
        var isNowPlaying = PlayerUi.IsNowPlayingVisible;
        PlayerUi.SetGlyph(_nowPlaying, isNowPlaying ? PlayerGlyphs.BackToWindow : PlayerGlyphs.FullScreen,
            isNowPlaying ? "Close Now Playing (Ctrl+Shift+P)" : "Now Playing (Ctrl+Shift+P)");
        PlayerUi.SetGlyph(_miniPlayer, Icons.MiniPlayer, MiniPlayerWindow.IsOpen ? "Close Mini Player (Ctrl+Shift+M)" : "Mini Player (Ctrl+Shift+M)");
        UpdateLayoutForWidth();
    }
}
