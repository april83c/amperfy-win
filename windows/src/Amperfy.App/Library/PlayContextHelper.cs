using Amperfy.Core.Model;
using Amperfy.Core.Player;

namespace Amperfy.App.Library;

public static class PlayContextHelper
{
    /// Play context for the element at index of a (possibly big) sorted query: the whole list if it
    /// is small enough, otherwise the window starting at the element (max. MaxSongsToAddOnce).
    /// Unplayable elements (offline, not available) are skipped.
    public static PlayContext FromQuery<T>(IQueryable<T> query, int index, IPlayableContainable? container, string name)
        where T : AbstractPlayable
    {
        var max = CategoryPageHelper.MaxSongsToAddOnce;
        var total = query.Count();
        List<T> window;
        int start;
        if (total <= max)
        {
            window = query.ToList();
            start = 0;
        }
        else
        {
            window = query.Skip(index).Take(max).ToList();
            start = index;
        }
        var selected = index - start >= 0 && index - start < window.Count ? window[index - start] : null;
        var playables = window.Where(EntityActions.IsPlayable).Cast<AbstractPlayable>().ToList();
        var playIndex = selected is null ? 0 : Math.Max(0, playables.IndexOf(selected));
        return container is not null
            ? new PlayContext(container, playIndex, playables)
            : new PlayContext(name, playIndex, playables);
    }
}
