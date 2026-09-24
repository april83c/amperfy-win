using Amperfy.App.Controls.Player;
using Amperfy.App.Pages;
using Amperfy.App.Services.Audio;
using Amperfy.Core.Common;
using Amperfy.Core.Downloads;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Amperfy.Core.Storage;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Amperfy.App.Services.Player;

/// Player specific glyphs (Segoe Fluent Icons).
public static class PlayerGlyphs
{
    public const string Volume0 = "";
    public const string Volume1 = "";
    public const string Volume2 = "";
    public const string Volume3 = "";
    public const string FullScreen = "";
    public const string BackToWindow = "";
    public const string PlaybackRate = "";
    public const string Cached = "";
    public const string Streaming = "";
    public const string MusicMode = "";
    public const string PodcastMode = "";
    public const string Sleep = "";
    public const string Autoplay = "";
    public const string Gripper = "";
    public const string Pin = "";
    public const string Unpin = "";
    public const string OpenMainWindow = "";
    public const string Live = "";
    public const string ScrollToCurrent = "";
    public const string AddToQueue = "";
    public const string PlayNext = "";
}

/// Current item texts (port of PlayerUIHandler.refreshCurrentlyPlayingInfo).
public sealed record CurrentlyPlayingInfo(string Title, string Artist, string Album, bool IsAlbumAvailable, bool IsArtistAvailable);

/// Shared player UI logic (port of PlayerUIHandler.swift + the player menus of the Swift app): commands, display
/// helpers, navigation, menus and the side panes / windows of the player.
public static class PlayerUi
{
    private const string Log = "PlayerUi";
    private static float _volumeBeforeMute = 1.0f;
    private static QueueView? _queuePane;
    private static LyricsView? _lyricsPane;

    private static AppServices Services => AppServices.Instance;

    public static IPlayerFacade Player => Services.Player;

    /// UI state that isn't a player notification changed (volume, autoplay, side pane, sleep timer, mini player).
    public static event Action? UiStateChanged;

    public static void NotifyUiStateChanged() => UiStateChanged?.Invoke();

    /// The artwork of the currently playing item was downloaded.
    public static event Action? CurrentArtworkChanged;

    /// DownloadFinishedSuccess handler (registered by PlayerUiService).
    internal static void OnDownloadFinished(NotificationArgs args)
    {
        if (args.Payload is not DownloadNotification notification || Services.PlayerComponents.Player.CurrentlyPlaying is not { } playable) return;
        if (playable.UniqueId() == notification.Id || (playable.Artwork is { } artwork && artwork.UniqueId() == notification.Id))
        {
            CurrentArtworkChanged?.Invoke();
        }
    }

    // --- commands (PlayerUIHandler) -----------------------------------------------------------

    public static void TogglePlayPause() => Player.TogglePlayPause();

    public static void Previous()
    {
        if (Player.PlayerMode == PlayerMode.Music) Player.PlayPreviousOrReplay();
        else Player.SkipBackward(Player.SkipBackwardPodcastInterval);
    }

    public static void Next()
    {
        if (Player.PlayerMode == PlayerMode.Music) Player.PlayNext();
        else Player.SkipForward(Player.SkipForwardPodcastInterval);
    }

    public static void SkipBackward() => Player.SkipBackward(Player.SkipBackwardInterval);

    public static void SkipForward() => Player.SkipForward(Player.SkipForwardInterval);

    public static void Stop() => Player.Stop();

    public static bool IsShuffleEnabled => Services.Settings.User.IsPlayerShuffleButtonEnabled;

    public static void ToggleShuffle()
    {
        if (!IsShuffleEnabled || Player.PlayerMode != PlayerMode.Music) return;
        Player.ToggleShuffle();
    }

    public static void CycleRepeat()
    {
        if (Player.PlayerMode != PlayerMode.Music) return;
        Player.SetRepeatMode(Player.RepeatMode.NextMode());
    }

    public static bool IsAutoplayEnabled => Services.Settings.User.IsAutoplayEnabled;

    public static void ToggleAutoplay()
    {
        Services.Settings.User.IsAutoplayEnabled = !Services.Settings.User.IsAutoplayEnabled;
        NotifyUiStateChanged();
    }

    /// Lyrics are only provided by (Open)Subsonic servers (Swift isLyricsButtonAllowedToDisplay).
    public static bool IsLyricsAvailable =>
        Player.PlayerMode == PlayerMode.Music && Services.Settings.Accounts.AvailableApiTypes.Contains(ServerApiType.Subsonic);

    public static bool IsPlayerModeSwitchVisible => Player.PodcastItemCount > 0 || Player.PlayerMode == PlayerMode.Podcast;

    public static void SwitchPlayerMode() => Player.SetPlayerMode(Player.PlayerMode.NextMode());

    public static float Volume => Player.Volume;

    public static void SetVolume(float volume)
    {
        volume = Math.Clamp(volume, 0.0f, 1.0f);
        if (Math.Abs(Player.Volume - volume) < 0.0001f) return;
        Player.Volume = volume;
        Services.Settings.User.PlayerVolume = volume;
        NotifyUiStateChanged();
    }

    public static void ChangeVolume(float delta) => SetVolume(Player.Volume + delta);

    public static void ToggleMute()
    {
        if (Player.Volume > 0)
        {
            _volumeBeforeMute = Player.Volume;
            SetVolume(0);
        }
        else
        {
            SetVolume(_volumeBeforeMute > 0.01f ? _volumeBeforeMute : 0.5f);
        }
    }

    public static string VolumeGlyph(float volume) => volume switch
    {
        <= 0.001f => Amperfy.App.Helpers.Icons.Mute,
        < 0.34f => PlayerGlyphs.Volume1,
        < 0.67f => PlayerGlyphs.Volume2,
        _ => PlayerGlyphs.Volume3,
    };

    /// Repaints all observers after a queue change that the player core doesn't notify (remove/move/clear).
    public static void NotifyQueueModified() => Services.PlayerComponents.MusicPlayer.NotifyPlaylistUpdated();

    public static void ClearPlayer() => Player.ClearQueues();

    public static void ClearUserQueue()
    {
        if (Player.UserQueueCount == 0) return;
        Player.ClearUserQueue();
        NotifyQueueModified();
    }

    public static void ClearContextQueue()
    {
        Player.ClearContextQueue();
        NotifyQueueModified();
    }

    public static bool HasAnythingInPlayer =>
        Player.CurrentlyPlaying is not null || Player.PrevQueueCount > 0 || Player.UserQueueCount > 0 || Player.NextQueueCount > 0;

    /// Favorite toggle (songs) / radio web site (radios) like the Swift popup player.
    public static async Task ToggleFavoriteAsync(AbstractPlayable? playable)
    {
        if (playable is null) return;
        try
        {
            if (playable.AsRadio is { } radio)
            {
                if (Uri.TryCreate(radio.SiteUrl, UriKind.Absolute, out var site)) await Windows.System.Launcher.LaunchUriAsync(site);
                return;
            }
            if (!playable.IsFavoritable || playable.Account?.Info is not { } info) return;
            if (Services.Settings.User.IsOfflineMode) return;
            await playable.RemoteToggleFavoriteAsync(Services.Library, Services.Kit.GetMeta(info).LibrarySyncer);
        }
        catch (Exception ex)
        {
            Services.EventLogger.Report("Toggle Favorite", ex);
        }
        finally
        {
            NotifyUiStateChanged();
        }
    }

    // --- display ------------------------------------------------------------------------------

    public static CurrentlyPlayingInfo CurrentInfo()
    {
        var player = Player;
        if (player.CurrentlyPlaying is not { } playable)
        {
            return new CurrentlyPlayingInfo(
                player.PlayerMode == PlayerMode.Music ? "No music playing" : "No podcast playing", "", "", false, false);
        }
        if (playable.IsRadio)
        {
            if (player.CurrentRadioNowPlaying is { IsEmpty: false } radioInfo)
                return new CurrentlyPlayingInfo(radioInfo.Title, radioInfo.Artist, playable.Title, false, false);
            return new CurrentlyPlayingInfo(playable.Title, "", playable.Title, false, false);
        }
        var song = playable.AsSong;
        return new CurrentlyPlayingInfo(
            playable.Title,
            playable.CreatorName,
            song?.Album?.Name ?? "",
            song?.Album is not null || playable.AsPodcastEpisode?.Podcast is not null,
            song?.Artist is not null || playable.AsPodcastEpisode?.Podcast is not null);
    }

    public static string PlayGlyph => Player.IsPlaying
        ? (Player.IsStopInsteadOfPause ? Amperfy.App.Helpers.Icons.Stop : Amperfy.App.Helpers.Icons.Pause)
        : Amperfy.App.Helpers.Icons.Play;

    public static string PlayTooltip => Player.IsPlaying
        ? (Player.IsStopInsteadOfPause ? "Stop (Space)" : "Pause (Space)")
        : "Play (Space)";

    public static string PreviousGlyph => Player.PlayerMode == PlayerMode.Music ? Amperfy.App.Helpers.Icons.Previous : Amperfy.App.Helpers.Icons.SkipBack;

    public static string NextGlyph => Player.PlayerMode == PlayerMode.Music ? Amperfy.App.Helpers.Icons.Next : Amperfy.App.Helpers.Icons.SkipForward;

    public static string PreviousTooltip => Player.PlayerMode == PlayerMode.Music
        ? "Previous (Ctrl+Left)"
        : $"Skip back {(int)Player.SkipBackwardPodcastInterval} s (Ctrl+Left)";

    public static string NextTooltip => Player.PlayerMode == PlayerMode.Music
        ? "Next (Ctrl+Right)"
        : $"Skip forward {(int)Player.SkipForwardPodcastInterval} s (Ctrl+Right)";

    public static string RepeatGlyph => Player.RepeatMode == RepeatMode.Single ? Amperfy.App.Helpers.Icons.RepeatOne : Amperfy.App.Helpers.Icons.Repeat;

    public static string RepeatTooltip => $"Repeat: {Player.RepeatMode.Description()} (Ctrl+T)";

    /// "-1:23" style remaining time (Swift: elapsed - ceil(duration)).
    public static string RemainingTimeText(double elapsed, double duration)
    {
        if (!double.IsFinite(duration) || duration <= 0) return "--:--";
        var remaining = (int)(Math.Ceiling(duration) - Math.Max(0, elapsed));
        return "-" + Math.Max(0, remaining).AsColonDurationString();
    }

    public static string ElapsedTimeText(double elapsed) => Math.Max(0, elapsed).AsColonDurationString();

    /// Audio format info like "FLAC 1024 kbps" and whether it is played from cache (port of refreshAudioInfo).
    public static (string Text, bool IsCached)? AudioInfo()
    {
        var player = Player;
        if (player.CurrentlyPlaying is not { } playable || player.PlayType is not { } playType) return null;
        int kbps;
        string format;
        if (playType == PlayType.Cache)
        {
            kbps = playable.Bitrate / 1000;
            format = FormatOf(playable.FileContentType);
        }
        else
        {
            var bitrate = player.ActiveStreamingBitrate;
            kbps = bitrate is null ? 0
                : bitrate == StreamingMaxBitratePreference.NoLimit || (int)bitrate > playable.Bitrate / 1000 ? playable.Bitrate / 1000
                : (int)bitrate;
            format = player.ActiveTranscodingFormat switch
            {
                null => "",
                StreamingFormatPreference.Raw => FormatOf(playable.ContentType),
                { } f => f.ShortInfo(),
            };
        }
        var text = kbps > 0 ? $"{format} {kbps} kbps".Trim() : format;
        return (text, playType == PlayType.Cache);
    }

    private static string FormatOf(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return "";
        var parts = contentType.Split('/');
        if (parts.Length < 2) return "";
        var format = parts[1].Split(';')[0].Trim().ToUpperInvariant();
        if (format is "MP3" or "MPEG") return "MP3";
        if (format.Contains("LOSSLESS", StringComparison.Ordinal)) return "LOSSLESS";
        if (format.StartsWith("X-", StringComparison.Ordinal) && format.Length > 2) return format[2..];
        return format;
    }

    // --- navigation ---------------------------------------------------------------------------

    /// Shows the detail page of a library entity in the main window.
    public static void ShowEntity(object? entity)
    {
        if (entity is null || PageRegistry.ForEntity(entity) is not { } target) return;
        BringMainWindowToFront();
        Services.Navigation.Navigate(target.Page, target.Parameter);
    }

    /// Shows the album of a song (scrolled to the song) or the podcast of an episode (scrolled to the episode).
    public static void ShowAlbum(AbstractPlayable? playable)
    {
        if (playable?.AsSong is { Album: { } album } song) ShowEntity(album, song);
        else if (playable?.AsPodcastEpisode is { Podcast: { } podcast } episode) ShowEntity(podcast, episode);
    }

    /// Shows the detail page of an entity and scrolls to one of its elements.
    public static void ShowEntity(object entity, object scrollTo)
    {
        if (PageRegistry.ForEntity(entity) is not { } target) return;
        BringMainWindowToFront();
        Services.Navigation.Navigate(target.Page, new Amperfy.App.Library.EntityNavigationArgs(target.Parameter, scrollTo));
    }

    public static void ShowArtist(AbstractPlayable? playable)
    {
        if (playable?.AsSong?.Artist is { } artist) ShowEntity(artist);
        else if (playable?.AsPodcastEpisode?.Podcast is { } podcast) ShowEntity(podcast);
    }

    /// "Go to Current Song" (album of the current song).
    public static void GoToCurrent() => ShowAlbum(Player.CurrentlyPlaying);

    public static void BringMainWindowToFront()
    {
        try
        {
            var window = Services.MainWindow;
            if (window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
            window.Activate();
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(Log, $"Activating the main window failed: {ex.Message}");
        }
    }

    // --- side panes / now playing / mini player ---------------------------------------------

    public static bool IsQueuePaneVisible => _queuePane is not null && ShellPage.Current?.SidePaneContent == _queuePane;

    public static bool IsLyricsPaneVisible => _lyricsPane is not null && ShellPage.Current?.SidePaneContent == _lyricsPane;

    public static void ToggleQueuePane() => ShowQueuePane(!IsQueuePaneVisible);

    public static void ToggleLyricsPane() => ShowLyricsPane(!IsLyricsPaneVisible);

    /// Shows the synced lyrics of the currently playing song: the lyrics tab of the now playing page when it is
    /// open, otherwise the lyrics side pane. False if lyrics can't be shown in the player (e.g. podcast mode).
    public static bool ShowCurrentLyrics()
    {
        if (!IsLyricsAvailable || ShellPage.Current is null) return false;
        BringMainWindowToFront();
        if (Services.Navigation.Frame?.Content is NowPlayingPage nowPlaying)
        {
            nowPlaying.ShowLyricsTab();
            return true;
        }
        ShowLyricsPane(true);
        return true;
    }

    public static void ShowQueuePane(bool show)
    {
        if (ShellPage.Current is not { } shell) return;
        if (show)
        {
            _queuePane ??= new QueueView { ShowsTitle = true };
            DetachFromParent(_queuePane);
            shell.ShowSidePane(_queuePane);
            _queuePane.ScrollToCurrent();
        }
        else if (IsQueuePaneVisible)
        {
            shell.ShowSidePane(null);
        }
        NotifyUiStateChanged();
    }

    public static void ShowLyricsPane(bool show)
    {
        if (ShellPage.Current is not { } shell) return;
        if (show)
        {
            _lyricsPane ??= new LyricsView { ShowsTitle = true };
            DetachFromParent(_lyricsPane);
            shell.ShowSidePane(_lyricsPane);
        }
        else if (IsLyricsPaneVisible)
        {
            shell.ShowSidePane(null);
        }
        NotifyUiStateChanged();
    }

    /// A view can only have one parent: remove it from its previous host (e.g. the side pane of an old shell page).
    public static void DetachFromParent(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Border border:
                border.Child = null;
                break;
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case ContentControl content:
                content.Content = null;
                break;
        }
    }

    public static bool IsNowPlayingVisible => Services.Navigation.CurrentPageType == typeof(NowPlayingPage);

    public static void ToggleNowPlaying()
    {
        if (IsNowPlayingVisible)
        {
            if (Services.Navigation.CanGoBack) Services.Navigation.GoBack();
            else Services.Navigation.Navigate(typeof(HomePage));
        }
        else
        {
            BringMainWindowToFront();
            Services.Navigation.Navigate(typeof(NowPlayingPage));
        }
    }

    public static void ToggleMiniPlayer() => MiniPlayerWindow.Toggle();

    // --- menus --------------------------------------------------------------------------------

    /// Sleep timer menu (port of AppDelegate.createSleepTimerMenu).
    public static MenuFlyoutSubItem CreateSleepTimerMenuItem()
    {
        var item = new MenuFlyoutSubItem { Text = "Sleep Timer", Icon = new FontIcon { Glyph = PlayerGlyphs.Sleep } };
        foreach (var child in CreateSleepTimerItems()) item.Items.Add(child);
        return item;
    }

    public static MenuFlyout CreateSleepTimerFlyout()
    {
        var flyout = new MenuFlyout();
        flyout.Opening += (_, _) =>
        {
            flyout.Items.Clear();
            foreach (var child in CreateSleepTimerItems()) flyout.Items.Add(child);
        };
        return flyout;
    }

    private static IEnumerable<MenuFlyoutItemBase> CreateSleepTimerItems()
    {
        var sleepTimer = Services.SleepTimer;
        if (sleepTimer.IsActive)
        {
            var off = new MenuFlyoutItem { Text = $"Turn Off ({sleepTimer.StatusDescription})" };
            off.Click += (_, _) =>
            {
                sleepTimer.Deactivate();
                NotifyUiStateChanged();
            };
            yield return off;
            yield break;
        }
        foreach (var option in SleepTimer.Options)
        {
            var item = new MenuFlyoutItem { Text = option.Title };
            item.Click += (_, _) =>
            {
                sleepTimer.Activate(option);
                NotifyUiStateChanged();
            };
            yield return item;
            if (option.IsEndOfTrack) yield return new MenuFlyoutSeparator();
        }
    }

    public static MenuFlyoutSubItem CreatePlaybackRateMenuItem()
    {
        var item = new MenuFlyoutSubItem
        {
            Text = $"Playback Rate ({Player.PlaybackRate.Description()})",
            Icon = new FontIcon { Glyph = PlayerGlyphs.PlaybackRate },
        };
        foreach (var child in CreatePlaybackRateItems()) item.Items.Add(child);
        return item;
    }

    public static MenuFlyout CreatePlaybackRateFlyout()
    {
        var flyout = new MenuFlyout();
        flyout.Opening += (_, _) =>
        {
            flyout.Items.Clear();
            foreach (var child in CreatePlaybackRateItems()) flyout.Items.Add(child);
        };
        return flyout;
    }

    private static IEnumerable<MenuFlyoutItemBase> CreatePlaybackRateItems()
    {
        var current = Player.PlaybackRate;
        foreach (var rate in Enum.GetValues<PlaybackRate>())
        {
            var item = new RadioMenuFlyoutItem { Text = rate.Description(), GroupName = "PlaybackRate", IsChecked = rate == current };
            item.Click += (_, _) => Player.SetPlaybackRate(rate);
            yield return item;
        }
    }

    /// The "more" menu of the player (port of PlayerControlView.createPlayerOptionsMenu + the Controls menu).
    public static MenuFlyout CreatePlayerOptionsFlyout(Action? scrollToCurrent = null)
    {
        var flyout = new MenuFlyout();
        flyout.Opening += (_, _) =>
        {
            flyout.Items.Clear();
            foreach (var item in CreatePlayerOptionsItems(scrollToCurrent)) flyout.Items.Add(item);
        };
        return flyout;
    }

    private static IEnumerable<MenuFlyoutItemBase> CreatePlayerOptionsItems(Action? scrollToCurrent)
    {
        var player = Player;
        if (HasAnythingInPlayer)
        {
            yield return MenuItem("Clear Player", Amperfy.App.Helpers.Icons.Clear, ClearPlayer);
        }
        if (player.UserQueueCount > 0)
        {
            yield return MenuItem("Clear User Queue", Amperfy.App.Helpers.Icons.Delete, ClearUserQueue);
        }
        if (player.PlayerMode == PlayerMode.Music && player.NextQueueCount > 0)
        {
            yield return MenuItem("Clear Context Queue", Amperfy.App.Helpers.Icons.Delete, ClearContextQueue);
        }
        yield return new MenuFlyoutSeparator();
        yield return CreateSleepTimerMenuItem();
        yield return CreatePlaybackRateMenuItem();
        if (player.PlayerMode == PlayerMode.Music)
        {
            yield return new ToggleMenuFlyoutItem { Text = "Autoplay", IsChecked = IsAutoplayEnabled, Icon = new FontIcon { Glyph = PlayerGlyphs.Autoplay } }
                .WithClick(ToggleAutoplay);
        }
        if (IsPlayerModeSwitchVisible)
        {
            yield return MenuItem(player.PlayerMode == PlayerMode.Music ? "Switch to Podcast mode" : "Switch to Music mode",
                player.PlayerMode == PlayerMode.Music ? PlayerGlyphs.PodcastMode : PlayerGlyphs.MusicMode, SwitchPlayerMode);
        }
        yield return new MenuFlyoutSeparator();
        if (player.CurrentlyPlaying?.AsSong?.Album is not null)
        {
            yield return MenuItem("Go to Current Song (Ctrl+L)", Amperfy.App.Helpers.Icons.Album, GoToCurrent);
        }
        if (scrollToCurrent is not null)
        {
            yield return MenuItem("Scroll to currently playing", PlayerGlyphs.ScrollToCurrent, scrollToCurrent);
        }
        if (IsLyricsAvailable || IsLyricsPaneVisible)
        {
            yield return MenuItem(IsLyricsPaneVisible ? "Hide Lyrics" : "Show Lyrics", Amperfy.App.Helpers.Icons.Lyrics, ToggleLyricsPane);
        }
        yield return MenuItem(IsQueuePaneVisible ? "Hide Queue" : "Show Queue", Amperfy.App.Helpers.Icons.Queue, ToggleQueuePane);
        yield return MenuItem(MiniPlayerWindow.IsOpen ? "Close Mini Player" : "Open Mini Player", Amperfy.App.Helpers.Icons.MiniPlayer, ToggleMiniPlayer);
        yield return new MenuFlyoutSeparator();
        yield return MenuItem("Player Info", Amperfy.App.Helpers.Icons.Info, () => _ = ShowPlayerInfoAsync());
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => action();
        return item;
    }

    private static T WithClick<T>(this T item, Action action) where T : MenuFlyoutItem
    {
        item.Click += (_, _) => action();
        return item;
    }

    /// Player info dialog (port of PlainDetailsVC.display(player:), extended with the audio details).
    public static async Task ShowPlayerInfoAsync()
    {
        var player = Player;
        var user = Services.Settings.User;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Play Time");
        sb.AppendLine($"Remaining: {player.RemainingPlayDuration.AsDurationString()}");
        sb.AppendLine($"Total: {player.TotalPlayDuration.AsDurationString()}");
        sb.AppendLine();
        sb.AppendLine("Queue Items");
        sb.AppendLine($"Previous: {player.PrevQueueCount}");
        sb.AppendLine($"User: {player.UserQueueCount}");
        sb.AppendLine($"Next: {player.NextQueueCount}");
        sb.AppendLine();
        sb.AppendLine("Audio");
        sb.AppendLine($"Mode: {player.PlayerMode.Description()}");
        if (player.PlayType is { } playType) sb.AppendLine($"Source: {(playType == PlayType.Cache ? "Cache" : "Stream")}");
        if (AudioInfo() is { } info && info.Text.Length > 0) sb.AppendLine($"Format: {info.Text}");
        sb.AppendLine($"Engine: {AudioBackend.CurrentEngine?.ActiveEngineName ?? "-"}");
        sb.AppendLine($"Equalizer: {(user.IsEqualizerEnabled ? user.ActiveEqualizerSetting.Name : "Off")}");
        sb.AppendLine($"Replay Gain: {(user.IsReplayGainEnabled ? "On" : "Off")} ({Services.PlayerComponents.BackendAudioPlayer.ReplayGainOutputVolume:F2} linear)");
        sb.AppendLine($"Volume: {(int)Math.Round(player.Volume * 100)} %");
        sb.AppendLine($"Playback Rate: {player.PlaybackRate.Description()}");
        await Services.Dialogs.ShowMessageAsync("Player Info", sb.ToString().TrimEnd());
    }

    // --- control factories --------------------------------------------------------------------

    public static Button CreateIconButton(string glyph, string tooltip, Action onClick, double size = 36, double iconSize = 16)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = iconSize },
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            // clicking must not move the focus (Space would trigger the button again instead of play/pause)
            AllowFocusOnInteraction = false,
        };
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    public static ToggleButton CreateToggleButton(string glyph, string tooltip, Action onClick, double size = 36, double iconSize = 16)
    {
        var button = new ToggleButton
        {
            Content = new FontIcon { Glyph = glyph, FontSize = iconSize },
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            // transparent when unchecked; the checked state of the template shows the accent fill
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            AllowFocusOnInteraction = false,
        };
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    public static void SetGlyph(ContentControl button, string glyph, string? tooltip = null)
    {
        if (button.Content is FontIcon icon) icon.Glyph = glyph;
        else button.Content = new FontIcon { Glyph = glyph };
        if (tooltip is not null)
        {
            ToolTipService.SetToolTip(button, tooltip);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        }
    }

    /// Makes a button an accent colored (filled) button.
    public static void ApplyAccentStyle(Button button)
    {
        if (!Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style) || style is not Style s) return;
        // local values would override the style's accent background/border
        button.ClearValue(Control.BackgroundProperty);
        button.ClearValue(Control.BorderThicknessProperty);
        button.Style = s;
    }

    /// Volume button with a slider flyout; the mouse wheel over the button changes the volume.
    public static Button CreateVolumeButton(double size = 36)
    {
        var button = CreateIconButton(VolumeGlyph(Volume), "Volume (Ctrl+Up / Ctrl+Down)", () => { }, size);
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            StepFrequency = 1,
            Width = 200,
            Value = Math.Round(Volume * 100),
        };
        var isUpdating = false;
        slider.ValueChanged += (_, e) =>
        {
            if (isUpdating) return;
            SetVolume((float)(e.NewValue / 100.0));
        };
        var mute = CreateIconButton(VolumeGlyph(Volume), "Mute", ToggleMute, 32);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(mute);
        panel.Children.Add(slider);
        var flyout = new Flyout { Content = panel, Placement = FlyoutPlacementMode.Top };
        button.Flyout = flyout;
        void Refresh()
        {
            isUpdating = true;
            slider.Value = Math.Round(Volume * 100);
            isUpdating = false;
            SetGlyph(button, VolumeGlyph(Volume), $"Volume {(int)Math.Round(Volume * 100)} % (Ctrl+Up / Ctrl+Down)");
            SetGlyph(mute, VolumeGlyph(Volume));
        }
        button.Loaded += (_, _) =>
        {
            UiStateChanged += Refresh;
            Refresh();
        };
        button.Unloaded += (_, _) => UiStateChanged -= Refresh;
        button.PointerWheelChanged += (_, e) =>
        {
            var delta = e.GetCurrentPoint(button).Properties.MouseWheelDelta;
            ChangeVolume(delta > 0 ? 0.05f : -0.05f);
            e.Handled = true;
        };
        return button;
    }

    /// Button with the sleep timer menu (accent colored while a timer is active).
    public static ToggleButton CreateSleepTimerButton(double size = 36)
    {
        var button = new ToggleButton
        {
            Content = new FontIcon { Glyph = PlayerGlyphs.Sleep, FontSize = 16 },
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            AllowFocusOnInteraction = false,
        };
        var flyout = CreateSleepTimerFlyout();
        void Refresh()
        {
            var timer = Services.SleepTimer;
            button.IsChecked = timer.IsActive;
            var tip = timer.StatusDescription is { } status ? $"Sleep Timer ({status})" : "Sleep Timer";
            ToolTipService.SetToolTip(button, tip);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tip);
        }
        button.Click += (_, _) =>
        {
            Refresh(); // the toggle state follows the timer, not the click
            flyout.ShowAt(button);
        };
        void OnTimerChanged() => Refresh();
        button.Loaded += (_, _) =>
        {
            Services.SleepTimer.StateChanged += OnTimerChanged;
            UiStateChanged += OnTimerChanged;
            Refresh();
        };
        button.Unloaded += (_, _) =>
        {
            Services.SleepTimer.StateChanged -= OnTimerChanged;
            UiStateChanged -= OnTimerChanged;
        };
        return button;
    }

    /// Button showing the playback rate ("1x") with the rate menu.
    public static Button CreatePlaybackRateButton(double height = 36)
    {
        var text = new TextBlock { Text = Player.PlaybackRate.Description(), VerticalAlignment = VerticalAlignment.Center };
        var button = new Button
        {
            Content = text,
            MinWidth = 44,
            Height = height,
            Padding = new Thickness(6, 0, 6, 0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            AllowFocusOnInteraction = false,
            Flyout = CreatePlaybackRateFlyout(),
        };
        ToolTipService.SetToolTip(button, "Playback Rate");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Playback Rate");
        var observer = new PlayerObserver();
        observer.PlaybackRateChanged += () => text.Text = Player.PlaybackRate.Description();
        observer.PlaylistChanged += () => text.Text = Player.PlaybackRate.Description();
        observer.StartedPlaying += () => text.Text = Player.PlaybackRate.Description();
        button.Tag = observer; // keeps the observer alive as long as the button
        button.Loaded += (_, _) =>
        {
            observer.Register().IsActive = true;
            text.Text = Player.PlaybackRate.Description();
        };
        button.Unloaded += (_, _) => observer.IsActive = false;
        return button;
    }

}
