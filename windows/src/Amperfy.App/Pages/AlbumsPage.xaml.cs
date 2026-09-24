using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Amperfy.Core.Storage;
using Amperfy.Core.Sync;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Albums / favorite / newest / recently played albums (port of AlbumsVC, AlbumsCollectionVC and
/// AlbumsCommonVCInteractions): grid or table style with adjustable grid size, sorting, cached
/// filter, in-page search with server search, jump to letter, refresh, download all. Newest and
/// recent albums load further pages from the server while scrolling.
public sealed partial class AlbumsPage : Page, ILibraryCategoryPage
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new();
    private readonly LibraryListController _listController;
    private readonly LibraryListController _gridController;
    private readonly HashSet<int> _remoteOffsetsSynced = [];
    private Account? _account;
    private DisplayCategoryFilter _displayFilter = DisplayCategoryFilter.All;
    private AlbumElementSortType _sortType = AlbumElementSortType.Name;
    private bool _onlyCached;
    private IQueryable<Album>? _query;
    private IncrementalItems? _items;
    private AppBarButton? _jumpButton;

    public LibraryDisplayType LibraryType { get; private set; } = LibraryDisplayType.Albums;

    public AlbumsPage()
    {
        InitializeComponent();
        _context.HostPageType = typeof(AlbumsPage);
        _context.Changed = () => { if (_displayFilter == DisplayCategoryFilter.Favorites) Reload(); };
        _listController = new LibraryListController(ItemsList, _context, LibraryListMode.Entities);
        _gridController = new LibraryListController(ItemsGrid, _context, LibraryListMode.Entities);
        Header.PlayRequested += () => _ = PlayAlbumsAsync(random: false);
        Header.ShuffleRequested += () => _ = PlayAlbumsAsync(random: true);
        Header.FilterChanged += (text, _) => OnFilterChanged(text);
        Header.AttachAccelerators(this, () => _ = RefreshAsync());
        ItemsGrid.SizeChanged += (_, _) => UpdateTileSize();
    }

    private bool IsGridStyle => _services.Settings.User.AlbumsStyleSetting == AlbumsDisplayStyle.Grid;

    private LibraryListController ActiveController => IsGridStyle ? _gridController : _listController;

    private string FilterTitle => _displayFilter switch
    {
        DisplayCategoryFilter.Newest => "Newest Albums",
        DisplayCategoryFilter.Recent => "Recently Played Albums",
        DisplayCategoryFilter.Favorites => "Favorite Albums",
        _ => "Albums",
    };

    private bool IsRemoteOrdered => _displayFilter is DisplayCategoryFilter.Newest or DisplayCategoryFilter.Recent;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is LibraryDisplayType type) LibraryType = type;
        _displayFilter = LibraryType switch
        {
            LibraryDisplayType.FavoriteAlbums => DisplayCategoryFilter.Favorites,
            LibraryDisplayType.NewestAlbums => DisplayCategoryFilter.Newest,
            LibraryDisplayType.RecentAlbums => DisplayCategoryFilter.Recent,
            _ => DisplayCategoryFilter.All,
        };
        _account = _services.ActiveAccount;
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.OfflineModeChanged += OnOfflineModeChanged;
        if (_account is null) return;
        _sortType = _displayFilter switch
        {
            DisplayCategoryFilter.Newest => AlbumElementSortType.Newest,
            DisplayCategoryFilter.Recent => AlbumElementSortType.Recent,
            _ => _services.Settings.User.AlbumsSortSetting is AlbumElementSortType.Newest or AlbumElementSortType.Recent
                ? AlbumElementSortType.Name
                : _services.Settings.User.AlbumsSortSetting,
        };
        _onlyCached = _services.Settings.User.IsOfflineMode;
        Header.Title = FilterTitle;
        Header.FilterPlaceholder = $"Search in \"{FilterTitle}\"";
        BuildCommands();
        ApplyStyle();
        Reload();
        _ = UpdateFromRemoteAsync(0);
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

    private void BuildCommands()
    {
        Header.Commands.PrimaryCommands.Clear();
        Header.Commands.SecondaryCommands.Clear();
        if (!IsRemoteOrdered)
        {
            Header.AddMenuCommand("Sort", LibraryGlyphs.Sort, () =>
            [
                Ui.RadioItem("Name", "sort", _sortType == AlbumElementSortType.Name, () => ChangeSort(AlbumElementSortType.Name)),
                Ui.RadioItem("Rating", "sort", _sortType == AlbumElementSortType.Rating, () => ChangeSort(AlbumElementSortType.Rating)),
                Ui.RadioItem("Artist", "sort", _sortType == AlbumElementSortType.Artist, () => ChangeSort(AlbumElementSortType.Artist)),
                Ui.RadioItem("Duration", "sort", _sortType == AlbumElementSortType.Duration, () => ChangeSort(AlbumElementSortType.Duration)),
                Ui.RadioItem("Year", "sort", _sortType == AlbumElementSortType.Year, () => ChangeSort(AlbumElementSortType.Year)),
            ]);
        }
        Header.AddMenuCommand("View", LibraryGlyphs.GridView, BuildStyleMenu, "Style");
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
                if (_query is not null) ActiveController.ScrollToIndex(LibraryStorage.CountBeforeSectionInitial(_query, section));
            });
        Header.AddCommand("Refresh", Icons.Refresh, () => _ = RefreshAsync(), "Refresh (F5)");
        if (_services.Settings.User.IsOnlineMode)
            Header.AddCommand($"Download {FilterTitle}", Icons.Download, () => _ = DownloadAllAsync(), secondary: true);
        UpdateJumpVisibility();
    }

    private IEnumerable<MenuFlyoutItemBase> BuildStyleMenu()
    {
        yield return Ui.RadioItem("Table", "style", !IsGridStyle, () => ChangeStyle(AlbumsDisplayStyle.Table));
        yield return Ui.RadioItem("Grid", "style", IsGridStyle, () => ChangeStyle(AlbumsDisplayStyle.Grid));
        yield return new MenuFlyoutSeparator();
        yield return Ui.MenuItem("Change Grid Size", LibraryGlyphs.Zoom, ShowGridSizeFlyout, IsGridStyle);
    }

    private void ShowGridSizeFlyout()
    {
        var slider = new Slider
        {
            Minimum = 3,
            Maximum = 11,
            StepFrequency = 1,
            SnapsTo = Microsoft.UI.Xaml.Controls.Primitives.SliderSnapsTo.StepValues,
            Value = Math.Clamp(_services.Settings.User.AlbumsGridSizeSetting, 3, 11),
            Width = 240,
            Header = "Albums per row",
        };
        slider.ValueChanged += (_, args) =>
        {
            var value = (int)Math.Round(args.NewValue);
            if (value == _services.Settings.User.AlbumsGridSizeSetting) return;
            _services.Settings.User.AlbumsGridSizeSetting = value;
            UpdateTileSize();
        };
        var flyout = new Flyout { Content = slider };
        flyout.ShowAt(Header);
    }

    private void ChangeStyle(AlbumsDisplayStyle style)
    {
        _services.Settings.User.AlbumsStyleSetting = style;
        ApplyStyle();
        Reload();
    }

    private void ApplyStyle()
    {
        ItemsGrid.Visibility = IsGridStyle ? Visibility.Visible : Visibility.Collapsed;
        ItemsList.Visibility = IsGridStyle ? Visibility.Collapsed : Visibility.Visible;
        (IsGridStyle ? _listController : _gridController).Clear();
    }

    private void ChangeSort(AlbumElementSortType sortType)
    {
        _sortType = sortType;
        _services.Settings.User.AlbumsSortSetting = sortType;
        UpdateJumpVisibility();
        Reload();
    }

    private void UpdateJumpVisibility()
    {
        if (_jumpButton is not null) _jumpButton.Visibility = _sortType == AlbumElementSortType.Name ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTileSize()
    {
        var columns = Math.Clamp(_services.Settings.User.AlbumsGridSizeSetting, 3, 11);
        var width = ItemsGrid.ActualWidth - ItemsGrid.Padding.Left - ItemsGrid.Padding.Right;
        if (width <= 0) return;
        var tile = Math.Clamp(Math.Floor(width / columns) - 8, 110, 320);
        _context.TileWidth = tile;
        if (ItemsGrid.ItemsPanelRoot is ItemsWrapGrid wrap)
        {
            wrap.ItemWidth = tile + 8;
            wrap.ItemHeight = tile + 72;
        }
        _context.NotifyLayoutChanged();
    }

    private void Reload()
    {
        if (_account is null) return;
        var onlyCached = _onlyCached || _services.Settings.User.IsOfflineMode;
        var query = LibraryStorage.SortAlbums(_services.Library.QueryAlbums(_account, Header.FilterText, onlyCached, _displayFilter), _sortType);
        _query = query;
        var count = query.Count();
        _items = ActiveController.SetIncrementalSource((skip, take) =>
        {
            var page = query.Skip(skip).Take(take).ToList();
            if (skip + page.Count >= (_items?.TotalCount ?? count)) OnEndReached(skip + page.Count);
            return page;
        }, count);
        Header.Info = Ui.Plural(count, "Album", "Albums");
        Header.IsPlayEnabled = count > 0;
        CategoryPageHelper.UpdateEmptyState(EmptyHost, count == 0, !string.IsNullOrEmpty(Header.FilterText), Icons.Album, FilterTitle);
        if (IsGridStyle) DispatcherQueue.TryEnqueue(UpdateTileSize);
    }

    /// Newest / recent albums: all local ones are shown -> fetch the next ones from the server
    /// (Swift: listViewWillDisplayCell).
    private void OnEndReached(int offset)
    {
        if (!IsRemoteOrdered || offset == 0 || !string.IsNullOrEmpty(Header.FilterText)) return;
        if (!_remoteOffsetsSynced.Add(offset)) return;
        DispatcherQueue.TryEnqueue(() => _ = FetchMoreAsync(offset));
    }

    private async Task FetchMoreAsync(int offset)
    {
        var items = _items;
        await UpdateFromRemoteAsync(offset, reload: false);
        if (items is null || items != _items || _query is null) return;
        items.ExtendTotal(_query.Count());
        Header.Info = Ui.Plural(items.TotalCount, "Album", "Albums");
    }

    private async Task UpdateFromRemoteAsync(int offset, bool reload = true)
    {
        if (_account is not { } account || !_services.Settings.User.IsOnlineMode) return;
        var count = CategoryPageHelper.NewestFetchCount;
        var meta = _services.Kit.GetMeta(account.Info);
        bool synced;
        switch (_displayFilter)
        {
            case DisplayCategoryFilter.Newest:
                synced = await CategoryPageHelper.SyncAsync("Newest Albums Sync", () =>
                    new AutoDownloadLibrarySyncer(_services.Library, _services.Settings, account, meta.LibrarySyncer, meta.PlayableDownloadManager)
                        .SyncNewestLibraryElementsAsync(offset, count));
                break;
            case DisplayCategoryFilter.Recent:
                synced = await CategoryPageHelper.SyncAsync("Recent Albums Sync", () => meta.LibrarySyncer.SyncRecentAlbumsAsync(offset, count));
                break;
            case DisplayCategoryFilter.Favorites:
                synced = await CategoryPageHelper.SyncAsync("Favorite Albums Sync", () => meta.LibrarySyncer.SyncFavoriteLibraryElementsAsync());
                break;
            default:
                return;
        }
        if (synced && reload) Reload();
    }

    private void OnFilterChanged(string text)
    {
        Reload();
        if (_account is not { } account || string.IsNullOrEmpty(text) || _onlyCached) return;
        _ = SearchOnServerAsync(account, text);
    }

    private async Task SearchOnServerAsync(Account account, string text)
    {
        if (await CategoryPageHelper.SyncAsync("Albums Search", () => EntityActions.SyncerFor(account).SearchAlbumsAsync(text), displayPopup: false) &&
            Header.FilterText == text)
            Reload();
    }

    private async Task RefreshAsync()
    {
        if (_account is not { } account || !_services.Settings.User.IsOnlineMode) return;
        Header.IsBusy = true;
        _remoteOffsetsSynced.Clear();
        switch (_displayFilter)
        {
            case DisplayCategoryFilter.Recent:
                await CategoryPageHelper.SyncAsync("Albums Newest Elements Sync",
                    () => EntityActions.SyncerFor(account).SyncRecentAlbumsAsync(0, CategoryPageHelper.NewestFetchCount));
                break;
            case DisplayCategoryFilter.Favorites:
                await UpdateFromRemoteAsync(0, reload: false);
                break;
            default:
                await CategoryPageHelper.SyncNewestLibraryElementsAsync(account, "Albums Newest Elements Sync");
                break;
        }
        Header.IsBusy = false;
        Reload();
    }

    /// Header play: songs of the first 5 albums; shuffle: songs of 5 random albums
    /// (Swift: handleHeaderPlay / handleHeaderShuffle).
    private async Task PlayAlbumsAsync(bool random)
    {
        if (_query is null) return;
        var albums = random ? LibraryStorage.TakeRandom(_query, 5) : _query.Take(5).ToList();
        var songs = new List<AbstractPlayable>();
        foreach (var album in albums)
        {
            if (!album.IsSongsMetaDataSynced) await EntityActions.PrefetchAsync(album);
            songs.AddRange(EntityActions.FilterPlayables(album));
        }
        EntityActions.Play(new PlayContext(FilterTitle, songs.Take(CategoryPageHelper.MaxSongsToAddOnce).ToList()));
    }

    private async Task DownloadAllAsync()
    {
        if (_account is not { } account) return;
        var albums = _displayFilter switch
        {
            DisplayCategoryFilter.Newest => _services.Library.GetNewestAlbums(account, 0, int.MaxValue),
            DisplayCategoryFilter.Recent => _services.Library.GetRecentAlbums(account, 0, int.MaxValue),
            DisplayCategoryFilter.Favorites => _services.Library.GetFavoriteAlbums(account),
            _ => _services.Library.GetAlbums(account),
        };
        var songs = albums.SelectMany(a => a.Playables).ToList();
        await EntityActions.DownloadAsync(account, songs, FilterTitle);
    }
}
