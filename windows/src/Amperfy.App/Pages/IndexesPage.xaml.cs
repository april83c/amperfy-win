using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Top level directories of a music folder (port of IndexesVC): search, synced when shown.
public sealed partial class IndexesPage : Page, ILibraryCategoryPage
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new();
    private readonly LibraryListController _controller;
    private MusicFolder? _folder;

    /// Highlights "Directories" in the sidebar.
    public LibraryDisplayType LibraryType => LibraryDisplayType.Directories;

    public IndexesPage()
    {
        InitializeComponent();
        _context.HostPageType = typeof(IndexesPage);
        _controller = new LibraryListController(ItemsList, _context, LibraryListMode.Entities);
        Header.FilterPlaceholder = "Search in \"Directories\"";
        Header.IsPlayVisible = false;
        Header.IsShuffleVisible = false;
        Header.FilterChanged += (_, _) => Reload();
        Header.AttachAccelerators(this, () => _ = SyncAsync(showBusy: true));
        Header.AddCommand("Refresh", Icons.Refresh, () => _ = SyncAsync(showBusy: true), "Refresh (F5)");
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        (_folder, _) = NavigationArgs.Unpack<MusicFolder>(e.Parameter);
        Header.Title = _folder?.Name ?? "Music Folder";
        Reload();
        _ = SyncAsync(showBusy: false);
    }

    private void Reload()
    {
        if (_folder is null) return;
        var directories = _services.Library.QueryMusicFolderDirectories(_folder, Header.FilterText).ToList();
        _controller.SetItems(directories);
        Header.Info = Ui.Plural(directories.Count, "Directory", "Directories");
        CategoryPageHelper.UpdateEmptyState(EmptyHost, directories.Count == 0, !string.IsNullOrEmpty(Header.FilterText), Icons.Folder, "Directories");
    }

    private async Task SyncAsync(bool showBusy)
    {
        if (_folder is not { Account: { } account } folder || !_services.Settings.User.IsOnlineMode) return;
        Header.IsBusy = showBusy;
        if (await CategoryPageHelper.SyncAsync("Indexes Sync", () => EntityActions.SyncerFor(account).SyncIndexesAsync(folder), displayPopup: showBusy))
            Reload();
        Header.IsBusy = false;
    }
}
