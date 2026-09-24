using Amperfy.Core.Model;
using Amperfy.Core.Player;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Amperfy.App.Library;

public enum LibraryListMode
{
    /// Containers (albums, artists, ...): click opens the detail page.
    Entities,

    /// Songs / episodes / radios: click selects (extended selection), double click or Enter plays.
    Playables,

    /// Both: click opens containers, double click or Enter plays playables.
    Mixed,
}

/// Wires the desktop interaction of a library ListView/GridView: click opens containers, double
/// click / Enter plays playables (with the list's play context), stretched rows, incremental
/// loading sources and scrolling to an item.
public sealed class LibraryListController
{
    private readonly ListViewBase _list;
    private DateTime _lastPointerPress = DateTime.MinValue;

    public LibraryListController(ListViewBase list, LibraryListContext context, LibraryListMode mode)
    {
        _list = list;
        Context = context;
        Mode = mode;
        Ui.StretchItems(list);
        if (mode == LibraryListMode.Playables)
        {
            list.SelectionMode = ListViewSelectionMode.Extended;
            list.IsItemClickEnabled = false;
        }
        else
        {
            list.SelectionMode = ListViewSelectionMode.None;
            list.IsItemClickEnabled = true;
        }
        list.ItemClick += OnItemClick;
        list.DoubleTapped += OnDoubleTapped;
        list.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => _lastPointerPress = DateTime.UtcNow), true);
        list.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKeyDown), true);
    }

    public LibraryListContext Context { get; }
    public LibraryListMode Mode { get; }
    public ListViewBase List => _list;

    /// Overrides the default activation (open container / play playable).
    public Action<LibraryItem>? ItemActivated { get; set; }

    public IncrementalItems? IncrementalSource => _list.ItemsSource as IncrementalItems;

    /// Loads the entities page by page. The loader returns entities; they are wrapped in LibraryItems.
    public IncrementalItems SetIncrementalSource(Func<int, int, IEnumerable<object>> loadEntities, int totalCount, int pageSize = 100)
    {
        var items = new IncrementalItems((skip, take) =>
        {
            var index = skip;
            return loadEntities(skip, take).Select(e => (object)new LibraryItem(e, Context, index++)).ToList();
        }, totalCount, pageSize);
        _list.ItemsSource = items;
        return items;
    }

    /// Shows a fixed list of entities (wrapped in LibraryItems; SectionHeaderItems are kept).
    public List<object> SetItems(IEnumerable<object> entities)
    {
        var index = 0;
        var items = entities.Select(e => e is SectionHeaderItem ? e : new LibraryItem(e, Context, index++)).ToList();
        _list.ItemsSource = items;
        return items;
    }

    public void Clear() => _list.ItemsSource = null;

    public void ScrollTo(object item) => _list.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);

    /// Scrolls to the entity with the given index (loads pages of an incremental source).
    public void ScrollToIndex(int index)
    {
        if (IncrementalSource is { } source) source.EnsureLoaded(index);
        if (_list.ItemsSource is not System.Collections.IList items) return;
        var target = items.OfType<LibraryItem>().FirstOrDefault(i => i.Index == index);
        if (target is not null) ScrollTo(target);
    }

    public LibraryItem? FindItem(Func<object, bool> predicate) =>
        (_list.ItemsSource as System.Collections.IEnumerable)?.OfType<LibraryItem>().FirstOrDefault(i => predicate(i.Entity) || predicate(LibraryItem.Unwrap(i.Entity) ?? i.Entity));

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LibraryItem item) return;
        var isPointer = (DateTime.UtcNow - _lastPointerPress).TotalMilliseconds < 600;
        if (item.Playable is not null && item.Entity is not SearchHistoryItem { SearchedPlayableContainable: not AbstractPlayable })
        {
            // playables: mouse click only focuses, keyboard invocation (Enter/Space) plays
            if (!isPointer) Activate(item);
            return;
        }
        Activate(item);
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (Ui.FindAncestor<ButtonBase>(source) is not null) return;
        if ((source as FrameworkElement)?.DataContext is not LibraryItem item) return;
        if (item.Playable is null) return; // containers open on single click
        e.Handled = true;
        Activate(item);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || _list.IsItemClickEnabled) return;
        var item = (FocusManager.GetFocusedElement(_list.XamlRoot) as SelectorItem) is { } container
            ? _list.ItemFromContainer(container) as LibraryItem
            : _list.SelectedItem as LibraryItem;
        if (item is null) return;
        e.Handled = true;
        Activate(item);
    }

    /// Default activation: containers open their detail page, playables play.
    public void Activate(LibraryItem item)
    {
        if (ItemActivated is { } handler)
        {
            handler(item);
            return;
        }
        if (item.Entity is SearchHistoryItem history && history.SearchedPlayableContainable is { } searched)
        {
            EntityActions.RecordSearchHistory(searched);
        }
        switch (LibraryItem.Unwrap(item.Entity))
        {
            case AbstractPlayable playable:
                if (!EntityActions.IsPlayable(playable)) return;
                EntityActions.Play(Context.PlayContextProvider?.Invoke(item) ?? new PlayContext(playable));
                break;
            case { } entity:
                EntityActions.Open(entity);
                break;
        }
    }
}
