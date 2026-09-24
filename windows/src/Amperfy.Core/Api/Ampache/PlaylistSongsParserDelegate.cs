namespace Amperfy.Core.Api.Ampache;

/// Parses the songs of a playlist and updates the playlist items in place.
public sealed class PlaylistSongsParserDelegate : SongParserDelegate
{
    public Playlist Playlist { get; }
    private readonly List<PlaylistItem> _items;
    private bool _playlistChanged;

    public PlaylistSongsParserDelegate(Playlist playlist, PrefetchElementContainer prefetch, Account account, LibraryStorage library)
        : base(prefetch, account, library, parseNotifier: null)
    {
        Playlist = playlist;
        _items = [.. playlist.Items];
    }

    protected override void DidEndElement(string elementName)
    {
        switch (elementName)
        {
            case "playlisttrack":
                {
                    var index = ParsedCount;
                    if (SongBuffer is { } song)
                    {
                        if (index < _items.Count)
                        {
                            var item = _items[index];
                            if (item.Playable?.Id != song.Id)
                            {
                                _playlistChanged = true;
                                item.Playable = song;
                            }
                        }
                        else
                        {
                            CreateAndAppendPlaylistItem(song);
                            _playlistChanged = true;
                        }
                    }
                    break;
                }
            case "root":
                if (_items.Count > ParsedCount)
                {
                    for (var i = ParsedCount; i < _items.Count; i++)
                    {
                        var item = _items[i];
                        Playlist.ItemsRaw.Remove(item);
                        Playlist.ArtworkItemsRaw.Remove(item);
                        Library.DeletePlaylistItem(item);
                    }
                    Playlist.InvalidateItemCache();
                    _playlistChanged = true;
                }
                if (_playlistChanged)
                {
                    Playlist.UpdateChangeDate();
                    Playlist.UpdateArtworkItems();
                    Playlist.RemoteDuration = CollectionDuration;
                }
                Playlist.IsCached = IsCollectionCached;
                break;
        }
        base.DidEndElement(elementName);
    }

    /// Swift Playlist.createAndAppendPlaylistItem(for:)
    private void CreateAndAppendPlaylistItem(Song song)
    {
        var items = Playlist.Items;
        var lastOrder = items.Count > 0 ? items[^1].Order : Playlist.ItemsRaw.Count;
        var item = new PlaylistItem
        {
            Playable = song,
            Account = Playlist.Account ?? song.Account,
            Order = lastOrder + PlaylistItem.OrderDistance,
        };
        Playlist.Add(item);
    }
}
