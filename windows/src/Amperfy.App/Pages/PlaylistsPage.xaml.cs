using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Library.Dialogs;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Playlists (port of PlaylistsVC): sort, category filter (all / cached / user / smart), search,
/// new playlist, delete (context menu), sync all playlists, refresh.
public sealed partial class PlaylistsPage : Page, ILibraryCategoryPage
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new();
    private readonly LibraryListController _controller;
    private Account? _account;
    private PlaylistSortType _sortType = PlaylistSortType.Name;
    private PlaylistSearchCategory _category = PlaylistSearchCategory.All;
    private IQueryable<Playlist>? _query;
    private AppBarButton? _jumpButton;

    public LibraryDisplayType LibraryType { get; private set; } = LibraryDisplayType.Playlists;

    public PlaylistsPage()
    {
        InitializeComponent();
        _context.HostPageType = typeof(PlaylistsPage);
        _context.Changed = Reload;
        _context.ExtraMenuItems = BuildExtraMenuItems;
        _controller = new LibraryListController(ItemsList, _context, LibraryListMode.Entities);
        Header.Title = "Playlists";
        Header.FilterPlaceholder = "Search in \"Playlists\"";
        Header.IsPlayVisible = false;
        Header.IsShuffleVisible = false;
        Header.FilterChanged += (_, _) => Reload();
        Header.AttachAccelerators(this, () => _ = RefreshAsync());
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is LibraryDisplayType type) LibraryType = type;
        _account = _services.ActiveAccount;
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.OfflineModeChanged += OnOfflineModeChanged;
        LibraryEventHub.EntityChanged += OnEntityChanged;
        _sortType = _services.Settings.User.PlaylistsSortSetting;
        _category = _services.Settings.User.IsOfflineMode ? PlaylistSearchCategory.Cached : PlaylistSearchCategory.All;
        BuildCommands();
        Reload();
        _ = SyncPlaylistsAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LibraryEventHub.OfflineModeChanged -= OnOfflineModeChanged;
        LibraryEventHub.EntityChanged -= OnEntityChanged;
    }

    private void OnOfflineModeChanged()
    {
        _category = _services.Settings.User.IsOfflineMode ? PlaylistSearchCategory.Cached : PlaylistSearchCategory.All;
        BuildCommands();
        Reload();
    }

    private void OnEntityChanged(object entity)
    {
        if (entity is Playlist) Reload();
    }

    private bool IsAmpache => _account?.ApiType.AsServerApiType() == ServerApiType.Ampache;

    private void BuildCommands()
    {
        Header.Commands.PrimaryCommands.Clear();
        Header.Commands.SecondaryCommands.Clear();
        Header.AddCommand("New Playlist", Icons.Add, () => _ = CreatePlaylistAsync(), "Create a new playlist");
        Header.AddMenuCommand("Sort", LibraryGlyphs.Sort, () =>
        [
            Ui.RadioItem("Name", "sort", _sortType == PlaylistSortType.Name, () => ChangeSort(PlaylistSortType.Name)),
            Ui.RadioItem("Last time played", "sort", _sortType == PlaylistSortType.LastPlayed, () => ChangeSort(PlaylistSortType.LastPlayed)),
            Ui.RadioItem("Change date", "sort", _sortType == PlaylistSortType.LastChanged, () => ChangeSort(PlaylistSortType.LastChanged)),
            Ui.RadioItem("Duration", "sort", _sortType == PlaylistSortType.Duration, () => ChangeSort(PlaylistSortType.Duration)),
        ]);
        Header.AddMenuCommand("Filter", LibraryGlyphs.Filter, BuildFilterMenu);
        _jumpButton = Header.AddCommand("Jump to", LibraryGlyphs.JumpTo, () => { }, "Jump to letter");
        _jumpButton.Flyout = CategoryPageHelper.CreateJumpFlyout(
            () => _query is null ? [] : LibraryStorage.GetSectionInitials(_query),
            section =>
            {
                if (_query is not null) _controller.ScrollToIndex(LibraryStorage.CountBeforeSectionInitial(_query, section));
            });
        _jumpButton.Visibility = _sortType == PlaylistSortType.Name ? Visibility.Visible : Visibility.Collapsed;
        Header.AddCommand("Refresh", Icons.Refresh, () => _ = RefreshAsync(), "Refresh (F5)");
        if (_services.Settings.User.IsOnlineMode)
            Header.AddCommand("Sync All Playlists", Icons.Refresh, () => _ = SyncAllPlaylistsAsync(), secondary: true);
    }

    private IEnumerable<MenuFlyoutItemBase> BuildFilterMenu()
    {
        var offline = _services.Settings.User.IsOfflineMode;
        var all = Ui.RadioItem("All", "filter", _category == PlaylistSearchCategory.All, () => ChangeCategory(PlaylistSearchCategory.All));
        all.IsEnabled = !offline;
        yield return all;
        yield return Ui.RadioItem("Cached", "filter", _category == PlaylistSearchCategory.Cached, () => ChangeCategory(PlaylistSearchCategory.Cached));
        if (IsAmpache)
        {
            yield return Ui.RadioItem("User", "filter", _category == PlaylistSearchCategory.UserOnly, () => ChangeCategory(PlaylistSearchCategory.UserOnly));
            yield return Ui.RadioItem("Smart", "filter", _category == PlaylistSearchCategory.SmartOnly, () => ChangeCategory(PlaylistSearchCategory.SmartOnly));
        }
    }

    private void ChangeSort(PlaylistSortType sortType)
    {
        _sortType = sortType;
        _services.Settings.User.PlaylistsSortSetting = sortType;
        if (_jumpButton is not null) _jumpButton.Visibility = sortType == PlaylistSortType.Name ? Visibility.Visible : Visibility.Collapsed;
        Reload();
    }

    private void ChangeCategory(PlaylistSearchCategory category)
    {
        _category = category;
        Reload();
    }

    private void Reload()
    {
        if (_account is null) return;
        var category = _services.Settings.User.IsOfflineMode && _category == PlaylistSearchCategory.All ? PlaylistSearchCategory.Cached : _category;
        var query = LibraryStorage.SortPlaylists(_services.Library.QueryPlaylists(_account, Header.FilterText, category), _sortType);
        _query = query;
        var count = query.Count();
        _controller.SetIncrementalSource((skip, take) => query.Skip(skip).Take(take).ToList(), count);
        Header.Info = Ui.Plural(count, "Playlist", "Playlists");
        CategoryPageHelper.UpdateEmptyState(EmptyHost, count == 0, !string.IsNullOrEmpty(Header.FilterText), Icons.Playlist, "Playlists");
    }

    private IEnumerable<MenuFlyoutItemBase> BuildExtraMenuItems(LibraryItem item)
    {
        if (item.Entity is not Playlist playlist || !_services.Settings.User.IsOnlineMode || playlist.IsSmartPlaylist) yield break;
        yield return Ui.MenuItem("Rename", LibraryGlyphs.Rename, () => _ = RenameAsync(playlist));
        yield return Ui.MenuItem("Delete Playlist", Icons.Delete, () => _ = DeleteAsync(playlist));
    }

    private async Task RenameAsync(Playlist playlist)
    {
        var name = await DialogHelper.PromptTextAsync("Rename Playlist", "Playlist name", playlist.Name, "Rename");
        if (string.IsNullOrWhiteSpace(name) || name == playlist.Name || playlist.Account is not { } account) return;
        playlist.Name = name.Trim();
        _services.Library.SaveContext();
        Reload();
        await CategoryPageHelper.SyncAsync("Playlist Update Name", () => EntityActions.SyncerFor(account).SyncUploadPlaylistNameAsync(playlist));
    }

    private async Task DeleteAsync(Playlist playlist)
    {
        if (await EntityActions.DeletePlaylistAsync(playlist)) Reload();
    }

    private async Task CreatePlaylistAsync()
    {
        if (_account is not { } account) return;
        var name = await DialogHelper.PromptTextAsync("New Playlist", "Playlist name", "", "Create");
        if (string.IsNullOrWhiteSpace(name)) return;
        var playlist = await EntityActions.CreatePlaylistAsync(account, name);
        Reload();
        if (playlist is not null) EntityActions.Open(playlist);
    }

    private async Task SyncPlaylistsAsync()
    {
        if (_account is not { } account) return;
        if (await CategoryPageHelper.SyncAsync("Playlists Sync", () => EntityActions.SyncerFor(account).SyncDownPlaylistsWithoutSongsAsync(), displayPopup: false))
            Reload();
    }

    private async Task RefreshAsync()
    {
        if (!_services.Settings.User.IsOnlineMode) return;
        Header.IsBusy = true;
        await SyncPlaylistsAsync();
        Header.IsBusy = false;
    }

    /// Swift: "Sync All Playlists" option.
    private async Task SyncAllPlaylistsAsync()
    {
        if (_account is not { } account) return;
        Header.IsBusy = true;
        try
        {
            var syncer = EntityActions.SyncerFor(account);
            foreach (var playlist in _services.Library.GetPlaylists(account))
                await playlist.FetchAsync(_services.Settings, syncer);
            _services.EventLogger.Info("Sync All Playlists", "All playlists have been synced.");
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Sync All Playlists", ex);
        }
        Header.IsBusy = false;
        Reload();
    }
}
