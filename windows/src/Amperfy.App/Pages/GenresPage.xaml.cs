using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Genres (port of GenresVC): cached filter, in-page search, jump to letter, refresh.
public sealed partial class GenresPage : Page, ILibraryCategoryPage
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new() { ShowArtwork = false };
    private readonly LibraryListController _controller;
    private Account? _account;
    private bool _onlyCached;
    private IQueryable<Genre>? _query;

    public LibraryDisplayType LibraryType { get; private set; } = LibraryDisplayType.Genres;

    public GenresPage()
    {
        InitializeComponent();
        _context.HostPageType = typeof(GenresPage);
        _controller = new LibraryListController(ItemsList, _context, LibraryListMode.Entities);
        Header.Title = "Genres";
        Header.FilterPlaceholder = "Search in \"Genres\"";
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
        _onlyCached = _services.Settings.User.IsOfflineMode;
        BuildCommands();
        Reload();
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
        CategoryPageHelper.AddCachedToggle(Header, () => _onlyCached, isChecked =>
        {
            _onlyCached = isChecked;
            Reload();
        });
        var jump = Header.AddCommand("Jump to", LibraryGlyphs.JumpTo, () => { }, "Jump to letter");
        jump.Flyout = CategoryPageHelper.CreateJumpFlyout(
            () => _query is null ? [] : LibraryStorage.GetSectionInitials(_query),
            section =>
            {
                if (_query is not null) _controller.ScrollToIndex(LibraryStorage.CountBeforeSectionInitial(_query, section));
            });
        Header.AddCommand("Refresh", Icons.Refresh, () => _ = RefreshAsync(), "Refresh (F5)");
    }

    private void Reload()
    {
        if (_account is null) return;
        var onlyCached = _onlyCached || _services.Settings.User.IsOfflineMode;
        var query = LibraryStorage.SortGenres(_services.Library.QueryGenres(_account, Header.FilterText, onlyCached));
        _query = query;
        var count = query.Count();
        _controller.SetIncrementalSource((skip, take) => query.Skip(skip).Take(take).ToList(), count);
        Header.Info = Ui.Plural(count, "Genre", "Genres");
        CategoryPageHelper.UpdateEmptyState(EmptyHost, count == 0, !string.IsNullOrEmpty(Header.FilterText), Icons.Genre, "Genres");
    }

    private async Task RefreshAsync()
    {
        if (_account is not { } account || !_services.Settings.User.IsOnlineMode) return;
        Header.IsBusy = true;
        await CategoryPageHelper.SyncNewestLibraryElementsAsync(account, "Genres Newest Elements Sync");
        Header.IsBusy = false;
        Reload();
    }
}
