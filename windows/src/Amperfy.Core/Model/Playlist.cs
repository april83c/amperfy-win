using Amperfy.Core.Api;

namespace Amperfy.Core.Model;

public class Playlist : IPlayableContainable
{
    public const string SmartPlaylistIdPrefix = "smart_";
    public const int ArtworkItemMaxLookCount = 20;

    public int Pk { get; set; }
    public string Id { get; set; } = "";
    public string AlphabeticSectionInitial { get; set; } = "?";
    public DateTime? ChangeDate { get; set; }
    public long DurationRaw { get; set; }
    public bool IsCached { get; set; }
    public DateTime? LastPlayedDate { get; set; }
    public string? NameRaw { get; set; }
    public int PlayCount { get; set; }
    public long RemoteDurationRaw { get; set; }
    public int RemoteSongCount { get; set; }
    public int SongCountRaw { get; set; }

    public int? AccountPk { get; set; }
    public virtual Account? Account { get; set; }
    public virtual ICollection<PlaylistItem> ItemsRaw { get; set; } = new HashSet<PlaylistItem>();
    public virtual ICollection<PlaylistItem> ArtworkItemsRaw { get; set; } = new HashSet<PlaylistItem>();
    public virtual SearchHistoryItem? SearchHistory { get; set; }

    protected Playlist() { }

    // --- ordered item cache -------------------------------------------------------------------

    private List<PlaylistItem>? _sortedItems;

    /// Items in playlist order. Cached; rebuilt when the underlying collection changed in size.
    private List<PlaylistItem> Sorted
    {
        get
        {
            if (_sortedItems is null || _sortedItems.Count != ItemsRaw.Count)
                _sortedItems = ItemsRaw.OrderBy(i => i.Order).ThenBy(i => i.Pk).ToList();
            return _sortedItems;
        }
    }

    public void InvalidateItemCache() => _sortedItems = null;

    /// Keeps the denormalized song count in sync with the items (CoreData did this in willSave).
    internal void UpdateSongCount()
    {
        if (SongCountRaw != ItemsRaw.Count) SongCountRaw = ItemsRaw.Count;
    }

    public IReadOnlyList<PlaylistItem> Items => Sorted;

    public IReadOnlyList<PlaylistItem> ArtworkItems => ArtworkItemsRaw.OrderBy(i => i.Order).ToList();

    public List<AbstractPlayable> PlayablesList => Sorted.Where(i => i.Playable is not null).Select(i => i.Playable!).ToList();

    public IReadOnlyList<AbstractPlayable> Playables => PlayablesList;

    public AbstractPlayable? GetPlayable(int at)
    {
        var items = Sorted;
        return at >= 0 && at < items.Count ? items[at].Playable : null;
    }

    public List<AbstractPlayable> GetPlayables(int from, int? to = null)
    {
        var items = Sorted;
        if (items.Count == 0) return [];
        var end = to ?? items.Count - 1;
        if (from < 0 || end < 0 || from > end || end >= items.Count) return [];
        var result = new List<AbstractPlayable>(end - from + 1);
        for (var i = from; i <= end; i++)
        {
            if (items[i].Playable is { } p) result.Add(p);
        }
        return result;
    }

    /// playables that are already contained in the playlist
    public List<AbstractPlayable> Contains(IEnumerable<AbstractPlayable> playablesToCheck)
    {
        var set = PlayablesList.ToHashSet();
        return playablesToCheck.Distinct().Where(set.Contains).ToList();
    }

    /// playables that are not already part of this playlist
    public List<AbstractPlayable> NotContains(IEnumerable<AbstractPlayable> playablesToCheck)
    {
        var set = PlayablesList.ToHashSet();
        return playablesToCheck.Distinct().Where(p => !set.Contains(p)).ToList();
    }

    public int SongCount => SongCountRaw != 0 ? SongCountRaw : RemoteSongCount;

    /// Number of items actually stored locally.
    public int ItemCount => ItemsRaw.Count;

    public string Identifier => Name;

    [NotMapped]
    public string Name
    {
        get => NameRaw ?? "";
        set
        {
            if (NameRaw == value) return;
            NameRaw = value;
            UpdateAlphabeticSectionInitial(value);
            UpdateChangeDate();
        }
    }

    private void UpdateAlphabeticSectionInitial(string section)
    {
        var initial = section.SectionInitial();
        if (AlphabeticSectionInitial != initial) AlphabeticSectionInitial = initial;
    }

    [NotMapped]
    public DateTime? LastTimePlayed
    {
        get => LastPlayedDate;
        set => LastPlayedDate = value;
    }

    public bool IsSmartPlaylist => Id.StartsWith(SmartPlaylistIdPrefix, StringComparison.Ordinal);

    public int LastPlayableIndex => Math.Max(0, ItemsRaw.Count - 1);

    public int Duration => (int)DurationRaw;

    [NotMapped]
    public int RemoteDuration
    {
        get => (int)RemoteDurationRaw;
        set
        {
            RemoteDurationRaw = value;
            DurationRaw = value;
        }
    }

    private void UpdateDuration(int byReducing = 0, int byIncreasing = 0)
    {
        if (byReducing > 0 && DurationRaw >= byReducing) DurationRaw -= byReducing;
        if (byIncreasing > 0) DurationRaw += byIncreasing;
    }

    public string Info
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append("Name: ").Append(Name).Append('\n');
            sb.Append("Count: ").Append(SongCount).Append('\n');
            sb.Append("Playables:\n");
            foreach (var item in Sorted) sb.Append(item.Order).Append(": ").Append(item.Playable?.Title).Append('\n');
            return sb.ToString();
        }
    }

    // --- mutation -----------------------------------------------------------------------------

    private PlaylistItem CreateItem(AbstractPlayable playable, int order) => new()
    {
        Playable = playable,
        Playlist = this,
        Account = Account ?? playable.Account,
        Order = order,
    };

    public void UpdateArtworkItems()
    {
        var updated = new List<PlaylistItem>();
        var items = Sorted;
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Playable?.Artwork is not null)
            {
                updated.Add(items[index]);
                if (updated.Count >= 4 || index > ArtworkItemMaxLookCount) break;
            }
        }
        var current = ArtworkItemsRaw.ToList();
        if (current.Count == updated.Count && current.All(updated.Contains)) return;
        foreach (var item in current) ArtworkItemsRaw.Remove(item);
        foreach (var item in updated) ArtworkItemsRaw.Add(item);
    }

    public void Append(AbstractPlayable playable) => Append([playable]);

    public void Append(IReadOnlyList<AbstractPlayable> playablesToAppend)
    {
        if (playablesToAppend.Count == 0) return;
        var items = Sorted;
        var lastOrder = items.Count > 0 ? items[^1].Order : items.Count;
        foreach (var playable in playablesToAppend)
        {
            lastOrder += PlaylistItem.OrderDistance;
            var item = CreateItem(playable, lastOrder);
            ItemsRaw.Add(item);
            items.Add(item);
        }
        UpdateChangeDate();
        UpdateDuration(byIncreasing: playablesToAppend.Sum(p => p.Duration));
        UpdateArtworkItems();
        UpdateSongCount();
    }

    /// Adds an existing item (used by parsers)
    public void Add(PlaylistItem item)
    {
        UpdateChangeDate();
        UpdateDuration(byIncreasing: item.Playable?.Duration ?? 0);
        item.Playlist = this;
        ItemsRaw.Add(item);
        InvalidateItemCache();
        UpdateSongCount();
    }

    public void ReassignOrder()
    {
        var items = Sorted;
        for (var i = 0; i < items.Count; i++) items[i].Order = (i + 1) * PlaylistItem.OrderDistance;
    }

    private static List<int> CreateEvenlySpreadIndicesInRangeOffset(int indexCount, int range)
    {
        var spread = new List<int>(indexCount);
        if (indexCount == range)
        {
            for (var i = 1; i <= indexCount; i++) spread.Add(i);
            return spread;
        }
        var availableSplitSpace = range - indexCount;
        var minDiff = availableSplitSpace / (indexCount + 1);
        if (minDiff > 0)
        {
            for (var i = 1; i <= indexCount; i++) spread.Add(i * (minDiff + 1));
        }
        else
        {
            var spaceBeforeAndAfter = (range - indexCount) / 2;
            for (var i = 1; i <= indexCount; i++) spread.Add(i + spaceBeforeAndAfter);
        }
        return spread;
    }

    /// Orders for inserting `count` items at `at` given the ordered item list (without the moved item). null -> no space left.
    private static List<int>? GetOrdersToInsert(List<PlaylistItem> items, int at, int count)
    {
        if (at == 0)
        {
            if (items.Count == 0) return Enumerable.Range(1, count).Select(i => i * PlaylistItem.OrderDistance).ToList();
            if (items[0].Order < count + 1) return null;
            return CreateEvenlySpreadIndicesInRangeOffset(count, items[0].Order).Select(o => o - 1).ToList();
        }
        if (at == items.Count)
        {
            var lastOrder = items[^1].Order;
            return Enumerable.Range(1, count).Select(i => lastOrder + i * PlaylistItem.OrderDistance).ToList();
        }
        var before = items[at - 1];
        var target = items[at];
        var space = target.Order - before.Order - 1;
        if (space < count) return null;
        return CreateEvenlySpreadIndicesInRangeOffset(count, space).Select(o => before.Order + o).ToList();
    }

    public void Insert(IReadOnlyList<AbstractPlayable> playablesToInsert, int insertIndex = 0)
    {
        var items = Sorted;
        if (insertIndex < 0 || insertIndex > items.Count || playablesToInsert.Count == 0) return;
        var orders = GetOrdersToInsert(items, insertIndex, playablesToInsert.Count);
        for (var i = 0; i < playablesToInsert.Count; i++)
        {
            var item = CreateItem(playablesToInsert[i], orders?[i] ?? 0);
            ItemsRaw.Add(item);
            items.Insert(insertIndex + i, item);
        }
        if (orders is null) ReassignOrder();
        UpdateChangeDate();
        UpdateDuration(byIncreasing: playablesToInsert.Sum(p => p.Duration));
        if (insertIndex < ArtworkItemMaxLookCount) UpdateArtworkItems();
        UpdateSongCount();
    }

    public void MovePlaylistItem(int fromIndex, int to)
    {
        var items = Sorted;
        if (fromIndex < 0 || fromIndex >= items.Count || to < 0 || to >= items.Count || fromIndex == to) return;
        var item = items[fromIndex];
        items.RemoveAt(fromIndex);
        var orders = GetOrdersToInsert(items, to, 1);
        items.Insert(to, item);
        if (orders is { Count: 1 }) item.Order = orders[0];
        else ReassignOrder();
        UpdateChangeDate();
        if (fromIndex < ArtworkItemMaxLookCount || to < ArtworkItemMaxLookCount) UpdateArtworkItems();
    }

    public void Remove(int at)
    {
        var items = Sorted;
        if (at < 0 || at >= items.Count) return;
        var item = items[at];
        items.RemoveAt(at);
        ItemsRaw.Remove(item);
        ArtworkItemsRaw.Remove(item);
        UpdateChangeDate();
        UpdateDuration(byReducing: item.Playable?.Duration ?? 0);
        if (at < ArtworkItemMaxLookCount) UpdateArtworkItems();
        UpdateSongCount();
    }

    public void RemoveFirstOccurrence(AbstractPlayable playable)
    {
        var idx = GetFirstIndex(playable);
        if (idx is { } i) Remove(i);
    }

    public int? GetFirstIndex(PlaylistItem item)
    {
        var idx = Sorted.IndexOf(item);
        return idx >= 0 ? idx : null;
    }

    public int? GetFirstIndex(AbstractPlayable playable)
    {
        var items = Sorted;
        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i].Playable, playable)) return i;
        }
        return null;
    }

    public void RemoveAllItems()
    {
        ArtworkItemsRaw.Clear();
        ItemsRaw.Clear();
        _sortedItems = [];
        UpdateChangeDate();
        DurationRaw = 0;
        RemoteDurationRaw = 0;
        UpdateSongCount();
    }

    public void Shuffle()
    {
        var items = Sorted;
        if (items.Count == 0) return;
        var playables = items.Select(i => i.Playable).OfType<AbstractPlayable>().ToList();
        RemoveAllItems();
        Append(playables.Shuffled());
    }

    public void UpdateChangeDate() => ChangeDate = DateTime.UtcNow;

    public ArtworkType DefaultArtworkType => ArtworkType.Playlist;

    // IPlayableContainable
    public string? Subtitle => null;
    public string? Subsubtitle => null;

    public List<string> InfoDetails(ServerApiType? api, DetailInfoType details)
    {
        var info = new List<string> { SongCount == 1 ? "1 Song" : $"{SongCount} Songs" };
        if (IsSmartPlaylist) info.Add("Smart Playlist");
        if (details.Type == DetailType.Short && Duration > 0) info.Add(Duration.AsDurationShortString());
        if (details.Type == DetailType.Long)
        {
            if (IsCached) info.Add("Cached");
            if (Duration > 0) info.Add(Duration.AsDurationShortString());
            if (details.IsShowDetailedInfo) info.Add(PlayableContainableExtensions.IdInfo(Id));
        }
        return info;
    }

    public PlayerMode PlayContextType => PlayerMode.Music;
    public bool IsRateable => false;
    public bool IsFavoritable => false;
    public bool IsFavorite => false;
    public bool IsDownloadAvailable => true;

    public Task FetchFromServerAsync(ILibrarySyncer librarySyncer) => librarySyncer.SyncDownAsync(this);

    public Task RemoteToggleFavoriteAsync(LibraryStorage library, ILibrarySyncer syncer) => throw BackendError.NotSupported;

    public ArtworkCollection GetArtworkCollection()
    {
        var artworkItems = ArtworkItems.Where(i => i.Playable is not null).ToList();
        if (artworkItems.Count == 0) return new ArtworkCollection(DefaultArtworkType, null);
        if (artworkItems.Count == 1) return new ArtworkCollection(DefaultArtworkType, artworkItems[0].Playable);
        var quad = artworkItems.Select(i => (AbstractLibraryEntity)i.Playable!).Take(4).ToList();
        return new ArtworkCollection(DefaultArtworkType, artworkItems[0].Playable, quad);
    }

    public void PlayedViaContext()
    {
        LastTimePlayed = DateTime.UtcNow;
        PlayCount += 1;
    }

    public PlayableContainerIdentifier ContainerIdentifier => new(PlayableContainerBaseType.Playlist, Pk.ToString(CultureInfo.InvariantCulture));

    public override string ToString() => $"Playlist({Name})";
}

public static class PlaylistListExtensions
{
    public static List<Playlist> FilterRegularPlaylists(this IEnumerable<Playlist> list) => list.Where(p => !p.IsSmartPlaylist).ToList();
    public static List<Playlist> FilterSmartPlaylists(this IEnumerable<Playlist> list) => list.Where(p => p.IsSmartPlaylist).ToList();
}
