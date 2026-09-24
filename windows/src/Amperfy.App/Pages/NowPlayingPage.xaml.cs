using Amperfy.App.Controls.Player;
using Amperfy.App.Helpers;
using Amperfy.App.Services;
using Amperfy.App.Services.Player;
using Amperfy.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Amperfy.App.Pages;

/// Large player view (port of PopupPlayerVC + LargeCurrentlyPlayingPlayerView + PlayerControlView): big artwork,
/// title/artist/album (links to the album/artist pages), favorite, transport controls with seek bar and audio info,
/// playback rate, sleep timer, mode, volume and the queue / lyrics next to it. Escape closes it.
public sealed partial class NowPlayingPage : Page
{
    private const double NarrowWidth = 900;

    private readonly AppServices _services = AppServices.Instance;
    private readonly PlayerObserver _observer = new();
    private readonly PlayerTransportControls _transport;
    private readonly HyperlinkButton _titleButton;
    private readonly TextBlock _titleText;
    private readonly HyperlinkButton _artistButton;
    private readonly TextBlock _artistText;
    private readonly HyperlinkButton _albumButton;
    private readonly TextBlock _albumText;
    private readonly Button _favorite;
    private readonly Button _mode;
    private readonly ToggleButton _queueTab;
    private readonly ToggleButton _lyricsTab;
    private readonly QueueView _queueView;
    private readonly LyricsView _lyricsView;
    private bool _isLyricsTab;

    public NowPlayingPage()
    {
        InitializeComponent();

        (_titleButton, _titleText) = CreateLink("TitleTextBlockStyle", 24);
        (_artistButton, _artistText) = CreateLink("SubtitleTextBlockStyle", 18);
        (_albumButton, _albumText) = CreateLink("BodyTextBlockStyle", 14);
        _artistText.Opacity = 0.85;
        _albumText.Opacity = 0.7;
        _titleButton.Click += (_, _) => PlayerUi.ShowAlbum(PlayerUi.Player.CurrentlyPlaying);
        _artistButton.Click += (_, _) => PlayerUi.ShowArtist(PlayerUi.Player.CurrentlyPlaying);
        _albumButton.Click += (_, _) => PlayerUi.ShowAlbum(PlayerUi.Player.CurrentlyPlaying);
        InfoPanel.Children.Add(_titleButton);
        InfoPanel.Children.Add(_artistButton);
        InfoPanel.Children.Add(_albumButton);

        _favorite = PlayerUi.CreateIconButton(Icons.Heart, "Favorite", () => _ = ToggleFavoriteAsync(), 44, 20);
        FavoriteHost.Child = _favorite;

        _transport = new PlayerTransportControls(TransportSize.Large, showsSeekBar: true, showsShuffleRepeat: true, showsAudioInfo: true);
        TransportHost.Child = _transport;

        _mode = PlayerUi.CreateIconButton(PlayerGlyphs.MusicMode, "Switch Music/Podcast mode (Ctrl+M)", PlayerUi.SwitchPlayerMode);
        var more = PlayerUi.CreateIconButton(Icons.More, "Player options", () => { });
        _queueView = new QueueView { ShowsTitle = false };
        _lyricsView = new LyricsView { ShowsTitle = false, LineFontSize = 22 };
        more.Flyout = PlayerUi.CreatePlayerOptionsFlyout(_queueView.ScrollToCurrent);
        ExtrasPanel.Children.Add(PlayerUi.CreatePlaybackRateButton());
        ExtrasPanel.Children.Add(PlayerUi.CreateSleepTimerButton());
        ExtrasPanel.Children.Add(_mode);
        ExtrasPanel.Children.Add(PlayerUi.CreateVolumeButton());
        ExtrasPanel.Children.Add(PlayerUi.CreateIconButton(Icons.MiniPlayer, "Mini Player (Ctrl+Shift+M)", PlayerUi.ToggleMiniPlayer));
        ExtrasPanel.Children.Add(PlayerUi.CreateIconButton(PlayerGlyphs.BackToWindow, "Close Now Playing (Esc)", PlayerUi.ToggleNowPlaying));
        ExtrasPanel.Children.Add(more);

        _queueTab = CreateTab("Queue", Icons.Queue, () => SelectTab(lyrics: false));
        _lyricsTab = CreateTab("Lyrics", Icons.Lyrics, () => SelectTab(lyrics: true));
        TabPanel.Children.Add(_queueTab);
        TabPanel.Children.Add(_lyricsTab);
        _isLyricsTab = _services.Settings.User.IsPlayerLyricsDisplayed;

        _observer.AnyChanged += Refresh;
        _observer.StartedPlayingFromBeginning += FetchSongInfo;
        SizeChanged += (_, _) => UpdateLayoutForSize();
        KeyDown += Page_KeyDown;
    }

    private static (HyperlinkButton Button, TextBlock Text) CreateLink(string styleKey, double fallbackSize)
    {
        var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, FontSize = fallbackSize };
        if (Application.Current.Resources.TryGetValue(styleKey, out var style) && style is Style s) text.Style = s;
        if (Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out var brush) && brush is Microsoft.UI.Xaml.Media.Brush b) text.Foreground = b;
        var button = new HyperlinkButton { Content = text, Padding = new Thickness(0), MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Left };
        return (button, text);
    }

    private static ToggleButton CreateTab(string text, string glyph, Action onClick)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
        content.Children.Add(new TextBlock { Text = text });
        var tab = new ToggleButton { Content = content, Padding = new Thickness(12, 6, 12, 6) };
        tab.Click += (_, _) => onClick();
        return tab;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _observer.Register().IsActive = true;
        PlayerUi.UiStateChanged += Refresh;
        // the side pane would show the same content
        PlayerUi.ShowQueuePane(false);
        PlayerUi.ShowLyricsPane(false);
        SelectTab(_isLyricsTab);
        Refresh();
        FetchSongInfo();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _observer.IsActive = false;
        PlayerUi.UiStateChanged -= Refresh;
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            PlayerUi.ToggleNowPlaying();
            e.Handled = true;
        }
    }

    private void SelectTab(bool lyrics)
    {
        if (lyrics && PlayerUi.Player.PlayerMode != PlayerMode.Music) lyrics = false;
        _isLyricsTab = lyrics;
        _services.Settings.User.IsPlayerLyricsDisplayed = lyrics;
        _queueTab.IsChecked = !lyrics;
        _lyricsTab.IsChecked = lyrics;
        FrameworkElement content = lyrics ? _lyricsView : _queueView;
        if (TabContentHost.Child != content)
        {
            PlayerUi.DetachFromParent(content);
            TabContentHost.Child = content;
        }
    }

    /// Swift fetchSongInfoAndUpdateViews: refresh the song's metadata from the server.
    private async void FetchSongInfo()
    {
        if (_services.Settings.User.IsOfflineMode || PlayerUi.Player.CurrentlyPlaying?.AsSong is not { } song || song.Account?.Info is not { } info) return;
        try
        {
            await _services.Kit.GetMeta(info).LibrarySyncer.SyncAsync(song);
            if (ReferenceEquals(PlayerUi.Player.CurrentlyPlaying, song)) Refresh();
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Song Info", ex, displayPopup: false);
        }
    }

    private async Task ToggleFavoriteAsync()
    {
        await PlayerUi.ToggleFavoriteAsync(PlayerUi.Player.CurrentlyPlaying);
        Refresh();
    }

    private void UpdateLayoutForSize()
    {
        var isNarrow = ActualWidth < NarrowWidth;
        SidePanel.Visibility = isNarrow ? Visibility.Collapsed : Visibility.Visible;
        SideColumn.Width = isNarrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        RootGrid.ColumnSpacing = isNarrow ? 0 : 32;
        // artwork: as large as possible, leaving room for the controls
        var availableWidth = (isNarrow ? ActualWidth : ActualWidth / 2) - 64;
        var availableHeight = ActualHeight - 330;
        var size = Math.Clamp(Math.Min(availableWidth, availableHeight), 160, 520);
        Artwork.Width = size;
        Artwork.Height = size;
    }

    private void Refresh()
    {
        var player = PlayerUi.Player;
        var info = PlayerUi.CurrentInfo();
        var playable = player.CurrentlyPlaying;
        _titleText.Text = info.Title;
        _artistText.Text = info.Artist;
        _artistButton.Visibility = string.IsNullOrEmpty(info.Artist) ? Visibility.Collapsed : Visibility.Visible;
        _albumText.Text = info.Album;
        _albumButton.Visibility = string.IsNullOrEmpty(info.Album) || info.Album == info.Title ? Visibility.Collapsed : Visibility.Visible;
        _titleButton.IsEnabled = info.IsAlbumAvailable;
        _artistButton.IsEnabled = info.IsArtistAvailable;
        _albumButton.IsEnabled = info.IsAlbumAvailable;
        ToolTipService.SetToolTip(_titleButton, info.Title);
        if (!ReferenceEquals(Artwork.Entity, playable)) Artwork.Entity = playable;
        else Artwork.Refresh();

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
        _mode.Visibility = PlayerUi.IsPlayerModeSwitchVisible ? Visibility.Visible : Visibility.Collapsed;
        PlayerUi.SetGlyph(_mode, isMusic ? PlayerGlyphs.MusicMode : PlayerGlyphs.PodcastMode,
            isMusic ? "Music mode: switch to Podcast mode (Ctrl+M)" : "Podcast mode: switch to Music mode (Ctrl+M)");
        _lyricsTab.Visibility = isMusic ? Visibility.Visible : Visibility.Collapsed;
        if (!isMusic && _isLyricsTab) SelectTab(lyrics: false);

        var context = isMusic ? player.ContextName : "";
        ContextText.Text = string.IsNullOrEmpty(context) ? "" : $"Playing from: {context}";
        ContextText.Visibility = string.IsNullOrEmpty(context) ? Visibility.Collapsed : Visibility.Visible;
        UpdateLayoutForSize();
    }
}
