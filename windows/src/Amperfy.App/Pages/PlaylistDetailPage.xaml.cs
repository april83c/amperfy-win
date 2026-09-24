using System.Collections.ObjectModel;
using Amperfy.App.Controls;
using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Library.Dialogs;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using VirtualKey = Windows.System.VirtualKey;

namespace Amperfy.App.Pages;

/// Playlist detail (port of PlaylistDetailVC + PlaylistEditVC): header, the playlist items; edit
/// mode to rename, reorder (drag and drop), remove and add songs. Changes are uploaded to the
/// server. The playlist is synced from the server when shown.
public sealed partial class PlaylistDetailPage : Page
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new() { ShowAlbumInSubtitle = true };
    private readonly LibraryListController _controller;
    private readonly ObservableCollection<object> _items = [];
    private readonly Button _editButton;
    private readonly Button _doneButton;
    private readonly Button _addButton;
    private readonly Button _removeButton;
    private Playlist? _playlist;
    private bool _isEditing;
    private LibraryItem? _draggedItem;
    private List<object> _dragSnapshot = [];

    public PlaylistDetailPage()
    {
        InitializeComponent();
        _context.HostPageType = typeof(PlaylistDetailPage);
        _context.PlayContextProvider = PlayContextFor;
        _context.Changed = () => Header.Refresh();
        _context.ExtraMenuItems = BuildExtraMenuItems;
        _controller = new LibraryListController(ItemsList, _context, LibraryListMode.Playables);
        ItemsList.ItemsSource = _items;

        _editButton = Ui.TextButton("Edit", Icons.Edit, (_, _) => SetEditing(true), tooltip: "Edit playlist");
        _doneButton = Ui.TextButton("Done", LibraryGlyphs.Accept, (_, _) => _ = FinishEditingAsync(), accent: true, tooltip: "Finish editing");
        _addButton = Ui.TextButton("Add Songs", Icons.Add, (_, _) => _ = AddSongsAsync(), tooltip: "Add songs from the library");
        _removeButton = Ui.TextButton("Remove", Icons.Delete, (_, _) => _ = RemoveSelectedAsync(), tooltip: "Remove selected songs (Del)");
        Header.ExtraButtons.Children.Add(_editButton);
        Toolbar.Buttons.Children.Add(_addButton);
        Toolbar.Buttons.Children.Add(_removeButton);
        Toolbar.Buttons.Children.Add(_doneButton);
        Toolbar.IsFilterVisible = false;
        Toolbar.FilterChanged += Reload;
        Toolbar.RefreshRequested += () => _ = SyncAsync(showBusy: true);
        Toolbar.AttachAccelerators(this);

        ItemsList.SelectionChanged += (_, _) => _removeButton.IsEnabled = _isEditing && ItemsList.SelectedItems.Count > 0;
        ItemsList.DragItemsStarting += OnDragItemsStarting;
        ItemsList.DragItemsCompleted += OnDragItemsCompleted;
        ItemsList.KeyDown += OnListKeyDown;
        UpdateEditControls();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        (_playlist, _) = NavigationArgs.Unpack<Playlist>(e.Parameter);
        LibraryEventHub.EnsureInitialized();
        LibraryEventHub.OfflineModeChanged += OnOfflineModeChanged;
        LibraryEventHub.EntityChanged += OnEntityChanged;
        if (_playlist is not { } playlist) return;
        Header.Configure(playlist, new DetailHeaderOptions
        {
            HostPageType = typeof(PlaylistDetailPage),
            PlayContext = () => Task.FromResult<PlayContext?>(new PlayContext(playlist, 0, ContextPlayables())),
            Changed = Reload,
        });
        UpdateEditControls();
        Reload();
        _ = SyncAsync(showBusy: false);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LibraryEventHub.OfflineModeChanged -= OnOfflineModeChanged;
        LibraryEventHub.EntityChanged -= OnEntityChanged;
        if (_isEditing) _ = FinishEditingAsync();
    }

    private void OnOfflineModeChanged()
    {
        if (_services.Settings.User.IsOfflineMode && _isEditing) SetEditing(false);
        Toolbar.UpdateOfflineMode();
        UpdateEditControls();
        Header.Refresh();
        Reload();
    }

    private void OnEntityChanged(object entity)
    {
        if (ReferenceEquals(entity, _playlist) && !_isEditing)
        {
            Header.Refresh();
            Reload();
        }
    }

    private bool CanEdit => _playlist is { IsSmartPlaylist: false } && _services.Settings.User.IsOnlineMode;

    private List<AbstractPlayable> ContextPlayables()
    {
        if (_playlist is not { } playlist) return [];
        var onlyCached = Toolbar.OnlyCached;
        return playlist.Items.Select(i => i.Playable).OfType<AbstractPlayable>()
            .Where(p => !onlyCached || p.IsCached)
            .Where(EntityActions.IsPlayable).ToList();
    }

    private PlayContext? PlayContextFor(LibraryItem item)
    {
        if (_playlist is not { } playlist || item.Playable is not { } playable) return null;
        var playables = ContextPlayables();
        // index of the item among the playable items before it (duplicates in playlists)
        var before = _items.OfType<LibraryItem>().TakeWhile(i => !ReferenceEquals(i, item))
            .Count(i => i.Playable is { } p && playables.Contains(p) && (!Toolbar.OnlyCached || p.IsCached));
        var index = Math.Min(before, Math.Max(0, playables.Count - 1));
        if (index < playables.Count && !ReferenceEquals(playables[index], playable)) index = Math.Max(0, playables.IndexOf(playable));
        return new PlayContext(playlist, index, playables);
    }

    private void Reload()
    {
        if (_playlist is not { } playlist) return;
        var onlyCached = Toolbar.OnlyCached && !_isEditing;
        _items.Clear();
        var index = 0;
        foreach (var item in playlist.Items)
        {
            if (item.Playable is not { } playable) continue;
            if (onlyCached && !playable.IsCached) continue;
            _items.Add(new LibraryItem(item, _context, index++));
        }
        EmptyText.Text = onlyCached ? "No cached songs" : "No songs";
        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- edit mode -------------------------------------------------------------------------------

    private void UpdateEditControls()
    {
        _editButton.Visibility = CanEdit && !_isEditing ? Visibility.Visible : Visibility.Collapsed;
        _doneButton.Visibility = _isEditing ? Visibility.Visible : Visibility.Collapsed;
        _addButton.Visibility = _isEditing ? Visibility.Visible : Visibility.Collapsed;
        _removeButton.Visibility = _isEditing ? Visibility.Visible : Visibility.Collapsed;
        _removeButton.IsEnabled = _isEditing && ItemsList.SelectedItems.Count > 0;
        Toolbar.IsCachedToggleVisible = !_isEditing;
        Toolbar.IsRefreshVisible = !_isEditing;
    }

    private void SetEditing(bool isEditing)
    {
        if (isEditing && !CanEdit) return;
        _isEditing = isEditing;
        _context.IsEditMode = isEditing;
        _controller.ItemActivated = isEditing ? _ => { } : null;
        Header.IsEditingName = isEditing;
        ItemsList.SelectionMode = isEditing ? ListViewSelectionMode.Multiple : ListViewSelectionMode.Extended;
        ItemsList.CanDragItems = isEditing;
        ItemsList.CanReorderItems = isEditing;
        ItemsList.AllowDrop = isEditing;
        UpdateEditControls();
        Reload();
    }

    private async Task FinishEditingAsync()
    {
        if (_playlist is not { } playlist) return;
        var newName = Header.EditedName.Trim();
        SetEditing(false);
        if (newName.Length == 0 || newName == playlist.Name) return;
        playlist.Name = newName;
        _services.Library.SaveContext();
        Header.Refresh();
        if (playlist.Account is { } account)
            await CategoryPageHelper.SyncAsync("Playlist Update Name", () => EntityActions.SyncerFor(account).SyncUploadPlaylistNameAsync(playlist));
    }

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _draggedItem = e.Items.OfType<LibraryItem>().FirstOrDefault();
        _dragSnapshot = _items.ToList();
        if (e.Items.Count != 1) e.Cancel = true; // one item at a time (like the Swift table view)
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs e)
    {
        if (_playlist is not { } playlist || _draggedItem is not { } item || e.DropResult != DataPackageOperation.Move) return;
        var newPosition = _items.IndexOf(item);
        _draggedItem = null;
        if (newPosition < 0 || newPosition >= _dragSnapshot.Count) return;
        // map list positions to playlist indexes (the list may skip orphaned items)
        var target = (_dragSnapshot[newPosition] as LibraryItem)?.Entity as PlaylistItem;
        if (item.Entity is not PlaylistItem moved || target is null) return;
        if (playlist.GetFirstIndex(moved) is not { } from || playlist.GetFirstIndex(target) is not { } to || from == to) return;
        playlist.MovePlaylistItem(from, to);
        _services.Library.SaveContext();
        var index = 0;
        foreach (var libraryItem in _items.OfType<LibraryItem>()) libraryItem.Index = index++;
        Header.Refresh();
        if (playlist.Account is { } account)
            _ = CategoryPageHelper.SyncAsync("Playlist Upload Order Update", () => EntityActions.SyncerFor(account).SyncUploadPlaylistOrderAsync(playlist));
    }

    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_isEditing || e.Key != VirtualKey.Delete || e.OriginalSource is TextBox) return;
        e.Handled = true;
        _ = RemoveSelectedAsync();
    }

    private async Task RemoveSelectedAsync()
    {
        if (_playlist is not { } playlist) return;
        var selected = ItemsList.SelectedItems.OfType<LibraryItem>().Select(i => i.Entity).OfType<PlaylistItem>().ToList();
        if (selected.Count == 0) return;
        await RemoveItemsAsync(playlist, selected);
    }

    private async Task RemoveItemsAsync(Playlist playlist, List<PlaylistItem> items)
    {
        var syncer = playlist.Account is { } account && _services.Settings.User.IsOnlineMode ? EntityActions.SyncerFor(account) : null;
        try
        {
            foreach (var item in items.OrderByDescending(i => i.Order))
            {
                if (playlist.GetFirstIndex(item) is not { } index) continue;
                if (syncer is not null) await syncer.SyncUploadPlaylistDeleteSongAsync(playlist, index);
                playlist.Remove(index);
            }
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Playlist Upload Entry Remove", ex);
        }
        _services.Library.SaveContext();
        Header.Refresh();
        Reload();
        UpdateEditControls();
    }

    private IEnumerable<MenuFlyoutItemBase> BuildExtraMenuItems(LibraryItem item)
    {
        if (!CanEdit || _playlist is not { } playlist || item.Entity is not PlaylistItem playlistItem) yield break;
        yield return Ui.MenuItem("Remove from Playlist", Icons.Delete, () => _ = RemoveItemsAsync(playlist, [playlistItem]));
    }

    private async Task AddSongsAsync()
    {
        if (_playlist is not { Account: { } account } playlist) return;
        var songs = await PlaylistAddSongsDialog.ShowAsync(account, playlist.Name);
        if (songs.Count == 0) return;
        try
        {
            if (_services.Settings.User.IsOnlineMode)
                await EntityActions.SyncerFor(account).SyncUploadPlaylistAddSongsAsync(playlist, songs);
            playlist.Append(songs);
            _services.Library.SaveContext();
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Playlist Add Songs", ex);
        }
        Header.Refresh();
        Reload();
    }

    private async Task SyncAsync(bool showBusy)
    {
        if (_playlist is not { Account: { } account } playlist || !_services.Settings.User.IsOnlineMode || _isEditing) return;
        Header.IsBusy = showBusy;
        await CategoryPageHelper.SyncAsync("Playlist Sync", () => EntityActions.SyncerFor(account).SyncDownAsync(playlist), displayPopup: showBusy);
        Header.IsBusy = false;
        if (_playlist != playlist || _isEditing) return;
        Header.Refresh();
        Reload();
    }
}
