namespace Amperfy.Core.Model;

/// Base of all library entities (TPH table "LibraryEntities").
/// Instances must be created through LibraryStorage (lazy loading proxies).
public abstract class AbstractLibraryEntity
{
    public int Pk { get; set; }

    /// Remote (server) id
    public string Id { get; set; } = "";
    public string AlphabeticSectionInitial { get; set; } = "?";
    public bool IsFavorite { get; set; }
    public DateTime? LastPlayedDate { get; set; }
    public int PlayCount { get; set; }

    private int _rating;
    public int Rating
    {
        get => _rating;
        set { if (value is >= 0 and <= 5) _rating = value; }
    }

    public RemoteStatus RemoteStatus { get; set; } = RemoteStatus.Available;
    public DateTime? StarredDate { get; set; }

    public int? AccountPk { get; set; }
    public virtual Account? Account { get; set; }

    public int? ArtworkPk { get; set; }
    public virtual Artwork? Artwork { get; set; }

    public virtual SearchHistoryItem? SearchHistory { get; set; }

    protected AbstractLibraryEntity() { }

    [NotMapped]
    public DateTime? LastTimePlayed
    {
        get => LastPlayedDate;
        set => LastPlayedDate = value;
    }

    protected void UpdateAlphabeticSectionInitial(string section)
    {
        var initial = section.SectionInitial();
        if (AlphabeticSectionInitial != initial) AlphabeticSectionInitial = initial;
    }

    public virtual string? ImagePath(ArtworkDisplayPreference setting) => Artwork?.ImagePath;

    public virtual ArtworkType DefaultArtworkType => ArtworkType.Song;

    public virtual void PlayedViaContext()
    {
        LastTimePlayed = DateTime.UtcNow;
        PlayCount += 1;
    }

    public override string ToString() => $"{GetType().Name.Replace("Proxy", "")}({Id})";
}
