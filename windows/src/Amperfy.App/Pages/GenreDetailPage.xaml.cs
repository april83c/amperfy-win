using Amperfy.App.Controls;
using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Genre detail (port of GenreDetailVC): header, and the genre's artists, albums and songs
/// (switchable sections, loaded page by page); search and cached filter. Synced when shown.
public sealed partial class GenreDetailPage : Page
{
    private enum Section { Artists, Albums, Songs }

    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new();
    private readonly LibraryListController _controller;
    private readonly SelectorBar _selector = new() { Margin = new Thickness(20, 0, 20, 8) };
    private readonly SelectorBarItem _artistsItem = new() { Text = "Artists", Icon = new FontIcon { Glyph = Icons.Artist } };
    private readonly SelectorBarItem _albumsItem = new() { Text = "Albums", Icon = new FontIcon { Glyph = Icons.Album } };
    private readonly SelectorBarItem _songsItem = new() { Text = "Songs", Icon = new FontIcon { Glyph = Icons.Song } };
    private Section _section = Section.Albums;
    private Genre? _genre;
    private IQueryable<Song>? _songsQuery;

    public GenreDetailPage()
    {
        InitializeComponent();
        _context.HostPageType = typeof(GenreDetailPage);
        _context.ShowAlbumInSubtitle = true;
        _context.PlayContextProvider = item =>
            _genre is { } genre && _songsQuery is { } query && item.Playable is not null
                ? PlayContextHelper.FromQuery(query, item.Index, genre, genre.Name)
                : null;
        _controller = new LibraryListController(ItemsList, _context, LibraryListMode.Mixed);
        _selector.Items.Add(_artistsItem);
        _selector.Items.Add(_albumsItem);
        _selector.Items.Add(_songsItem);
        _selector.SelectionChanged += (_, _) =>
        {
            var section = _selector.SelectedItem == _artistsItem ? Section.Artists : _selector.SelectedItem == _songsItem ? Section.Songs : Section.Albums;
            if (section == _section) return;
            _section = section;
            ReloadList();
        };
        HeaderExtras.Children.Add(_selector);
        Toolbar.FilterPlaceholder = "Artists, Albums and Songs";
        Toolbar.FilterChanged += Reload;
        Toolbar.RefreshRequested += () => _ = SyncAsync(showBusy: true);
        Toolbar.AttachAccelerators(this);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        (_genre, _) = NavigationArgs.Unpack<Genre>(e.Parameter);
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.OfflineModeChanged += OnOfflineModeChanged;
        if (_genre is not { } genre) return;
        Header.Configure(genre, new DetailHeaderOptions
        {
            HostPageType = typeof(GenreDetailPage),
            PlayContext = () => Task.FromResult<PlayContext?>(new PlayContext(genre, 0, AllSongs())),
            Changed = Reload,
        });
        _section = _services.Library.QueryGenreAlbums(genre).Any() ? Section.Albums : Section.Songs;
        _selector.SelectedItem = _section == Section.Albums ? _albumsItem : _songsItem;
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

    private List<AbstractPlayable> AllSongs()
    {
        if (_genre is not { } genre) return [];
        return _services.Library.QueryGenreSongs(genre, "", Toolbar.OnlyCached).ToList().Where(EntityActions.IsPlayable).Cast<AbstractPlayable>().ToList();
    }

    /// Updates the section counts and the list.
    private void Reload()
    {
        if (_genre is not { } genre) return;
        var filter = Toolbar.FilterText;
        var onlyCached = Toolbar.OnlyCached;
        var artistCount = _services.Library.QueryGenreArtists(genre, filter, onlyCached).Count();
        var albumCount = _services.Library.QueryGenreAlbums(genre, filter, onlyCached).Count();
        var songCount = _services.Library.QueryGenreSongs(genre, filter, onlyCached).Count();
        _artistsItem.Text = $"Artists ({artistCount})";
        _albumsItem.Text = $"Albums ({albumCount})";
        _songsItem.Text = $"Songs ({songCount})";
        EmptyText.Text = string.IsNullOrEmpty(filter) && !onlyCached ? "No artists, albums or songs" : "No results";
        EmptyText.Visibility = artistCount + albumCount + songCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReloadList();
    }

    private void ReloadList()
    {
        if (_genre is not { } genre) return;
        var filter = Toolbar.FilterText;
        var onlyCached = Toolbar.OnlyCached;
        switch (_section)
        {
            case Section.Artists:
            {
                var query = _services.Library.QueryGenreArtists(genre, filter, onlyCached);
                _controller.SetIncrementalSource((skip, take) => query.Skip(skip).Take(take).ToList(), query.Count());
                break;
            }
            case Section.Albums:
            {
                var query = _services.Library.QueryGenreAlbums(genre, filter, onlyCached);
                _controller.SetIncrementalSource((skip, take) => query.Skip(skip).Take(take).ToList(), query.Count());
                break;
            }
            default:
            {
                var query = _services.Library.QueryGenreSongs(genre, filter, onlyCached);
                _songsQuery = query;
                _controller.SetIncrementalSource((skip, take) => query.Skip(skip).Take(take).ToList(), query.Count());
                break;
            }
        }
    }

    private async Task SyncAsync(bool showBusy)
    {
        if (_genre is not { Account: { } account } genre || !_services.Settings.User.IsOnlineMode) return;
        Header.IsBusy = showBusy;
        await CategoryPageHelper.SyncAsync("Genre Sync", () => EntityActions.SyncerFor(account).SyncAsync(genre), displayPopup: showBusy);
        Header.IsBusy = false;
        if (_genre != genre) return;
        Header.Refresh();
        Reload();
    }
}
