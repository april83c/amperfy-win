using Amperfy.Core.Model;
using Amperfy.Core.Player;

namespace Amperfy.App.Library;

/// Shared configuration of the items of one list (how rows are displayed and played).
public sealed class LibraryListContext
{
    /// Page hosting the list (e.g. typeof(AlbumDetailPage)): hides "Show Album" there etc.
    public Type? HostPageType { get; set; }

    /// Play context for a playable item (double click / Enter / "Play"); null: the playable alone.
    public Func<LibraryItem, PlayContext?>? PlayContextProvider { get; set; }

    /// Album style: track numbers instead of artworks.
    public bool IsTrackNumberStyle { get; set; }

    /// Songs show "Artist · Album" as subtitle.
    public bool ShowAlbumInSubtitle { get; set; }

    /// Rows show artworks (genres don't).
    public bool ShowArtwork { get; set; } = true;

    /// Podcast episodes are shown with the large episode row (description, progress).
    public bool IsEpisodeStyle { get; set; } = true;

    /// Playlist edit mode (no context menu actions, reorder handles).
    public bool IsEditMode { get; set; }

    /// Width of grid tiles.
    public double TileWidth { get; set; } = 170;

    /// Called after an action changed an entity (favorite, cache, ...).
    public Action? Changed { get; set; }

    /// Additional context menu items for an item (e.g. "Remove from Playlist").
    public Func<LibraryItem, IEnumerable<Microsoft.UI.Xaml.Controls.MenuFlyoutItemBase>>? ExtraMenuItems { get; set; }

    /// Raised when display settings (e.g. tile size) changed; visible rows refresh.
    public event Action? LayoutChanged;

    public void NotifyLayoutChanged() => LayoutChanged?.Invoke();
}

/// An element of a library list: an entity (Song, Album, Artist, Playlist, Download, PlaylistItem,
/// SearchHistoryItem, MusicFolder, ...) together with its list context and position.
public sealed class LibraryItem
{
    public LibraryItem(object entity, LibraryListContext context, int index = 0)
    {
        Entity = entity;
        Context = context;
        Index = index;
    }

    public object Entity { get; }
    public LibraryListContext Context { get; }

    /// Index of the entity in the (unfiltered by headers) list: used for play contexts.
    public int Index { get; set; }

    /// The playable of the item, if any (unwraps downloads, playlist items and search history).
    public AbstractPlayable? Playable => Unwrap(Entity) as AbstractPlayable;

    /// The playable container of the item (unwraps downloads, playlist items and search history).
    public IPlayableContainable? Container => Unwrap(Entity) as IPlayableContainable;

    public static object? Unwrap(object? entity) => entity switch
    {
        Download d => d.Playable,
        PlaylistItem p => p.Playable,
        SearchHistoryItem h => h.SearchedPlayableContainable,
        _ => entity,
    };

    public override string ToString() => Unwrap(Entity) switch
    {
        AbstractPlayable p => p.Title,
        IPlayableContainable c => c.Name,
        MusicFolder f => f.Name,
        _ => "",
    };
}

/// Section header row inside a flat list (search results, album discs).
public sealed class SectionHeaderItem
{
    public SectionHeaderItem(string title, string? actionText = null, Action? action = null)
    {
        Title = title;
        ActionText = actionText;
        Action = action;
    }

    public string Title { get; }
    public string? ActionText { get; }
    public Action? Action { get; }

    public override string ToString() => Title;
}

/// Navigation parameter for detail pages that should scroll to an element (e.g. album + song).
public sealed record EntityNavigationArgs(object Entity, object? ScrollTo);

public static class NavigationArgs
{
    public static (T? Entity, object? ScrollTo) Unpack<T>(object? parameter) where T : class => parameter switch
    {
        EntityNavigationArgs args => (args.Entity as T, args.ScrollTo),
        T entity => (entity, null),
        _ => (null, null),
    };
}
