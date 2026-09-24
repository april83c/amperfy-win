using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Music folders of the server (port of MusicFoldersVC): search, synced when shown.
public sealed partial class MusicFoldersPage : Page, ILibraryCategoryPage
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new();
    private readonly LibraryListController _controller;
    private Account? _account;

    public LibraryDisplayType LibraryType { get; private set; } = LibraryDisplayType.Directories;

    public MusicFoldersPage()
    {
        InitializeComponent();
        _context.HostPageType = typeof(MusicFoldersPage);
        _controller = new LibraryListController(ItemsList, _context, LibraryListMode.Entities);
        Header.Title = "Directories";
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
        if (e.Parameter is LibraryDisplayType type) LibraryType = type;
        _account = _services.ActiveAccount;
        Reload();
        _ = SyncAsync(showBusy: false);
    }

    private void Reload()
    {
        if (_account is null) return;
        var folders = _services.Library.QueryMusicFolders(_account, Header.FilterText).ToList();
        _controller.SetItems(folders);
        Header.Info = Ui.Plural(folders.Count, "Folder", "Folders");
        CategoryPageHelper.UpdateEmptyState(EmptyHost, folders.Count == 0, !string.IsNullOrEmpty(Header.FilterText), Icons.Folder, "Directories");
    }

    private async Task SyncAsync(bool showBusy)
    {
        if (_account is not { } account || !_services.Settings.User.IsOnlineMode) return;
        Header.IsBusy = showBusy;
        if (await CategoryPageHelper.SyncAsync("Music Folders Sync", () => EntityActions.SyncerFor(account).SyncMusicFoldersAsync(), displayPopup: showBusy))
            Reload();
        Header.IsBusy = false;
    }
}
