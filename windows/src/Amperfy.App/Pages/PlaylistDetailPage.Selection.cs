using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Amperfy.App.Services;
using Amperfy.Core.Model;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace Amperfy.App.Pages;

/// Working with selected songs of a playlist (in and outside the edit mode, online only):
/// - remove the selected songs (context menu, Delete key, Remove button in edit mode)
/// - drag the selected songs to another position; they keep their order
/// - cut the selected songs (context menu, Ctrl+X) and paste them after / before another song
///   (context menu, Ctrl+V pastes after the selected song); a bar shows the songs being held
public sealed partial class PlaylistDetailPage
{
    private List<PlaylistItem> _cut = [];
    private InfoBar? _cutBar;
    private List<PlaylistItem> _dragged = [];
    private Rectangle? _dropIndicator;
    private DispatcherQueueTimer? _autoScrollTimer;
    private double _lastDragY;
    private bool _isBusy;

    private void SetupMultiSelection()
    {
        ItemsList.DragItemsStarting += OnDragItemsStarting;
        ItemsList.DragItemsCompleted += (_, _) => EndDrag();
        ItemsList.DragOver += OnDragOver;
        ItemsList.DragLeave += (_, _) => HideDropIndicator();
        ItemsList.Drop += OnDrop;
        ItemsList.KeyDown += OnListKeyDown;
        _context.IsDimmed = item => _cut.Count > 0 && item.Entity is PlaylistItem p && _cut.Contains(p);

        if (Content is Grid root)
        {
            var overlay = new Canvas { IsHitTestVisible = false };
            _dropIndicator = new Rectangle { Height = 3, RadiusX = 1.5, RadiusY = 1.5, Visibility = Visibility.Collapsed };
            overlay.Children.Add(_dropIndicator);
            root.Children.Add(overlay);

            _cutBar = new InfoBar
            {
                IsOpen = false,
                IsClosable = true,
                Severity = InfoBarSeverity.Informational,
                IconSource = new FontIconSource { Glyph = "" },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(24, 0, 24, 16),
                MaxWidth = 720,
                Background = Ui.Brush("SolidBackgroundFillColorBaseBrush"),
                Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow(),
                Translation = new System.Numerics.Vector3(0, 0, 16),
            };
            _cutBar.CloseButtonClick += (_, _) => CancelCut();
            var pasteTop = new Button { Content = "Paste at top" };
            pasteTop.Click += (_, _) => _ = PasteAsync(after: null);
            _cutBar.ActionButton = pasteTop;
            root.Children.Add(_cutBar);
        }
    }

    /// Dragging, cut and paste need a playlist that can be changed on the server.
    private void UpdateDragAndDrop()
    {
        var canMove = CanEdit;
        ItemsList.CanDragItems = canMove;
        ItemsList.AllowDrop = canMove;
        ItemsList.CanReorderItems = false; // custom drop handling: moves all dragged songs
        if (!canMove) CancelCut();
    }

    /// The songs an action applies to: the selection if the item is part of a multi selection,
    /// otherwise the item. In playlist order.
    private List<PlaylistItem> TargetsFor(LibraryItem item)
    {
        if (item.Entity is not PlaylistItem clicked) return [];
        var selected = SelectedPlaylistItems();
        return selected.Count > 1 && selected.Contains(clicked) ? selected : [clicked];
    }

    private List<PlaylistItem> SelectedPlaylistItems()
    {
        var selected = ItemsList.SelectedItems.OfType<LibraryItem>().Select(i => i.Entity).OfType<PlaylistItem>().ToHashSet();
        return _playlist?.Items.Where(selected.Contains).ToList() ?? [];
    }

    private static string Songs(int count) => count == 1 ? "Song" : $"{count} Songs";

    private IEnumerable<MenuFlyoutItemBase> BuildExtraMenuItems(LibraryItem item)
    {
        if (!CanEdit || _playlist is null || item.Entity is not PlaylistItem target) yield break;
        var targets = TargetsFor(item);
        if (_cut.Count > 0)
        {
            var paste = $"Paste {Songs(_cut.Count)}";
            yield return Ui.MenuItem($"{paste} After This", LibraryGlyphs.Paste, () => _ = PasteAsync(after: target));
            yield return Ui.MenuItem($"{paste} Before This", LibraryGlyphs.Paste, () => _ = PasteAsync(after: ItemBefore(target)));
            yield return Ui.MenuItem("Cancel Cut", Icons.Clear, CancelCut);
            yield return new MenuFlyoutSeparator();
        }
        var count = targets.Count == 1 ? "" : $" {targets.Count} Songs";
        yield return Ui.MenuItem($"Cut{count}", LibraryGlyphs.Cut, () => Cut(targets));
        yield return Ui.MenuItem($"Remove{count} from Playlist", Icons.Delete, () => _ = RemoveItemsAsync(targets));
    }

    /// The item before the given one (null at the start), skipping the songs being moved.
    private PlaylistItem? ItemBefore(PlaylistItem item)
    {
        if (_playlist is not { } playlist) return null;
        var items = playlist.Items;
        var index = items.ToList().IndexOf(item);
        for (var i = index - 1; i >= 0; i--)
        {
            if (!_cut.Contains(items[i])) return items[i];
        }
        return null;
    }

    // --- remove --------------------------------------------------------------------------------

    private async Task RemoveItemsAsync(List<PlaylistItem> items)
    {
        if (_playlist is not { } playlist || items.Count == 0 || _isBusy) return;
        var indexes = items.Select(i => playlist.GetFirstIndex(i)).OfType<int>().Distinct().Order().ToList();
        if (indexes.Count == 0) return;
        _isBusy = true;
        try
        {
            if (playlist.Account is { } account && _services.Settings.User.IsOnlineMode)
                await EntityActions.SyncerFor(account).SyncUploadPlaylistDeleteSongsAsync(playlist, indexes);
            playlist.RemoveItems(items);
            _services.Library.SaveContext();
        }
        catch (Exception ex)
        {
            _services.EventLogger.Report("Playlist Upload Entry Remove", ex);
        }
        finally
        {
            _isBusy = false;
        }
        Header.Refresh();
        Reload();
        UpdateEditControls();
    }

    // --- move (drag and drop, cut and paste) -------------------------------------------------------

    /// Moves the items as a block after <paramref name="after"/> (null: to the start) and uploads the new order.
    private async Task MoveAsync(List<PlaylistItem> items, PlaylistItem? after)
    {
        if (_playlist is not { } playlist || items.Count == 0 || _isBusy) return;
        if (!playlist.MoveItems(items, after)) return;
        _services.Library.SaveContext();
        Reload();
        // keep the moved songs selected
        ItemsList.SelectedItems.Clear();
        foreach (var libraryItem in _items.OfType<LibraryItem>().Where(i => i.Entity is PlaylistItem p && items.Contains(p)))
            ItemsList.SelectedItems.Add(libraryItem);
        Header.Refresh();
        if (playlist.Account is { } account)
        {
            _isBusy = true;
            try
            {
                await CategoryPageHelper.SyncAsync("Playlist Upload Order Update", () => EntityActions.SyncerFor(account).SyncUploadPlaylistOrderAsync(playlist));
            }
            finally
            {
                _isBusy = false;
            }
        }
    }

    private void Cut(List<PlaylistItem> items)
    {
        if (items.Count == 0) return;
        _cut = items;
        UpdateCutBar();
        _context.NotifyLayoutChanged();
    }

    private void CancelCut()
    {
        if (_cut.Count == 0) return;
        _cut = [];
        UpdateCutBar();
        _context.NotifyLayoutChanged();
    }

    private async Task PasteAsync(PlaylistItem? after)
    {
        if (_cut.Count == 0) return;
        var items = _cut;
        CancelCut();
        await MoveAsync(items, after);
    }

    /// Removes songs that are no longer in the playlist from the cut songs (after a sync or removal).
    private void PruneCut()
    {
        if (_cut.Count == 0 || _playlist is not { } playlist) return;
        var remaining = playlist.Items.Where(_cut.Contains).ToList();
        if (remaining.Count == _cut.Count) return;
        _cut = remaining;
        UpdateCutBar();
    }

    private void UpdateCutBar()
    {
        if (_cutBar is null) return;
        _cutBar.IsOpen = _cut.Count > 0;
        if (_cut.Count == 0) return;
        _cutBar.Title = $"Holding {Songs(_cut.Count).ToLowerInvariant()}";
        _cutBar.Message = "Right-click a song and choose \"Paste After This\", or select a song and press Ctrl+V. Esc cancels.";
    }

    // --- keyboard ------------------------------------------------------------------------------

    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox || !CanEdit) return;
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        switch (e.Key)
        {
            case VirtualKey.Delete:
                e.Handled = true;
                _ = RemoveItemsAsync(SelectedPlaylistItems());
                break;
            case VirtualKey.X when ctrl:
                e.Handled = true;
                Cut(SelectedPlaylistItems());
                break;
            case VirtualKey.V when ctrl && _cut.Count > 0:
                e.Handled = true;
                // after the (last) selected song
                if (SelectedPlaylistItems().LastOrDefault() is { } target) _ = PasteAsync(after: target);
                break;
            case VirtualKey.Escape when _cut.Count > 0:
                e.Handled = true;
                CancelCut();
                break;
        }
    }

    // --- drag and drop ---------------------------------------------------------------------------

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (!CanEdit || _playlist is not { } playlist)
        {
            e.Cancel = true;
            return;
        }
        // all dragged songs (the selection if a selected song is dragged), in playlist order
        var dragged = e.Items.OfType<LibraryItem>().Select(i => i.Entity).OfType<PlaylistItem>().ToHashSet();
        _dragged = playlist.Items.Where(dragged.Contains).ToList();
        if (_dragged.Count == 0)
        {
            e.Cancel = true;
            return;
        }
        e.Data.RequestedOperation = DataPackageOperation.Move;
        e.Data.SetText(string.Join(Environment.NewLine, _dragged.Select(i => i.Playable?.Title ?? "")));
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (_dragged.Count == 0)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }
        _lastDragY = e.GetPosition(ItemsList).Y;
        StartAutoScroll();
        var target = DropTarget(e);
        ShowDropIndicator(target?.LineY);
        e.AcceptedOperation = target is null ? DataPackageOperation.None : DataPackageOperation.Move;
        e.DragUIOverride.Caption = _dragged.Count == 1 ? "Move" : $"Move {_dragged.Count} songs";
        e.DragUIOverride.IsGlyphVisible = false;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var target = DropTarget(e);
        var dragged = _dragged;
        EndDrag();
        if (target is null || dragged.Count == 0) return;
        e.AcceptedOperation = DataPackageOperation.Move;
        _ = MoveAsync(dragged, target.Value.After);
    }

    private void EndDrag()
    {
        _dragged = [];
        HideDropIndicator();
        _autoScrollTimer?.Stop();
    }

    /// Insert position under the pointer: after which item the songs go (null: start) and the y
    /// position of the insertion line (relative to the list). Null if not over the rows.
    private (PlaylistItem? After, double LineY)? DropTarget(DragEventArgs e)
    {
        if (ItemsList.ItemsPanelRoot is not Panel panel) return null;
        var y = e.GetPosition(ItemsList).Y;
        var rows = new List<(LibraryItem Item, double Top, double Bottom)>();
        foreach (var child in panel.Children)
        {
            if (child is not ListViewItem { Content: LibraryItem item } container || container.Visibility != Visibility.Visible) continue;
            var bounds = container.TransformToVisual(ItemsList).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
            rows.Add((item, bounds.Top, bounds.Bottom));
        }
        if (rows.Count == 0) return null;
        rows.Sort((a, b) => a.Top.CompareTo(b.Top));
        var first = rows[0];
        if (y < first.Top)
        {
            // just above the first song of the playlist: to the start; otherwise (header) no target
            return _items.IndexOf(first.Item) == 0 && y >= first.Top - 24 ? (null, first.Top) : null;
        }
        foreach (var (item, top, bottom) in rows)
        {
            if (y > bottom) continue;
            var index = _items.IndexOf(item);
            if (y < (top + bottom) / 2)
            {
                // upper half: before this song = after the previous one
                var previous = index > 0 ? (_items[index - 1] as LibraryItem)?.Entity as PlaylistItem : null;
                return (previous, top);
            }
            return (item.Entity as PlaylistItem, bottom);
        }
        // below the last realized row: only if it is the last song
        var last = rows[^1];
        return ReferenceEquals(last.Item, _items.LastOrDefault()) ? (last.Item.Entity as PlaylistItem, last.Bottom) : null;
    }

    private void ShowDropIndicator(double? listY)
    {
        if (_dropIndicator is null) return;
        if (listY is not { } y)
        {
            HideDropIndicator();
            return;
        }
        var origin = ItemsList.TransformToVisual(Content as UIElement).TransformPoint(new Point(0, 0));
        _dropIndicator.Fill = Ui.ThemeAccent;
        _dropIndicator.Width = Math.Max(0, ItemsList.ActualWidth - 48);
        Canvas.SetLeft(_dropIndicator, origin.X + 24);
        Canvas.SetTop(_dropIndicator, origin.Y + y - 1.5);
        _dropIndicator.Visibility = Visibility.Visible;
    }

    private void HideDropIndicator()
    {
        if (_dropIndicator is not null) _dropIndicator.Visibility = Visibility.Collapsed;
    }

    /// Scrolls the list while dragging near its top or bottom edge (big playlists).
    private void StartAutoScroll()
    {
        if (_autoScrollTimer is null)
        {
            _autoScrollTimer = DispatcherQueue.CreateTimer();
            _autoScrollTimer.Interval = TimeSpan.FromMilliseconds(30);
            _autoScrollTimer.Tick += (_, _) =>
            {
                if (_dragged.Count == 0 || Scroller is not { } scroller)
                {
                    _autoScrollTimer.Stop();
                    return;
                }
                const double edge = 56;
                var height = ItemsList.ActualHeight;
                double delta = 0;
                if (_lastDragY < edge) delta = -Math.Max(4, (edge - _lastDragY) / 2);
                else if (_lastDragY > height - edge) delta = Math.Max(4, (_lastDragY - (height - edge)) / 2);
                if (delta != 0) scroller.ChangeView(null, Math.Max(0, scroller.VerticalOffset + delta), null, disableAnimation: true);
            };
        }
        if (!_autoScrollTimer.IsRunning) _autoScrollTimer.Start();
    }

    // --- E2E tour hooks --------------------------------------------------------------------------

    /// Selects the songs at the list positions and cuts them (E2E tour).
    internal void SelectAndCutForTour(params int[] positions)
    {
        ItemsList.SelectedItems.Clear();
        foreach (var position in positions)
        {
            if (position < _items.Count) ItemsList.SelectedItems.Add(_items[position]);
        }
        Cut(SelectedPlaylistItems());
    }

    /// Pastes the cut songs after the song at the list position and returns the new song order (E2E tour).
    internal async Task<string> PasteAfterForTourAsync(int position)
    {
        if (position < _items.Count && _items[position] is LibraryItem { Entity: PlaylistItem target }) await PasteAsync(target);
        return string.Join(" | ", _playlist?.Items.Select(i => i.Playable?.Title ?? "?") ?? []);
    }
}
