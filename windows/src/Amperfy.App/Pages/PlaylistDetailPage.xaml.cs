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
/// mode to rename and add songs. Selected songs can be removed, dragged and cut / pasted (see
/// PlaylistDetailPage.Selection.cs). Changes are uploaded to the
/// server. The playlist is synced from the server when shown.
public sealed partial class PlaylistDetailPage : Page
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly LibraryListContext _context = new() { ShowAlbumInSubtitle = true };
    private readonly LibraryListController _controller;
    private ObservableCollection<object> _items = [];
    private readonly Button _editButton;
    private readonly Button _doneButton;
    private readonly Button _addButton;
    private readonly Button _removeButton;
    private Playlist? _playlist;
    private bool _isEditing;

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
        _removeButton = Ui.TextButton("Remove", Icons.Delete, (_, _) => _ = RemoveItemsAsync(SelectedPlaylistItems()), tooltip: "Remove selected songs (Del)");
        Header.ExtraButtons.Children.Add(_editButton);
        Toolbar.Buttons.Children.Add(_addButton);
        Toolbar.Buttons.Children.Add(_removeButton);
        Toolbar.Buttons.Children.Add(_doneButton);
        Toolbar.IsFilterVisible = false;
        Toolbar.FilterChanged += Reload;
        Toolbar.RefreshRequested += () => _ = SyncAsync(showBusy: true);
        Toolbar.AttachFinder(_controller);
        Toolbar.AttachAccelerators(this);

        ItemsList.SelectionChanged += (_, _) => _removeButton.IsEnabled = _isEditing && ItemsList.SelectedItems.Count > 0;
        SetupMultiSelection();
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
        CancelCut();
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

    private void Reload() => Reload(rebuild: false);

    // playable shown by each row (a sync can change the song of an existing playlist item)
    private List<AbstractPlayable?> _shownPlayables = [];

    /// Shows the playlist items. Changes (removed items, appended items, changed songs) are applied
    /// to the existing list so the scroll position stays; <paramref name="rebuild"/> recreates all
    /// rows (e.g. when switching the edit mode) and restores the scroll position.
    private void Reload(bool rebuild)
    {
        if (_playlist is not { } playlist) return;
        var onlyCached = Toolbar.OnlyCached && !_isEditing;
        // items, songs, albums, artists and artworks in a few batch queries (not one per row)
        try { _services.Library.LoadPlaylistItems(playlist); }
        catch (Exception ex) { Amperfy.Core.Common.AmperfyLog.Error("PlaylistDetail", $"Loading items failed: {ex.Message}"); }
        var entities = playlist.Items.Where(i => i.Playable is { } p && (!onlyCached || p.IsCached)).ToList();
        if (rebuild || !TryUpdateInPlace(entities)) Rebuild(entities);
        PruneCut();
        UpdateEmptyText(onlyCached);
    }

    private void Rebuild(List<PlaylistItem> entities)
    {
        var offset = Scroller?.VerticalOffset ?? 0;
        var index = 0;
        // one reset instead of one change notification per item
        _items = new ObservableCollection<object>(entities.Select(item => (object)new LibraryItem(item, _context, index++)));
        _shownPlayables = entities.Select(i => i.Playable).ToList();
        ItemsList.ItemsSource = _items;
        if (offset > 0)
        {
            // restore the scroll position once the new rows are laid out
            void Restore(object? sender, object e)
            {
                ItemsList.LayoutUpdated -= Restore;
                Scroller?.ChangeView(null, offset, null, disableAnimation: true);
            }
            ItemsList.LayoutUpdated += Restore;
        }
    }

    /// Applies removals, appended items and changed songs to the shown list. Returns false if the
    /// change is too big for that (then the list is rebuilt).
    private bool TryUpdateInPlace(List<PlaylistItem> entities)
    {
        if (_items.Count == 0 || _items.Count != _shownPlayables.Count) return false;
        var shown = _items.OfType<LibraryItem>().ToList();
        if (shown.Count != _items.Count) return false;
        if (TryReorderInPlace(shown, entities)) return true;
        // shown items not in the new list (in order) are removed; new items after the last kept one are appended
        var keep = new bool[shown.Count];
        var next = 0;
        for (var i = 0; i < shown.Count; i++)
        {
            if (next < entities.Count && ReferenceEquals(shown[i].Entity, entities[next]))
            {
                keep[i] = true;
                next++;
            }
        }
        var removed = shown.Count - next;
        var appended = entities.Count - next;
        if (removed + appended > 200) return false;
        for (var i = shown.Count - 1; i >= 0; i--)
        {
            if (keep[i]) continue;
            _items.RemoveAt(i);
            _shownPlayables.RemoveAt(i);
        }
        for (var i = next; i < entities.Count; i++)
        {
            _items.Add(new LibraryItem(entities[i], _context, i));
            _shownPlayables.Add(entities[i].Playable);
        }
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i] is not LibraryItem item) continue;
            item.Index = i;
            // song of the item changed (server sync): new row content
            if (!ReferenceEquals(_shownPlayables[i], entities[i].Playable))
            {
                _items[i] = new LibraryItem(entities[i], _context, i);
                _shownPlayables[i] = entities[i].Playable;
            }
        }
        return true;
    }

    /// Same items in a different order (moved songs): moves the rows (keeps the scroll position and
    /// the selection).
    private bool TryReorderInPlace(List<LibraryItem> shown, List<PlaylistItem> entities)
    {
        if (shown.Count != entities.Count) return false;
        var shownSet = shown.Select(i => i.Entity).ToHashSet();
        if (!entities.All(e => shownSet.Contains(e))) return false;
        if (shown.Select(i => i.Entity).SequenceEqual(entities)) return false; // unchanged: other checks
        for (var i = 0; i < entities.Count; i++)
        {
            var current = (LibraryItem)_items[i];
            if (ReferenceEquals(current.Entity, entities[i])) continue;
            var from = _items.IndexOf(shown.First(x => ReferenceEquals(x.Entity, entities[i])));
            _items.Move(from, i);
        }
        for (var i = 0; i < _items.Count; i++)
        {
            var item = (LibraryItem)_items[i];
            item.Index = i;
            _shownPlayables[i] = entities[i].Playable;
        }
        return true;
    }

    private ScrollViewer? _scroller;

    private ScrollViewer? Scroller => _scroller ??= FindScrollViewer(ItemsList);

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer) return viewer;
            if (FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }

    private void UpdateEmptyText(bool onlyCached)
    {
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
        UpdateDragAndDrop();
    }

    private void SetEditing(bool isEditing)
    {
        if (isEditing && !CanEdit) return;
        _isEditing = isEditing;
        _context.IsEditMode = isEditing;
        _controller.ItemActivated = isEditing ? _ => { } : null;
        Header.IsEditingName = isEditing;
        ItemsList.SelectionMode = isEditing ? ListViewSelectionMode.Multiple : ListViewSelectionMode.Extended;
        UpdateDragAndDrop();
        UpdateEditControls();
        Reload(rebuild: true); // rows show the edit mode (reorder handle)
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
