using Amperfy.App.Controls;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Artist detail (port of ArtistDetailVC): header, albums (by release year) as tiles and the
/// artist's songs; search and cached filter. The artist is synced from the server when shown.
public sealed partial class ArtistDetailPage : Page
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _songContext = new() { ShowAlbumInSubtitle = true };
    private readonly LibraryListContext _albumContext = new() { TileWidth = 170 };
    private readonly LibraryListController _controller;
    private readonly TextBlock _albumsTitle;
    private readonly VariableSizedWrapGrid _albumsGrid;
    private readonly TextBlock _songsTitle;
    private Artist? _artist;
    private object? _scrollTo;
    private IQueryable<Song>? _songsQuery;

    public ArtistDetailPage()
    {
        InitializeComponent();
        _songContext.HostPageType = typeof(ArtistDetailPage);
        _albumContext.HostPageType = typeof(ArtistDetailPage);
        _songContext.PlayContextProvider = item =>
            _artist is { } artist && _songsQuery is { } query && item.Playable is not null
                ? PlayContextHelper.FromQuery(query, item.Index, artist, artist.Name)
                : null;
        _songContext.Changed = Header.Refresh;
        _controller = new LibraryListController(ItemsList, _songContext, LibraryListMode.Playables);

        _albumsTitle = Ui.Text("Albums", "SubtitleTextBlockStyle");
        _albumsTitle.Margin = new Thickness(24, 12, 24, 8);
        _albumsGrid = new VariableSizedWrapGrid
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = _albumContext.TileWidth + 8,
            ItemHeight = _albumContext.TileWidth + 72,
            Margin = new Thickness(20, 0, 20, 8),
        };
        _songsTitle = Ui.Text("Songs", "SubtitleTextBlockStyle");
        _songsTitle.Margin = new Thickness(24, 16, 24, 4);
        HeaderExtras.Children.Add(_albumsTitle);
        HeaderExtras.Children.Add(_albumsGrid);
        HeaderExtras.Children.Add(_songsTitle);

        Toolbar.FilterPlaceholder = "Albums and Songs";
        Toolbar.FilterChanged += Reload;
        Toolbar.RefreshRequested += () => _ = SyncAsync(showBusy: true);
        Toolbar.AttachAccelerators(this);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        (_artist, _scrollTo) = NavigationArgs.Unpack<Artist>(e.Parameter);
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.OfflineModeChanged += OnOfflineModeChanged;
        if (_artist is not { } artist) return;
        Header.Configure(artist, new DetailHeaderOptions
        {
            HostPageType = typeof(ArtistDetailPage),
            PlayContext = () => Task.FromResult<PlayContext?>(new PlayContext(artist, 0, AllSongsSortedByAlbum())),
            Changed = Reload,
        });
        Reload();
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

    private ArtistCategoryFilter SongFilter => _services.Settings.User.ArtistsFilterSetting;

    /// Header "Play": all songs of the artist sorted by album (Swift: songs.filterSongs().sortByAlbum()).
    private List<AbstractPlayable> AllSongsSortedByAlbum()
    {
        if (_artist is not { } artist) return [];
        return _services.Library.QueryArtistSongs(artist, SongFilter, "", Toolbar.OnlyCached).ToList()
            .SortByAlbum().Where(EntityActions.IsPlayable).Cast<AbstractPlayable>().ToList();
    }

    private void Reload()
    {
        if (_artist is not { } artist) return;
        var filter = Toolbar.FilterText;
        var onlyCached = Toolbar.OnlyCached;

        var albums = _services.Library.QueryArtistAlbums(artist, filter, onlyCached).ToList();
        _albumsGrid.Children.Clear();
        var index = 0;
        foreach (var album in albums)
        {
            var tile = new EntityTile { IsStandalone = true };
            tile.Bind(new LibraryItem(album, _albumContext, index++));
            _albumsGrid.Children.Add(tile);
        }
        _albumsTitle.Text = albums.Count == 1 ? "1 Album" : $"{albums.Count} Albums";
        _albumsTitle.Visibility = albums.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _albumsGrid.Visibility = albums.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var query = _services.Library.QueryArtistSongs(artist, SongFilter, filter, onlyCached);
        _songsQuery = query;
        var count = query.Count();
        _controller.SetIncrementalSource((skip, take) => query.Skip(skip).Take(take).ToList(), count);
        _songsTitle.Text = count == 1 ? "1 Song" : $"{count} Songs";
        _songsTitle.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;

        EmptyText.Text = string.IsNullOrEmpty(filter) && !onlyCached ? "No albums or songs" : "No results";
        EmptyText.Visibility = albums.Count == 0 && count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScrollToTarget();
    }

    private void ScrollToTarget()
    {
        if (_scrollTo is not Album album) return;
        var tile = _albumsGrid.Children.OfType<EntityTile>().FirstOrDefault(t => t.Item?.Entity == album);
        if (tile is null) return;
        _scrollTo = null;
        DispatcherQueue.TryEnqueue(() =>
        {
            tile.StartBringIntoView();
            tile.Focus(FocusState.Programmatic);
        });
    }

    private async Task SyncAsync(bool showBusy)
    {
        if (_artist is not { Account: { } account } artist || !_services.Settings.User.IsOnlineMode) return;
        Header.IsBusy = showBusy;
        await CategoryPageHelper.SyncAsync("Artist Sync", () => EntityActions.SyncerFor(account).SyncAsync(artist), displayPopup: showBusy);
        Header.IsBusy = false;
        if (_artist != artist) return;
        Header.Refresh();
        Reload();
    }
}
