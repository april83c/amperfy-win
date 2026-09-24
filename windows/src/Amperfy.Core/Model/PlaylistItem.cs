namespace Amperfy.Core.Model;

public class PlaylistItem
{
    public const int OrderDistance = 32;

    public int Pk { get; set; }
    public int Order { get; set; }

    public int? AccountPk { get; set; }
    public virtual Account? Account { get; set; }
    public int? PlayablePk { get; set; }
    public virtual AbstractPlayable? Playable { get; set; }
    public int? PlaylistPk { get; set; }
    public virtual Playlist? Playlist { get; set; }
    public int? PlaylistArtworkItemPk { get; set; }
    public virtual Playlist? PlaylistArtworkItem { get; set; }

    public PlaylistItem() { }

    public override string ToString() => $"PlaylistItem({Order}: {Playable?.Title})";
}
