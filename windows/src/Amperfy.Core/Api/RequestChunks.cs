namespace Amperfy.Core.Api;

/// Splits the list parameters of playlist updates over several requests so the URLs stay short:
/// servers and reverse proxies reject long URLs (nginx/openresty: "414 Request-URI Too Large"
/// at about 8 KB), which a playlist with a thousand songs easily exceeds.
public static class RequestChunks
{
    /// Budget for the list parameters of one request; the base URL with the credentials comes on top.
    public const int MaxParameterLength = 4000;

    /// Splits the items in order into chunks whose summed parameter length stays within the budget
    /// (an item longer than the budget gets a chunk of its own).
    public static List<List<T>> Split<T>(IEnumerable<T> items, Func<T, int> parameterLength, int maxLength = MaxParameterLength)
    {
        var chunks = new List<List<T>>();
        var current = new List<T>();
        var length = 0;
        foreach (var item in items)
        {
            var itemLength = parameterLength(item);
            if (current.Count > 0 && length + itemLength > maxLength)
            {
                chunks.Add(current);
                current = [];
                length = 0;
            }
            current.Add(item);
            length += itemLength;
        }
        if (current.Count > 0) chunks.Add(current);
        return chunks;
    }
}

/// Changes that turn the server's playlist into the new order with the Subsonic updatePlaylist
/// call, which can only remove indexes and append songs: the unchanged start is kept, the rest is
/// appended in the new order and the old rest removed afterwards.
public sealed record PlaylistOrderUpload(int KeptCount, IReadOnlyList<string> SongIdsToAppend, IReadOnlyList<int> IndexesToRemove)
{
    public bool IsEmpty => SongIdsToAppend.Count == 0 && IndexesToRemove.Count == 0;

    public static PlaylistOrderUpload Create(IReadOnlyList<string> serverSongIds, IReadOnlyList<string> newSongIds)
    {
        var kept = 0;
        while (kept < serverSongIds.Count && kept < newSongIds.Count && serverSongIds[kept] == newSongIds[kept]) kept++;
        return new PlaylistOrderUpload(kept,
            newSongIds.Skip(kept).ToList(),
            Enumerable.Range(kept, serverSongIds.Count - kept).ToList());
    }
}
