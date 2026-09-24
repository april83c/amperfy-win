using Amperfy.App.Helpers;
using Amperfy.App.Library.Dialogs;
using Amperfy.App.Pages;
using Amperfy.App.Services;
using Amperfy.Core.Api;
using Amperfy.Core.Common;
using Amperfy.Core.Downloads;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Amperfy.App.Library;

/// Options of an entity context menu (Swift: EntityPreviewActionBuilder parameters).
public sealed class EntityActionOptions
{
    /// Play context of a list element (Swift: playContextCb). Songs/episodes/radios only offer
    /// "Play" and the queue actions when a play context provider is given.
    public Func<PlayContext?>? PlayContext { get; init; }

    /// Index in the player queue (Swift: playerIndexCb, used by the queue view): "Play" jumps to it.
    public Func<PlayerIndex?>? PlayerIndex { get; init; }

    /// Page type the menu is shown on (hides "Show Album" on the album page etc.).
    public Type? HostPageType { get; init; }

    /// Called after an action changed the entity (favorite, rating, cache).
    public Action? Changed { get; init; }

    /// Additional items appended at the end of the menu (e.g. "Remove from Playlist").
    public Func<IEnumerable<MenuFlyoutItemBase>>? ExtraItems { get; init; }
}

/// Context menu actions of library entities (port of EntityPreviewActionBuilder and
/// CommonScreenOperations): play, shuffle, instant mix, queue, navigation, lyrics, descriptions,
/// favorite, rating, add to playlist, download, delete cache, delete on server, go to site, copy id.
/// Also provides navigation helpers to open the detail page of an entity.
public static class EntityActions
{
    private static AppServices Services => AppServices.Instance;
    private static bool IsOnline => Services.Settings.User.IsOnlineMode;
    private static bool IsOffline => Services.Settings.User.IsOfflineMode;

    /// Warn before adding more songs than this to the download queue at once (Swift AppDelegate constant).
    public const int MaxPlayablesDownloadsToAddAtOnceWithoutWarning = 200;

    /// Shows the lyrics of a song. Replace it to show lyrics in the player's lyrics view; the
    /// default shows the lyrics text in a dialog.
    public static Action<Song>? ShowLyricsHandler { get; set; }

    private static readonly Dictionary<IPlayableContainable, Task> RunningFetches = new(ReferenceEqualityComparer.Instance);

    // --- menu ------------------------------------------------------------------------------------

    /// Context menu of a container. The items are built when the menu opens (current state).
    public static MenuFlyout CreateMenuFlyout(IPlayableContainable container, EntityActionOptions? options = null) =>
        CreateMenuFlyout(() => (container, options));

    /// Context menu whose target is resolved when it opens (for recycled list rows).
    public static MenuFlyout CreateMenuFlyout(Func<(IPlayableContainable Container, EntityActionOptions? Options)?> resolve)
    {
        var flyout = new MenuFlyout();
        flyout.Opening += (_, _) =>
        {
            flyout.Items.Clear();
            try
            {
                if (resolve() is { } target)
                {
                    _ = PrefetchAsync(target.Container);
                    foreach (var item in BuildMenuItems(target.Container, target.Options)) flyout.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                AmperfyLog.Error("EntityActions", $"Building the context menu failed: {ex}");
            }
            if (flyout.Items.Count == 0) flyout.Items.Add(new MenuFlyoutItem { Text = "No actions available", IsEnabled = false });
        };
        return flyout;
    }

    /// The menu items for a container (Swift: createMenuActions).
    public static List<MenuFlyoutItemBase> BuildMenuItems(IPlayableContainable container, EntityActionOptions? options = null)
    {
        options ??= new EntityActionOptions();
        var config = Configure(container, options);
        var groups = new List<List<MenuFlyoutItemBase>>();

        var playActions = new List<MenuFlyoutItemBase>();
        if (config.IsPlay) playActions.Add(Ui.MenuItem("Play", Icons.Play, () => _ = PlayAsync(container, options)));
        if (config.IsShuffle) playActions.Add(Ui.MenuItem("Shuffle", Icons.Shuffle, () => _ = ShuffleAsync(container, options)));
        if (config.IsInstantMix && container is Song mixSong) playActions.Add(Ui.MenuItem("Instant Mix", LibraryGlyphs.InstantMix, () => _ = PlayInstantMixAsync(mixSong)));
        groups.Add(playActions);

        var queueActions = new List<MenuFlyoutItemBase>();
        if (config.IsMusicQueue)
        {
            var queue = new MenuFlyoutSubItem { Text = "Music Queue", Icon = new FontIcon { Glyph = Icons.Queue } };
            queue.Items.Add(Ui.MenuItem("Insert Context Queue", LibraryGlyphs.QueueInsert, () => _ = QueueAsync(container, p => Services.Player.InsertContextQueue(p))));
            queue.Items.Add(Ui.MenuItem("Append Context Queue", LibraryGlyphs.QueueAppend, () => _ = QueueAsync(container, p => Services.Player.AppendContextQueue(p))));
            queue.Items.Add(Ui.MenuItem("Insert User Queue", LibraryGlyphs.QueueInsert, () => _ = QueueAsync(container, p => Services.Player.InsertUserQueue(p))));
            queue.Items.Add(Ui.MenuItem("Append User Queue", LibraryGlyphs.QueueAppend, () => _ = QueueAsync(container, p => Services.Player.AppendUserQueue(p))));
            queueActions.Add(queue);
        }
        if (config.IsPodcastQueue)
        {
            queueActions.Add(Ui.MenuItem("Insert Podcast Queue", LibraryGlyphs.QueueInsert, () => _ = QueueAsync(container, p => Services.Player.InsertPodcastQueue(p))));
            queueActions.Add(Ui.MenuItem("Append Podcast Queue", LibraryGlyphs.QueueAppend, () => _ = QueueAsync(container, p => Services.Player.AppendPodcastQueue(p))));
        }
        groups.Add(queueActions);

        var gotoActions = new List<MenuFlyoutItemBase>();
        if (config.IsShowAlbum && container is Song { Album: { } album } albumSong)
            gotoActions.Add(Ui.MenuItem("Show Album", Icons.Album, () => Open(album, albumSong)));
        if (config.IsShowArtist)
        {
            switch (container)
            {
                case Song { Artist: { } songArtist }:
                    gotoActions.Add(Ui.MenuItem("Show Artist", Icons.Artist, () => Open(songArtist)));
                    break;
                case Album { Artist: { } albumArtist } showAlbum:
                    gotoActions.Add(Ui.MenuItem("Show Artist", Icons.Artist, () => Open(albumArtist, showAlbum)));
                    break;
                case PodcastEpisode { Podcast: { } podcast } showEpisode:
                    gotoActions.Add(Ui.MenuItem("Show Podcast", Icons.Podcast, () => Open(podcast, showEpisode)));
                    break;
            }
        }
        if (config.IsShowSongDetails && container is Song { LyricsRelFilePath: not null } lyricsSong)
            gotoActions.Add(Ui.MenuItem("Show Lyrics", Icons.Lyrics, () => ShowLyrics(lyricsSong)));
        if (config.IsShowPodcastDetails && container is PodcastEpisode descriptionEpisode)
            gotoActions.Add(Ui.MenuItem("Show Episode Description", Icons.Info, () => _ = ShowDescriptionAsync(descriptionEpisode)));
        if (config.IsShowPodcastDetails && container is Podcast descriptionPodcast)
            gotoActions.Add(Ui.MenuItem("Show Podcast Description", Icons.Info, () => _ = ShowDescriptionAsync(descriptionPodcast)));
        groups.Add(gotoActions);

        var ratingFavActions = new List<MenuFlyoutItemBase>();
        if (container is AbstractLibraryEntity favEntity && container.IsFavoritable && IsOnline)
        {
            ratingFavActions.Add(favEntity.IsFavorite
                ? Ui.MenuItem("Unmark favorite", Icons.Heart, () => _ = ToggleFavoriteAsync(container, options.Changed))
                : Ui.MenuItem("Favorite", Icons.HeartFill, () => _ = ToggleFavoriteAsync(container, options.Changed)));
        }
        if (container is AbstractLibraryEntity ratingEntity && container.IsRateable && IsOnline)
        {
            var ratingText = ratingEntity.Rating == 0 ? "Not rated" : $"{ratingEntity.Rating} Star{(ratingEntity.Rating > 1 ? "s" : "")}";
            var rating = new MenuFlyoutSubItem
            {
                Text = $"Rating: {ratingText}",
                Icon = new FontIcon { Glyph = ratingEntity.Rating == 0 ? LibraryGlyphs.Star : LibraryGlyphs.StarFill },
            };
            rating.Items.Add(Ui.RadioItem("No Rating", "rating", ratingEntity.Rating == 0, () => _ = SetRatingAsync(ratingEntity, 0, options.Changed)));
            for (var stars = 1; stars <= 5; stars++)
            {
                var value = stars;
                rating.Items.Add(Ui.RadioItem(stars == 1 ? "1 Star" : $"{stars} Stars", "rating", ratingEntity.Rating == stars,
                    () => _ = SetRatingAsync(ratingEntity, value, options.Changed)));
            }
            ratingFavActions.Add(rating);
        }
        groups.Add(ratingFavActions);

        var elementActions = new List<MenuFlyoutItemBase>();
        if (config.IsAddToPlaylist) elementActions.Add(Ui.MenuItem("Add to Playlist", Icons.Playlist, () => _ = AddToPlaylistAsync(container)));
        if (IsDownloadPossible(container)) elementActions.Add(Ui.MenuItem("Download", Icons.Download, () => _ = DownloadAsync(container)));
        if (container.Playables.HasCachedItems()) elementActions.Add(Ui.MenuItem("Delete Cache", Icons.Delete, () => _ = DeleteCacheAsync(container, options.Changed)));
        if (config.IsDeleteOnServer && container is PodcastEpisode deleteEpisode)
            elementActions.Add(Ui.MenuItem("Delete on Server", LibraryGlyphs.CloudDelete, () => _ = DeleteEpisodeOnServerAsync(deleteEpisode, options.Changed)));
        if (config.IsGoToSiteUrl && container is Radio { SiteUrl: { Length: > 0 } siteUrl })
            elementActions.Add(Ui.MenuItem("Go to Site", LibraryGlyphs.Globe, () => _ = OpenUrlAsync(siteUrl)));
        groups.Add(elementActions);

        if (options.ExtraItems?.Invoke() is { } extra) groups.Add(extra.ToList());

        if (Services.Settings.User.IsShowDetailedInfo && !string.IsNullOrEmpty(container.Id))
            groups.Add([Ui.MenuItem("Copy ID to Clipboard", LibraryGlyphs.Copy, () => CopyToClipboard(container.Id))]);

        var result = new List<MenuFlyoutItemBase>();
        foreach (var group in groups.Where(g => g.Count > 0))
        {
            if (result.Count > 0) result.Add(new MenuFlyoutSeparator());
            result.AddRange(group);
        }
        return result;
    }

    private sealed class MenuConfig
    {
        public bool IsPlay, IsShuffle, IsMusicQueue, IsPodcastQueue, IsShowAlbum, IsShowArtist, IsAddToPlaylist,
            IsDeleteOnServer, IsGoToSiteUrl, IsShowPodcastDetails, IsShowSongDetails, IsInstantMix;
    }

    private static MenuConfig Configure(IPlayableContainable container, EntityActionOptions options)
    {
        var online = IsOnline;
        var offline = !online;
        var hasContext = options.PlayContext is not null || options.PlayerIndex is not null;
        var hasPlayerIndex = options.PlayerIndex is not null;
        var shuffleEnabled = Services.Settings.User.IsPlayerShuffleButtonEnabled;
        var host = options.HostPageType;
        var c = new MenuConfig();
        switch (container)
        {
            case Song song:
                c.IsPlay = hasContext && !(!song.IsCached && offline);
                c.IsShuffle = hasContext && !hasPlayerIndex && !(!song.IsCached && offline) && shuffleEnabled;
                c.IsMusicQueue = hasContext && !hasPlayerIndex && !(!song.IsCached && offline);
                c.IsShowAlbum = host != typeof(AlbumDetailPage);
                c.IsShowArtist = host != typeof(ArtistDetailPage);
                c.IsAddToPlaylist = online;
                c.IsShowSongDetails = true;
                c.IsInstantMix = online;
                break;
            case PodcastEpisode episode:
                c.IsPlay = hasContext && !(!episode.IsAvailableToUser() && online) && !(!episode.IsCached && offline);
                c.IsPodcastQueue = hasContext && !hasPlayerIndex && !(!episode.IsAvailableToUser() && online) && !(!episode.IsCached && offline);
                c.IsShowArtist = host != typeof(PodcastDetailPage);
                c.IsDeleteOnServer = episode.PodcastStatus != PodcastEpisodeRemoteStatus.Deleted && online;
                c.IsShowPodcastDetails = true;
                break;
            case Radio radio:
                c.IsPlay = hasContext && online;
                c.IsMusicQueue = hasContext && !hasPlayerIndex && online;
                c.IsGoToSiteUrl = !string.IsNullOrEmpty(radio.SiteUrl);
                break;
            case Podcast:
                c.IsPlay = (online || container.Playables.HasCachedItems()) && shuffleEnabled;
                c.IsPodcastQueue = true;
                c.IsShowPodcastDetails = true;
                break;
            case Album:
                SetContainerDefaults(c, container, online, shuffleEnabled);
                c.IsShowArtist = host != typeof(ArtistDetailPage);
                c.IsAddToPlaylist = online;
                break;
            case MusicDirectory:
                SetContainerDefaults(c, container, online, shuffleEnabled);
                c.IsAddToPlaylist = online && container.Playables.Count > 0;
                break;
            case PlayableSelection selection:
                c.IsPlay = online || selection.Playables.HasCachedItems();
                c.IsShuffle = c.IsPlay && shuffleEnabled && selection.PlayContextType == PlayerMode.Music;
                c.IsMusicQueue = selection.PlayContextType == PlayerMode.Music;
                c.IsPodcastQueue = selection.PlayContextType == PlayerMode.Podcast;
                c.IsAddToPlaylist = online && selection.Playables.Any(p => p.IsSong);
                break;
            default: // Artist, Genre, Playlist
                SetContainerDefaults(c, container, online, shuffleEnabled);
                c.IsAddToPlaylist = online;
                break;
        }
        return c;
    }

    private static void SetContainerDefaults(MenuConfig c, IPlayableContainable container, bool online, bool shuffleEnabled)
    {
        c.IsPlay = online || container.Playables.HasCachedItems();
        c.IsShuffle = c.IsPlay && shuffleEnabled;
        c.IsMusicQueue = true;
    }

    private static bool IsDownloadPossible(IPlayableContainable container) =>
        !(IsOffline || !container.IsDownloadAvailable || container.Playables.IsCachedCompletely());

    // --- playables -------------------------------------------------------------------------------

    /// The playables of a container that can be played in the current mode (Swift: entityPlayables).
    public static List<AbstractPlayable> FilterPlayables(IPlayableContainable container)
    {
        var playables = container.Playables.FilterCached(IsOffline);
        if (container.PlayContextType == PlayerMode.Music)
            playables = playables.Where(p => p is Song s ? s.IsAvailableToUser() : p is Radio r && r.IsAvailableToUser()).ToList();
        return playables;
    }

    /// Fetches the container from the server (online mode) before its playables are used.
    /// Concurrent requests for the same container share the running fetch.
    public static Task PrefetchAsync(IPlayableContainable container)
    {
        if (!IsOnline || container is PlayableSelection or Radio or PodcastEpisode || container.Account is null) return Task.CompletedTask;
        if (RunningFetches.TryGetValue(container, out var running)) return running;
        var task = FetchCoreAsync(container);
        if (!task.IsCompleted) RunningFetches[container] = task;
        return task;
    }

    private static async Task FetchCoreAsync(IPlayableContainable container)
    {
        try
        {
            if (container.Account is { } account) await container.FetchAsync(Services.Settings, SyncerFor(account));
        }
        catch (Exception ex)
        {
            Services.EventLogger.Report("Preview Sync", ex, displayPopup: false);
        }
        finally
        {
            RunningFetches.Remove(container);
        }
    }

    public static async Task<List<AbstractPlayable>> GetPlayablesAsync(IPlayableContainable container)
    {
        if (container is not AbstractPlayable) await PrefetchAsync(container);
        return FilterPlayables(container);
    }

    public static ILibrarySyncer SyncerFor(Account account) => Services.Kit.GetMeta(account.Info).LibrarySyncer;

    public static DownloadManager DownloadManagerFor(Account account) => Services.Kit.GetMeta(account.Info).PlayableDownloadManager;

    // --- play ------------------------------------------------------------------------------------

    private static async Task PlayAsync(IPlayableContainable container, EntityActionOptions options)
    {
        try
        {
            if (options.PlayerIndex?.Invoke() is { } playerIndex)
            {
                Services.Player.Play(playerIndex);
                return;
            }
            if (options.PlayContext?.Invoke() is { } context)
            {
                if (context.Playables.Count > 0) Services.Player.Play(context);
                return;
            }
            await PlayContainerAsync(container);
        }
        catch (Exception ex)
        {
            Services.EventLogger.Report("Play", ex);
        }
    }

    private static async Task ShuffleAsync(IPlayableContainable container, EntityActionOptions options)
    {
        try
        {
            if (options.PlayContext?.Invoke() is { } context)
            {
                if (context.Playables.Count > 0) Services.Player.PlayShuffled(context);
                return;
            }
            await PlayContainerAsync(container, shuffle: true);
        }
        catch (Exception ex)
        {
            Services.EventLogger.Report("Play", ex);
        }
    }

    /// Plays all (available) playables of a container.
    public static async Task PlayContainerAsync(IPlayableContainable container, bool shuffle = false)
    {
        var playables = await GetPlayablesAsync(container);
        if (playables.Count == 0) return;
        var context = container is PlayableSelection
            ? new PlayContext(container.Name, container.PlayContextType, playables)
            : new PlayContext(container, 0, playables);
        if (shuffle) Services.Player.PlayShuffled(context);
        else Services.Player.Play(context);
    }

    /// Plays a context if it contains playables (list double click).
    public static void Play(PlayContext? context)
    {
        if (context is null || context.Playables.Count == 0) return;
        try { Services.Player.Play(context); }
        catch (Exception ex) { Services.EventLogger.Report("Play", ex); }
    }

    /// Whether a playable can be played in the current mode (offline: only cached ones).
    public static bool IsPlayable(AbstractPlayable playable) => playable switch
    {
        _ when IsOffline && !playable.IsCached => false,
        PodcastEpisode episode => episode.IsAvailableToUser(),
        Song song => song.IsAvailableToUser(),
        Radio radio => radio.IsAvailableToUser() && IsOnline,
        _ => true,
    };

    private static async Task PlayInstantMixAsync(Song song)
    {
        try
        {
            if (song.Account is not { } account)
            {
                Services.EventLogger.Error("Instant Mix", AmperfyLogStatusCode.CommonError, "Song has no account", displayPopup: true);
                return;
            }
            var similarSongs = await SyncerFor(account).RequestSimilarSongsAsync(song, 99);
            if (similarSongs.Count == 0)
            {
                Services.EventLogger.Info("Instant Mix", "No similar songs found", displayPopup: true);
                return;
            }
            var allSongs = new List<AbstractPlayable> { song };
            allSongs.AddRange(similarSongs);
            Services.Player.Play(new PlayContext($"Instant Mix: {song.Title}", allSongs));
            Services.EventLogger.Info("Instant Mix", $"Playing instant mix with {allSongs.Count} songs", displayPopup: true);
        }
        catch (Exception ex)
        {
            Services.EventLogger.Report("Instant Mix", ex);
        }
    }

    private static async Task QueueAsync(IPlayableContainable container, Action<IReadOnlyList<AbstractPlayable>> queueAction)
    {
        try
        {
            var playables = await GetPlayablesAsync(container);
            if (playables.Count == 0) return;
            queueAction(playables);
        }
        catch (Exception ex)
        {
            Services.EventLogger.Report("Queue", ex);
        }
    }

    // --- favorite / rating -----------------------------------------------------------------------

    public static async Task ToggleFavoriteAsync(IPlayableContainable container, Action? changed = null)
    {
        if (!IsOnline || container.Account is not { } account) return;
        try
        {
            await container.RemoteToggleFavoriteAsync(Services.Library, SyncerFor(account));
        }
        catch (Exception ex)
        {
            Services.EventLogger.Report("Toggle Favorite", ex);
        }
        NotifyChanged(container, changed);
    }

    public static async Task SetRatingAsync(AbstractLibraryEntity entity, int rating, Action? changed = null)
    {
        if (!IsOnline || entity.Account is not { } account) return;
        var syncer = SyncerFor(account);
        try
        {
            switch (entity)
            {
                case Song song:
                    song.Rating = rating;
                    Services.Library.SaveContext();
                    NotifyChanged(entity, changed);
                    await syncer.SetRatingAsync(song, rating);
                    break;
                case Album album:
                    album.Rating = rating;
                    Services.Library.SaveContext();
                    NotifyChanged(entity, changed);
                    await syncer.SetRatingAsync(album, rating);
                    break;
                case Artist artist:
                    artist.Rating = rating;
                    Services.Library.SaveContext();
                    NotifyChanged(entity, changed);
                    await syncer.SetRatingAsync(artist, rating);
                    break;
            }
        }
        catch (Exception ex)
        {
            var topic = entity switch { Song => "Song Rating Sync", Album => "Album Rating Sync", _ => "Artist Rating Sync" };
            Services.EventLogger.Report(topic, ex);
        }
    }

    private static void NotifyChanged(object entity, Action? changed)
    {
        changed?.Invoke();
        LibraryEventHub.RaiseEntityChanged(entity);
    }

    // --- playlist / download / cache -------------------------------------------------------------

    private static async Task AddToPlaylistAsync(IPlayableContainable container)
    {
        var playables = await GetPlayablesAsync(container);
        var songs = playables.FilterSongs();
        if (songs.Count == 0 || (container.Account ?? songs[0].Account) is not { } account) return;
        await AddToPlaylistDialog.ShowAsync(account, songs);
    }

    private static async Task DownloadAsync(IPlayableContainable container)
    {
        try
        {
            var playables = await GetPlayablesAsync(container);
            if (playables.Count == 0 || playables[0].Account is not { } account) return;
            await DownloadAsync(account, playables, container.Name);
        }
        catch (Exception ex)
        {
            Services.EventLogger.Report("Download", ex);
        }
    }

    /// Adds playables to the download queue (asks before adding many songs at once).
    public static async Task DownloadAsync(Account account, IReadOnlyList<AbstractPlayable> playables, string title)
    {
        if (playables.Count == 0) return;
        if (playables.Count > MaxPlayablesDownloadsToAddAtOnceWithoutWarning &&
            !await DialogHelper.ConfirmAsync("Many Songs", $"Are you sure to add {playables.Count} songs from \"{title}\" to download queue?", "OK"))
            return;
        DownloadManagerFor(account).Download(playables.Cast<IDownloadable>().ToList());
    }

    private static async Task DeleteCacheAsync(IPlayableContainable container, Action? changed)
    {
        var playables = FilterPlayables(container).Where(p => p.IsCached).ToList();
        if (playables.Count == 0) playables = container.Playables.Where(p => p.IsCached).ToList();
        if (playables.Count == 0 || container.Account is not { } account) return;
        if (!await DialogHelper.ConfirmAsync("Delete Cache", $"Are you sure to delete the cached file{(playables.Count > 1 ? "s" : "")}?", "Delete", destructive: true))
            return;
        try
        {
            DownloadManagerFor(account).RemoveFinishedDownload(playables.Cast<IDownloadable>().ToList());
            Services.Library.DeleteCache(playables);
            Services.Library.SaveContext();
        }
        catch (Exception ex)
        {
            Services.EventLogger.Report("Delete Cache", ex);
        }
        NotifyChanged(container, changed);
    }

    private static async Task DeleteEpisodeOnServerAsync(PodcastEpisode episode, Action? changed)
    {
        if (episode.Account is not { } account) return;
        if (!await DialogHelper.ConfirmAsync("Delete on Server", "Are you sure to delete the podcast episode on the server?", "Delete", destructive: true))
            return;
        try
        {
            var syncer = SyncerFor(account);
            await syncer.RequestPodcastEpisodeDeleteAsync(episode);
            if (episode.Podcast is { } podcast) await syncer.SyncAsync(podcast);
        }
        catch (Exception ex)
        {
            Services.EventLogger.Report("Podcast Episode Delete Sync", ex);
        }
        NotifyChanged(episode, changed);
    }

    public static async Task OpenUrlAsync(string url)
    {
        try
        {
            if (!url.Contains("://", StringComparison.Ordinal)) url = "https://" + url;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            Services.EventLogger.Report("Go to Site", ex);
        }
    }

    public static void CopyToClipboard(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            AmperfyLog.Error("EntityActions", $"Clipboard failed: {ex.Message}");
        }
    }

    // --- descriptions / lyrics -------------------------------------------------------------------

    public static Task ShowDescriptionAsync(PodcastEpisode episode) =>
        DialogHelper.ShowTextAsync("Description", (episode.Depiction ?? "").Html2String(), episode.Title);

    public static Task ShowDescriptionAsync(Podcast podcast) =>
        DialogHelper.ShowTextAsync("Description", podcast.Depiction.Html2String(), podcast.Title);

    public static void ShowLyrics(Song song)
    {
        if (ShowLyricsHandler is { } handler)
        {
            handler(song);
            return;
        }
        _ = ShowLyricsDialogAsync(song);
    }

    /// Default lyrics presentation: the lyrics text in a dialog (Swift: PlainDetailsVC lyrics).
    public static async Task ShowLyricsDialogAsync(Song song)
    {
        var text = "Lyrics are not available anymore.";
        try
        {
            if (song.LyricsRelFilePath is { } path && song.Account is { } account)
            {
                var lyricsList = await SyncerFor(account).ParseLyricsAsync(path);
                if (lyricsList.Lyrics.FirstOrDefault() is { } lyrics)
                    text = string.Join("\n", lyrics.Line.Select(l => l.Value));
            }
        }
        catch (Exception ex)
        {
            AmperfyLog.Info("EntityActions", $"Lyrics parsing failed: {ex.Message}");
        }
        await DialogHelper.ShowTextAsync("Lyrics", text, song.DisplayString);
    }

    // --- navigation ------------------------------------------------------------------------------

    /// Opens the detail page of an entity (optionally scrolling to an element). Songs are played,
    /// radios open the radios page, episodes open their podcast.
    public static void Open(object? entity, object? scrollTo = null)
    {
        var nav = Services.Navigation;
        switch (entity)
        {
            case null:
                return;
            case SearchHistoryItem history:
                Open(history.SearchedPlayableContainable);
                return;
            case Radio:
                nav.Navigate(typeof(RadiosPage), LibraryDisplayType.Radios);
                return;
            case PodcastEpisode { Podcast: { } podcast } episode:
                nav.Navigate(typeof(PodcastDetailPage), new EntityNavigationArgs(podcast, episode));
                return;
            case Song song:
                if (IsPlayable(song)) Play(new PlayContext(song));
                return;
        }
        if (PageRegistry.ForEntity(entity) is not { } target) return;
        nav.Navigate(target.Page, scrollTo is null ? target.Parameter : new EntityNavigationArgs(target.Parameter, scrollTo));
    }

    /// Records a search history entry for an opened search result.
    public static void RecordSearchHistory(IPlayableContainable container)
    {
        try
        {
            Services.Library.CreateOrUpdateSearchHistory(container);
            Services.Library.SaveContext();
        }
        catch (Exception ex)
        {
            AmperfyLog.Error("EntityActions", $"Search history failed: {ex.Message}");
        }
    }

    // --- playlists -------------------------------------------------------------------------------

    /// Deletes a playlist locally and on the server (Swift: PlaylistsVC delete).
    public static async Task<bool> DeletePlaylistAsync(Playlist playlist)
    {
        if (!await DialogHelper.ConfirmAsync("Delete Playlist", $"Are you sure to delete the playlist \"{playlist.Name}\"?", "Delete", destructive: true))
            return false;
        var playlistId = playlist.Id;
        var account = playlist.Account;
        Services.Library.DeletePlaylist(playlist);
        Services.Library.SaveContext();
        if (account is null || string.IsNullOrEmpty(playlistId) || !IsOnline) return true;
        try
        {
            await SyncerFor(account).SyncUploadPlaylistDeleteAsync(playlistId);
        }
        catch (Exception ex)
        {
            Services.EventLogger.Report("Playlist Upload Deletion", ex);
        }
        return true;
    }

    /// Creates a new (empty) playlist; uploads it when online (Swift: NewPlaylistTableHeader).
    public static async Task<Playlist?> CreatePlaylistAsync(Account account, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var playlist = Services.Library.CreatePlaylist(account);
        Services.Library.SaveContext();
        playlist.Name = name.Trim();
        Services.Library.SaveContext();
        if (IsOnline)
        {
            try
            {
                await SyncerFor(account).SyncUploadPlaylistNameAsync(playlist);
                Services.Library.SaveContext();
            }
            catch (Exception ex)
            {
                Services.EventLogger.Report("Playlist Create", ex);
            }
        }
        return playlist;
    }
}
