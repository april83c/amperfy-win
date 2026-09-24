using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Artists / favorite artists (port of ArtistsVC): sort, "All" / "Album Artists" filter, cached
/// filter, in-page search with server search, jump to letter, refresh, download all.
public sealed partial class ArtistsPage : Page, ILibraryCategoryPage
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new();
    private readonly LibraryListController _controller;
    private Account? _account;
    private ArtistCategoryFilter _displayFilter = ArtistCategoryFilter.All;
    private ArtistElementSortType _sortType = ArtistElementSortType.Name;
    private bool _onlyCached;
    private IQueryable<Artist>? _query;
    private AppBarButton? _jumpButton;

    public LibraryDisplayType LibraryType { get; private set; } = LibraryDisplayType.Artists;

    public ArtistsPage()
    {
        InitializeComponent();
        _context.HostPageType = typeof(ArtistsPage);
        _context.Changed = () => { if (_displayFilter == ArtistCategoryFilter.Favorites) Reload(); };
        _controller = new LibraryListController(ItemsList, _context, LibraryListMode.Entities);
        Header.IsPlayVisible = false;
        Header.IsShuffleVisible = false;
        Header.FilterChanged += (text, _) => OnFilterChanged(text);
        Header.AttachAccelerators(this, () => _ = RefreshAsync());
    }

    private string FilterTitle => _displayFilter switch
    {
        ArtistCategoryFilter.Favorites => "Favorite Artists",
        ArtistCategoryFilter.AlbumArtists => "Album Artists",
        _ => "Artists",
    };

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is LibraryDisplayType type) LibraryType = type;
        _account = _services.ActiveAccount;
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.OfflineModeChanged += OnOfflineModeChanged;
        if (_account is null) return;
        _displayFilter = LibraryType == LibraryDisplayType.FavoriteArtists
            ? ArtistCategoryFilter.Favorites
            : _services.Settings.User.ArtistsFilterSetting == ArtistCategoryFilter.Favorites ? ArtistCategoryFilter.AlbumArtists : _services.Settings.User.ArtistsFilterSetting;
        _sortType = _services.Settings.User.ArtistsSortSetting;
        _onlyCached = _services.Settings.User.IsOfflineMode;
        BuildCommands();
        UpdateTitle();
        Reload();
        if (_displayFilter == ArtistCategoryFilter.Favorites) _ = SyncFavoritesAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LibraryEventHub.OfflineModeChanged -= OnOfflineModeChanged;
    }

    private void OnOfflineModeChanged()
    {
        _onlyCached = _services.Settings.User.IsOfflineMode;
        BuildCommands();
        Reload();
    }

    private void UpdateTitle()
    {
        Header.Title = _displayFilter == ArtistCategoryFilter.Favorites ? "Favorite Artists" : "Artists";
        Header.FilterPlaceholder = $"Search in \"{FilterTitle}\"";
    }

    private void BuildCommands()
    {
        Header.Commands.PrimaryCommands.Clear();
        Header.Commands.SecondaryCommands.Clear();
        Header.AddMenuCommand("Sort", LibraryGlyphs.Sort, () =>
        [
            Ui.RadioItem("Name", "sort", _sortType == ArtistElementSortType.Name, () => ChangeSort(ArtistElementSortType.Name)),
            Ui.RadioItem("Rating", "sort", _sortType == ArtistElementSortType.Rating, () => ChangeSort(ArtistElementSortType.Rating)),
            Ui.RadioItem("Duration", "sort", _sortType == ArtistElementSortType.Duration, () => ChangeSort(ArtistElementSortType.Duration)),
        ]);
        if (_displayFilter != ArtistCategoryFilter.Favorites)
        {
            Header.AddMenuCommand("Filter", LibraryGlyphs.Filter, () =>
            [
                Ui.RadioItem("All", "filter", _displayFilter == ArtistCategoryFilter.All, () => ChangeFilter(ArtistCategoryFilter.All)),
                Ui.RadioItem("Album Artists", "filter", _displayFilter == ArtistCategoryFilter.AlbumArtists, () => ChangeFilter(ArtistCategoryFilter.AlbumArtists)),
            ]);
        }
        CategoryPageHelper.AddCachedToggle(Header, () => _onlyCached, isChecked =>
        {
            _onlyCached = isChecked;
            Reload();
        });
        _jumpButton = Header.AddCommand("Jump to", LibraryGlyphs.JumpTo, () => { }, "Jump to letter");
        _jumpButton.Flyout = CategoryPageHelper.CreateJumpFlyout(
            () => _query is null ? [] : LibraryStorage.GetSectionInitials(_query),
            section =>
            {
                if (_query is not null) _controller.ScrollToIndex(LibraryStorage.CountBeforeSectionInitial(_query, section));
            });
        Header.AddCommand("Refresh", Icons.Refresh, () => _ = RefreshAsync(), "Refresh (F5)");
        if (_services.Settings.User.IsOnlineMode)
            Header.AddCommand($"Download {FilterTitle}", Icons.Download, () => _ = DownloadAllAsync(), secondary: true);
        _jumpButton.Visibility = _sortType == ArtistElementSortType.Name ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChangeSort(ArtistElementSortType sortType)
    {
        _sortType = sortType;
        _services.Settings.User.ArtistsSortSetting = sortType;
        if (_jumpButton is not null) _jumpButton.Visibility = sortType == ArtistElementSortType.Name ? Visibility.Visible : Visibility.Collapsed;
        Reload();
    }

    private void ChangeFilter(ArtistCategoryFilter filter)
    {
        // favorite views can't change the display filter
        if (_displayFilter == ArtistCategoryFilter.Favorites) return;
        _displayFilter = filter;
        _services.Settings.User.ArtistsFilterSetting = filter;
        UpdateTitle();
        BuildCommands();
        Reload();
    }

    private void Reload()
    {
        if (_account is null) return;
        var onlyCached = _onlyCached || _services.Settings.User.IsOfflineMode;
        var query = LibraryStorage.SortArtists(_services.Library.QueryArtists(_account, Header.FilterText, onlyCached, _displayFilter), _sortType);
        _query = query;
        var count = query.Count();
        _controller.SetIncrementalSource((skip, take) => query.Skip(skip).Take(take).ToList(), count);
        Header.Info = Ui.Plural(count, "Artist", "Artists");
        CategoryPageHelper.UpdateEmptyState(EmptyHost, count == 0, !string.IsNullOrEmpty(Header.FilterText), Icons.Artist, FilterTitle);
    }

    private void OnFilterChanged(string text)
    {
        Reload();
        if (_account is not { } account || string.IsNullOrEmpty(text) || _onlyCached) return;
        _ = SearchOnServerAsync(account, text);
    }

    private async Task SearchOnServerAsync(Account account, string text)
    {
        if (await CategoryPageHelper.SyncAsync("Artists Search", () => EntityActions.SyncerFor(account).SearchArtistsAsync(text), displayPopup: false) &&
            Header.FilterText == text)
            Reload();
    }

    private async Task RefreshAsync()
    {
        if (_account is null || !_services.Settings.User.IsOnlineMode) return;
        Header.IsBusy = true;
        if (_displayFilter == ArtistCategoryFilter.Favorites) await SyncFavoritesAsync();
        else await CategoryPageHelper.SyncNewestLibraryElementsAsync(_account, "Artists Newest Elements Sync");
        Header.IsBusy = false;
        Reload();
    }

    private async Task SyncFavoritesAsync()
    {
        if (_account is not { } account) return;
        if (await CategoryPageHelper.SyncAsync("Favorite Artists Sync", () => EntityActions.SyncerFor(account).SyncFavoriteLibraryElementsAsync()))
            Reload();
    }

    private async Task DownloadAllAsync()
    {
        if (_account is not { } account) return;
        var artists = _displayFilter switch
        {
            ArtistCategoryFilter.AlbumArtists => _services.Library.GetAlbumArtists(account),
            ArtistCategoryFilter.Favorites => _services.Library.GetFavoriteArtists(account),
            _ => _services.Library.GetArtists(account),
        };
        var songs = artists.SelectMany(a => a.Playables).ToList();
        await EntityActions.DownloadAsync(account, songs, FilterTitle);
    }
}
