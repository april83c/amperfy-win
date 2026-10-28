using System.Globalization;
using Amperfy.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Amperfy.App.Library;

/// "Jump to" search in a list (like Ctrl+F in a browser): finds the items whose title, artist or
/// album contains the text and scrolls to them one after the other, without filtering the list.
/// Lists with selection select the current match, so actions (play, context menu, remove) apply to it.
public sealed class ListFinder
{
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private readonly LibraryListController _controller;
    private List<LibraryItem> _matches = [];
    private string _text = "";

    private System.Collections.Specialized.INotifyCollectionChanged? _observed;
    private bool _isRefreshQueued;

    public ListFinder(LibraryListController controller)
    {
        _controller = controller;
        // list replaced or items changed (reload, removed item): update the matches without jumping
        controller.List.RegisterPropertyChangedCallback(ItemsControl.ItemsSourceProperty, (_, _) => ObserveItems());
        ObserveItems();
    }

    private void ObserveItems()
    {
        if (_observed is not null) _observed.CollectionChanged -= OnCollectionChanged;
        _observed = _controller.List.ItemsSource as System.Collections.Specialized.INotifyCollectionChanged;
        if (_observed is not null) _observed.CollectionChanged += OnCollectionChanged;
        QueueQuietRefresh();
    }

    private void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => QueueQuietRefresh();

    private void QueueQuietRefresh()
    {
        if (_text.Length == 0 || _isRefreshQueued) return;
        _isRefreshQueued = true;
        _controller.List.DispatcherQueue.TryEnqueue(() =>
        {
            _isRefreshQueued = false;
            RefreshQuietly();
        });
    }

    /// Recomputes the matches of the current text without selecting or scrolling.
    private void RefreshQuietly()
    {
        if (_text.Length == 0) return;
        var previousIndex = CurrentIndex;
        var previousEntity = Current?.Entity;
        _matches = Items().Where(i => Matches(i.Entity, _text)).ToList();
        var kept = previousEntity is null ? -1 : _matches.FindIndex(i => ReferenceEquals(i.Entity, previousEntity));
        // the current match was removed: the next one becomes current (shown on the next F3 / Enter)
        CurrentIndex = kept >= 0 ? kept : _matches.Count == 0 ? -1 : Math.Min(Math.Max(previousIndex, 0), _matches.Count - 1) - 1;
        Changed?.Invoke();
    }

    public int MatchCount => _matches.Count;

    /// Position (0-based) of the current match in the matches, -1 if none.
    public int CurrentIndex { get; private set; } = -1;

    public LibraryItem? Current => CurrentIndex >= 0 && CurrentIndex < _matches.Count ? _matches[CurrentIndex] : null;

    /// Raised when the matches or the current match changed.
    public event Action? Changed;

    /// Text displayed for an entity in the lists (title, artist, album / name, subtitle).
    public static string ItemText(object entity) => LibraryItem.Unwrap(entity) switch
    {
        IPlayableContainable c => $"{c.Name}\n{c.Subtitle}\n{c.Subsubtitle}",
        MusicFolder f => f.Name,
        null => "",
        var other => other.ToString() ?? "",
    };

    public static bool Matches(object entity, string text) =>
        text.Length > 0 && Compare.IndexOf(ItemText(entity), text, Options) >= 0;

    /// Searches the list for the text. Keeps the current match if it still matches (typing more
    /// letters), otherwise jumps to the first match.
    public void Search(string text)
    {
        text = text.Trim();
        var previous = Current;
        _text = text;
        if (text.Length == 0)
        {
            _matches = [];
            CurrentIndex = -1;
            Changed?.Invoke();
            return;
        }
        // incremental lists: load everything so matches further down are found too
        if (_controller.IncrementalSource is { } source && source.HasMoreItems) source.EnsureLoaded(source.TotalCount - 1);
        _matches = Items().Where(i => Matches(i.Entity, text)).ToList();
        var kept = previous is null ? -1 : _matches.FindIndex(i => ReferenceEquals(i.Entity, previous.Entity));
        CurrentIndex = kept >= 0 ? kept : (_matches.Count > 0 ? 0 : -1);
        Show();
    }

    public void Next() => Step(1);

    public void Previous() => Step(-1);

    private void Step(int direction)
    {
        if (_matches.Count == 0) return;
        CurrentIndex = (CurrentIndex + direction + _matches.Count) % _matches.Count;
        Show();
    }

    /// Moves the keyboard focus to the current match (to play it with Enter or open its menu).
    public bool FocusCurrent()
    {
        if (Current is not { } item) return false;
        if (_controller.List.ContainerFromItem(item) is Control container) return container.Focus(FocusState.Keyboard);
        return false;
    }

    private IEnumerable<LibraryItem> Items() =>
        (_controller.List.ItemsSource as System.Collections.IEnumerable)?.OfType<LibraryItem>() ?? [];

    private void Show()
    {
        if (Current is { } item)
        {
            var list = _controller.List;
            if (list.SelectionMode is ListViewSelectionMode.Single or ListViewSelectionMode.Extended)
            {
                list.SelectedItems.Clear();
                list.SelectedItem = item;
            }
            if (!IsFullyVisible(item)) list.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
        }
        Changed?.Invoke();
    }

    private bool IsFullyVisible(LibraryItem item)
    {
        var list = _controller.List;
        if (list.ContainerFromItem(item) is not FrameworkElement container || container.ActualHeight <= 0) return false;
        try
        {
            var bounds = container.TransformToVisual(list).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
            return bounds.Top >= 0 && bounds.Bottom <= list.ActualHeight;
        }
        catch
        {
            return false;
        }
    }
}
