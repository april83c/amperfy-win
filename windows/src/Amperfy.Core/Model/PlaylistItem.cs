namespace Amperfy.Core.Model;

public class PlaylistItem
{
    public const int OrderDistance = 32;

    public virtual int Pk { get; set; }
    public virtual int Order { get; set; }

    public virtual int? AccountPk { get; set; }
    public virtual Account? Account { get; set; }
    public virtual int? PlayablePk { get; set; }
    public virtual AbstractPlayable? Playable { get; set; }
    public virtual int? PlaylistPk { get; set; }
    public virtual Playlist? Playlist { get; set; }
    public virtual int? PlaylistArtworkItemPk { get; set; }
    public virtual Playlist? PlaylistArtworkItem { get; set; }

    public PlaylistItem() { }

    public override string ToString() => $"PlaylistItem({Order}: {Playable?.Title})";
}
