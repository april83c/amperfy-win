using Amperfy.App.Controls;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Common;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Album detail (port of AlbumDetailVC): header with artwork, artist link, info, play / shuffle,
/// favorite, rating and menu; the tracks grouped by disc; search and cached filter. The album is
/// synced from the server when shown.
public sealed partial class AlbumDetailPage : Page
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new() { IsTrackNumberStyle = true };
    private readonly LibraryListController _controller;
    private Album? _album;
    private object? _scrollTo;
    private List<AbstractPlayable> _contextSongs = [];

    public AlbumDetailPage()
    {
        InitializeComponent();
        _context.HostPageType = typeof(AlbumDetailPage);
        _context.PlayContextProvider = item =>
            _album is { } album && item.Playable is { } song
                ? new PlayContext(album, Math.Max(0, _contextSongs.IndexOf(song)), _contextSongs)
                : null;
        _context.Changed = RefreshHeader;
        _controller = new LibraryListController(ItemsList, _context, LibraryListMode.Playables);
        Toolbar.FilterPlaceholder = "Search in \"Album\"";
        Toolbar.FilterChanged += Reload;
        Toolbar.RefreshRequested += () => _ = SyncAsync(showBusy: true);
        Toolbar.AttachAccelerators(this);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        (_album, _scrollTo) = NavigationArgs.Unpack<Album>(e.Parameter);
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.OfflineModeChanged += OnOfflineModeChanged;
        if (_album is not { } album) return;
        Header.Configure(album, new DetailHeaderOptions
        {
            HostPageType = typeof(AlbumDetailPage),
            PlayContext = () => Task.FromResult<PlayContext?>(new PlayContext(album, 0, _contextSongs)),
            Changed = Reload,
        });
        Reload();
        ScrollToTarget();
        _ = SyncAsync(showBusy: false);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LibraryEventHub.OfflineModeChanged -= OnOfflineModeChanged;
    }

    private void OnOfflineModeChanged()
    {
        Toolbar.UpdateOfflineMode();
        Header.Refresh();
        Reload();
    }

    private void RefreshHeader() => Header.Refresh();

    private void Reload()
    {
        if (_album is not { } album) return;
        var filter = Toolbar.FilterText;
        var onlyCached = Toolbar.OnlyCached;
        var songs = album.Songs
            .Where(s => s.IsAvailableToUser())
            .Where(s => !onlyCached || s.IsCached)
            .Where(s => string.IsNullOrEmpty(filter) || s.Title.IsFoundBy(filter))
            .ToList();
        _contextSongs = songs.Where(EntityActions.IsPlayable).Cast<AbstractPlayable>().ToList();

        var items = new List<object>();
        var discs = songs.Select(s => s.Disk ?? "").Distinct().ToList();
        if (discs.Count > 1)
        {
            foreach (var group in songs.GroupBy(s => s.Disk ?? ""))
            {
                items.Add(new SectionHeaderItem(string.IsNullOrEmpty(group.Key) ? "Other" : $"Disc {group.Key}"));
                items.AddRange(group);
            }
        }
        else
        {
            items.AddRange(songs);
        }
        _controller.SetItems(items);
        EmptyText.Text = string.IsNullOrEmpty(filter) && !onlyCached ? "No songs" : "No results";
        EmptyText.Visibility = songs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ScrollToTarget()
    {
        if (_scrollTo is null) return;
        var target = _scrollTo;
        if (_controller.FindItem(e => ReferenceEquals(e, target)) is { } item)
        {
            _scrollTo = null;
            ItemsList.SelectedItem = item;
            DispatcherQueue.TryEnqueue(() => _controller.ScrollTo(item));
        }
    }

    private async Task SyncAsync(bool showBusy)
    {
        if (_album is not { Account: { } account } album || !_services.Settings.User.IsOnlineMode) return;
        Header.IsBusy = showBusy;
        await CategoryPageHelper.SyncAsync("Album Sync", () => EntityActions.SyncerFor(account).SyncAsync(album), displayPopup: showBusy);
        Header.IsBusy = false;
        if (_album != album) return;
        Header.Refresh();
        Reload();
        ScrollToTarget();
    }
}
