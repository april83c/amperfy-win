using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// All songs / favorite songs (port of SongsVC): sorting, cached filter, in-page search with
/// server search, jump to letter, refresh, download all. Songs are loaded page by page.
public sealed partial class SongsPage : Page, ILibraryCategoryPage
{
    /// Swift SongsVC.maxPlayContextCount: songs following the played one that form the context.
    private const int MaxPlayContextCount = 40;

    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new() { ShowAlbumInSubtitle = true };
    private readonly LibraryListController _controller;
    private Account? _account;
    private DisplayCategoryFilter _displayFilter = DisplayCategoryFilter.All;
    private SongElementSortType _sortType = SongElementSortType.Name;
    private bool _onlyCached;
    private IQueryable<Song>? _query;
    private AppBarToggleButton? _cachedToggle;
    private AppBarButton? _jumpButton;

    public LibraryDisplayType LibraryType { get; private set; } = LibraryDisplayType.Songs;

    public SongsPage()
    {
        InitializeComponent();
        _context.HostPageType = typeof(SongsPage);
        _context.PlayContextProvider = item => PlayContextAt(item.Index);
        _context.Changed = () => { if (_displayFilter == DisplayCategoryFilter.Favorites) Reload(); };
        _controller = new LibraryListController(ItemsList, _context, LibraryListMode.Playables);
        Header.PlayRequested += () => PlayAll(shuffle: false);
        Header.ShuffleRequested += () => PlayAll(shuffle: true);
        Header.FilterChanged += (text, _) => OnFilterChanged(text);
        Header.AttachAccelerators(this, () => _ = RefreshAsync());
    }

    private string FilterTitle => _displayFilter == DisplayCategoryFilter.Favorites ? "Favorite Songs" : "Songs";

    private bool IsAmpache => _account?.ApiType.AsServerApiType() == ServerApiType.Ampache;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is LibraryDisplayType type) LibraryType = type;
        _displayFilter = LibraryType == LibraryDisplayType.FavoriteSongs ? DisplayCategoryFilter.Favorites : DisplayCategoryFilter.All;
        _account = _services.ActiveAccount;
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.OfflineModeChanged += OnOfflineModeChanged;
        if (_account is null) return;
        _sortType = _displayFilter == DisplayCategoryFilter.Favorites && !IsAmpache
            ? _services.Settings.User.FavoriteSongSortSetting
            : _services.Settings.User.SongsSortSetting;
        _onlyCached = _services.Settings.User.IsOfflineMode;
        Header.Title = FilterTitle;
        Header.FilterPlaceholder = $"Search in \"{FilterTitle}\"";
        BuildCommands();
        Reload();
        if (_displayFilter == DisplayCategoryFilter.Favorites) _ = SyncFavoritesAsync();
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
        var commands = Header.Commands;
        commands.PrimaryCommands.Clear();
        commands.SecondaryCommands.Clear();
        Header.AddMenuCommand("Sort", LibraryGlyphs.Sort, BuildSortMenu, "Sort");
        _cachedToggle = CategoryPageHelper.AddCachedToggle(Header, () => _onlyCached, isChecked =>
        {
            _onlyCached = isChecked;
            Reload();
        });
        _jumpButton = Header.AddCommand("Jump to", LibraryGlyphs.JumpTo, () => { }, "Jump to letter");
        _jumpButton.Flyout = CategoryPageHelper.CreateJumpFlyout(
            () => _query is null ? [] : LibraryStorage.GetSectionInitials(_query),
            section =>
            {
                if (_query is null) return;
                _controller.ScrollToIndex(LibraryStorage.CountBeforeSectionInitial(_query, section));
            });
        Header.AddCommand("Refresh", Icons.Refresh, () => _ = RefreshAsync(), "Refresh (F5)");
        if (_services.Settings.User.IsOnlineMode)
            Header.AddCommand($"Download {FilterTitle}", Icons.Download, () => _ = DownloadAllAsync(), secondary: true);
        UpdateJumpVisibility();
    }

    private IEnumerable<MenuFlyoutItemBase> BuildSortMenu()
    {
        yield return Ui.RadioItem("Name", "sort", _sortType == SongElementSortType.Name, () => ChangeSort(SongElementSortType.Name));
        yield return Ui.RadioItem("Rating", "sort", _sortType == SongElementSortType.Rating, () => ChangeSort(SongElementSortType.Rating));
        yield return Ui.RadioItem("Duration", "sort", _sortType == SongElementSortType.Duration, () => ChangeSort(SongElementSortType.Duration));
        if (_displayFilter == DisplayCategoryFilter.Favorites && !IsAmpache)
            yield return Ui.RadioItem("Starred date", "sort", _sortType == SongElementSortType.StarredDate, () => ChangeSort(SongElementSortType.StarredDate));
        if (!IsAmpache)
            yield return Ui.RadioItem("Date Added", "sort", _sortType == SongElementSortType.AddedDate, () => ChangeSort(SongElementSortType.AddedDate));
    }

    private void ChangeSort(SongElementSortType sortType)
    {
        _sortType = sortType;
        if (_displayFilter == DisplayCategoryFilter.Favorites && !IsAmpache) _services.Settings.User.FavoriteSongSortSetting = sortType;
        else _services.Settings.User.SongsSortSetting = sortType;
        UpdateJumpVisibility();
        Reload();
    }

    private void UpdateJumpVisibility()
    {
        if (_jumpButton is not null) _jumpButton.Visibility = _sortType == SongElementSortType.Name ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Reload()
    {
        if (_account is null) return;
        var onlyCached = _onlyCached || _services.Settings.User.IsOfflineMode;
        var query = LibraryStorage.SortSongs(_services.Library.QuerySongs(_account, Header.FilterText, onlyCached, _displayFilter), _sortType);
        _query = query;
        var count = query.Count();
        _controller.SetIncrementalSource((skip, take) => query.Skip(skip).Take(take).ToList(), count);
        Header.Info = Ui.Plural(count, "Song", "Songs");
        Header.IsPlayEnabled = count > 0;
        CategoryPageHelper.UpdateEmptyState(EmptyHost, count == 0, !string.IsNullOrEmpty(Header.FilterText), Icons.Song, FilterTitle);
    }

    private void OnFilterChanged(string text)
    {
        Reload();
        if (_account is null || string.IsNullOrEmpty(text) || _onlyCached) return;
        var account = _account;
        _ = SearchOnServerAsync(account, text);
    }

    private async Task SearchOnServerAsync(Account account, string text)
    {
        if (await CategoryPageHelper.SyncAsync("Songs Search", () => EntityActions.SyncerFor(account).SearchSongsAsync(text), displayPopup: false) &&
            Header.FilterText == text)
            Reload();
    }

    private PlayContext? PlayContextAt(int index)
    {
        if (_query is null) return null;
        var songs = _query.Skip(index).Take(MaxPlayContextCount).ToList<AbstractPlayable>();
        return new PlayContext(FilterTitle, songs);
    }

    private void PlayAll(bool shuffle)
    {
        if (_query is null) return;
        var max = CategoryPageHelper.MaxSongsToAddOnce;
        if (shuffle)
        {
            var songs = LibraryStorage.TakeRandom(_query, max).ToList<AbstractPlayable>();
            if (songs.Count > 0) _services.Player.PlayShuffled(new PlayContext(FilterTitle, songs));
        }
        else
        {
            EntityActions.Play(new PlayContext(FilterTitle, _query.Take(max).ToList<AbstractPlayable>()));
        }
    }

    private async Task RefreshAsync()
    {
        if (_account is null || !_services.Settings.User.IsOnlineMode) return;
        Header.IsBusy = true;
        if (_displayFilter == DisplayCategoryFilter.Favorites) await SyncFavoritesAsync();
        else await CategoryPageHelper.SyncNewestLibraryElementsAsync(_account, "Songs Newest Elements Sync");
        Header.IsBusy = false;
        Reload();
    }

    private async Task SyncFavoritesAsync()
    {
        if (_account is not { } account) return;
        if (await CategoryPageHelper.SyncAsync("Favorite Songs Sync", () => EntityActions.SyncerFor(account).SyncFavoriteLibraryElementsAsync()))
            Reload();
    }

    private async Task DownloadAllAsync()
    {
        if (_account is not { } account) return;
        var songs = _displayFilter == DisplayCategoryFilter.Favorites
            ? _services.Library.GetFavoriteSongs(account)
            : _services.Library.GetSongs(account);
        await EntityActions.DownloadAsync(account, songs, FilterTitle);
    }
}
