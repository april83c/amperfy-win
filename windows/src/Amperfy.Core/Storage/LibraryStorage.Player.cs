using Microsoft.EntityFrameworkCore;
using Amperfy.Core.Player;

namespace Amperfy.Core.Storage;

public sealed partial class LibraryStorage
{
    /// Port of LibraryStorage.getPlayerData() (see <see cref="PlayerData.Create"/>).
    public PlayerData GetPlayerData() => PlayerData.Create(this);

    /// Port of LibraryStorage.createPlaylistItem(playable:). The item still has to be added to a playlist
    /// (<see cref="Playlist.Add(PlaylistItem)"/>) before the context is saved.
    public PlaylistItem CreatePlaylistItem(AbstractPlayable playable)
    {
        var item = Context.CreateProxy<PlaylistItem>(); item.Playable = playable; item.Account = playable.Account;
        Context.Add(item);
        return item;
    }
}
