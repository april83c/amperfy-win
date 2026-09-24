using Microsoft.EntityFrameworkCore;
namespace Amperfy.Core.Api.Subsonic;

/// Parses a playlist with its songs ("getPlaylist" / "createPlaylist") and updates the local
/// playlist items in place.
public class SsPlaylistSongsParserDelegate : SsSongParserDelegate
{
    private readonly Playlist _playlist;
    private bool _playlistChanged;
    private readonly List<PlaylistItem> _items;
    public bool PlaylistHasBeenDetected { get; private set; }

    public SsPlaylistSongsParserDelegate(Playlist playlist, Account account, LibraryStorage library, PrefetchElementContainer prefetch)
        : base(prefetch, account, library)
    {
        _playlist = playlist;
        _items = [.. playlist.Items];
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);

        if (elementName == "playlist")
        {
            if (StrAttr(attributes, "id") is not { } playlistId) return;
            PlaylistHasBeenDetected = true;
            if (_playlist.Id != playlistId) _playlist.Id = playlistId;
            if (StrAttr(attributes, "name") is { } attributePlaylistName) _playlist.Name = attributePlaylistName;
            if (SwiftInt(StrAttr(attributes, "songCount")) is { } songCount) _playlist.RemoteSongCount = songCount;
            if (SwiftInt(StrAttr(attributes, "duration")) is { } duration) _playlist.RemoteDuration = duration;
        }

        if (elementName == "entry")
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
        }
    }

    /// Port of Playlist.createAndAppendPlaylistItem (no duration/change date update).
    private void CreateAndAppendPlaylistItem(AbstractPlayable playable)
    {
        var existing = _playlist.Items;
        var lastOrder = existing.Count > 0 ? existing[^1].Order : existing.Count;
        var item = Library.Context.CreateProxy<PlaylistItem>();
        item.Playable = playable;
        item.Playlist = _playlist;
        item.Account = _playlist.Account ?? playable.Account;
        item.Order = lastOrder + PlaylistItem.OrderDistance;
        Library.Context.Add(item);
        _playlist.ItemsRaw.Add(item);
        _playlist.InvalidateItemCache();
        _playlist.UpdateSongCount();
    }

    protected override void DidEndElement(string elementName)
    {
        if (elementName == "playlist")
        {
            if (_items.Count > ParsedCount)
            {
                for (var i = ParsedCount; i < _items.Count; i++)
                {
                    var item = _items[i];
                    _playlist.ArtworkItemsRaw.Remove(item);
                    _playlist.ItemsRaw.Remove(item);
                    Library.DeletePlaylistItem(item);
                }
                _playlist.InvalidateItemCache();
                _playlist.UpdateSongCount();
                _playlistChanged = true;
            }
            if (_playlistChanged)
            {
                _playlist.UpdateChangeDate();
                _playlist.UpdateArtworkItems();
            }
            _playlist.IsCached = IsCollectionCached;
        }
        base.DidEndElement(elementName);
    }
}
