namespace Amperfy.Core.Model;

/// Artwork extracted from the ID3 tag of a cached file.
public class EmbeddedArtwork
{
    public int Pk { get; set; }
    public string? RelFilePath { get; set; }

    public int? AccountPk { get; set; }
    public virtual Account? Account { get; set; }
    public int? OwnerPk { get; set; }
    public virtual AbstractPlayable? Owner { get; set; }

    public EmbeddedArtwork() { }

    public string? ImagePath => RelFilePath is null ? null : CacheFileManager.Shared.GetAbsoluteAmperfyPath(RelFilePath);
}
