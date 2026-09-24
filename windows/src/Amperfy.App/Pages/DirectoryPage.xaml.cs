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

/// Directory browsing (port of DirectoriesVC): subdirectories and songs of a directory; search
/// and cached filter; play the directory's songs. Synced from the server when shown.
public sealed partial class DirectoryPage : Page, ILibraryCategoryPage
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new();
    private readonly LibraryListController _controller;
    private MusicDirectory? _directory;
    private List<AbstractPlayable> _contextSongs = [];

    /// Highlights "Directories" in the sidebar.
    public LibraryDisplayType LibraryType => LibraryDisplayType.Directories;

    public DirectoryPage()
    {
        InitializeComponent();
        _context.HostPageType = typeof(DirectoryPage);
        _context.PlayContextProvider = item =>
            _directory is { } directory && item.Playable is { } song
                ? new PlayContext(directory, Math.Max(0, _contextSongs.IndexOf(song)), _contextSongs)
                : null;
        _context.Changed = () => Header.Refresh();
        _controller = new LibraryListController(ItemsList, _context, LibraryListMode.Mixed);
        Toolbar.FilterPlaceholder = "Directories and Songs";
        Toolbar.FilterChanged += Reload;
        Toolbar.RefreshRequested += () => _ = SyncAsync(showBusy: true);
        Toolbar.AttachAccelerators(this);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        (_directory, _) = NavigationArgs.Unpack<MusicDirectory>(e.Parameter);
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.OfflineModeChanged += OnOfflineModeChanged;
        if (_directory is not { } directory) return;
        Header.Configure(directory, new DetailHeaderOptions
        {
            HostPageType = typeof(DirectoryPage),
            PlayContext = () => Task.FromResult<PlayContext?>(new PlayContext(directory, 0, _contextSongs)),
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

    private void Reload()
    {
        if (_directory is not { } directory) return;
        var filter = Toolbar.FilterText;
        var onlyCached = Toolbar.OnlyCached;
        var subdirectories = _services.Library.QuerySubdirectories(directory, filter).ToList();
        var songs = _services.Library.QueryDirectorySongs(directory, filter, onlyCached).ToList();
        _contextSongs = songs.Where(EntityActions.IsPlayable).Cast<AbstractPlayable>().ToList();
        var items = new List<object>();
        if (subdirectories.Count > 0 && songs.Count > 0) items.Add(new SectionHeaderItem(Ui.Plural(subdirectories.Count, "Directory", "Directories")));
        items.AddRange(subdirectories);
        if (subdirectories.Count > 0 && songs.Count > 0) items.Add(new SectionHeaderItem(Ui.Plural(songs.Count, "Song", "Songs")));
        items.AddRange(songs);
        _controller.SetItems(items);
        EmptyText.Text = string.IsNullOrEmpty(filter) && !onlyCached ? "No directories or songs" : "No results";
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task SyncAsync(bool showBusy)
    {
        if (_directory is not { Account: { } account } directory || !_services.Settings.User.IsOnlineMode) return;
        Header.IsBusy = showBusy;
        await CategoryPageHelper.SyncAsync("Directories Sync", () => EntityActions.SyncerFor(account).SyncAsync(directory), displayPopup: showBusy);
        Header.IsBusy = false;
        if (_directory != directory) return;
        Header.Refresh();
        Reload();
    }
}
