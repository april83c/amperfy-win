using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Amperfy.Core.Common;
using Microsoft.UI.Xaml.Data;
using Windows.Foundation;

namespace Amperfy.App.Library;

/// Items of a big list loaded page by page while scrolling (ListView/GridView incremental loading).
/// The page loader runs on the UI thread (EF storage is main thread only); pages are small so a
/// page query takes a few milliseconds.
public sealed class IncrementalItems : ObservableCollection<object>, ISupportIncrementalLoading
{
    private readonly Func<int, int, IReadOnlyList<object>> _loadPage;
    private bool _isExhausted;
    private int _loadedEntityCount;

    /// <param name="loadPage">(skip, take) -> items of the page</param>
    /// <param name="totalCount">number of items the query returns</param>
    public IncrementalItems(Func<int, int, IReadOnlyList<object>> loadPage, int totalCount, int pageSize = 100)
    {
        _loadPage = loadPage;
        TotalCount = totalCount;
        PageSize = pageSize;
    }

    public int TotalCount { get; private set; }
    public int PageSize { get; }

    /// Number of loaded entities (the collection may contain additional header items).
    public int LoadedEntityCount => _loadedEntityCount;

    public bool HasMoreItems => !_isExhausted && _loadedEntityCount < TotalCount;

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
    {
        var added = 0;
        try
        {
            added = LoadMore(Math.Max((int)count, PageSize));
        }
        catch (Exception ex)
        {
            _isExhausted = true;
            AmperfyLog.Error("IncrementalItems", $"Loading page failed: {ex.Message}");
        }
        return Task.FromResult(new LoadMoreItemsResult { Count = (uint)added }).AsAsyncOperation();
    }

    /// Loads the next page(s) synchronously. Returns the number of added items. Big loads (jump to
    /// a far position) raise a single reset notification instead of one per item.
    public int LoadMore(int count)
    {
        if (!HasMoreItems) return 0;
        var take = Math.Min(count, TotalCount - _loadedEntityCount);
        var page = _loadPage(_loadedEntityCount, take);
        if (page.Count < take) _isExhausted = true;
        if (page.Count > 500)
        {
            foreach (var item in page) Items.Add(item);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
        else
        {
            foreach (var item in page) Add(item);
        }
        _loadedEntityCount += page.Count;
        return page.Count;
    }

    /// The query returns more items now (e.g. after fetching more from the server): loads them.
    public void ExtendTotal(int newTotalCount)
    {
        if (newTotalCount <= TotalCount) return;
        TotalCount = newTotalCount;
        _isExhausted = false;
        LoadMore(newTotalCount - _loadedEntityCount);
    }

    /// Loads pages until the entity with the given index is part of the collection.
    public void EnsureLoaded(int index)
    {
        while (_loadedEntityCount <= index && HasMoreItems)
        {
            if (LoadMore(Math.Max(PageSize, index - _loadedEntityCount + 1)) == 0) break;
        }
    }
}
