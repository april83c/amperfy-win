using Microsoft.EntityFrameworkCore.ChangeTracking;
using Amperfy.Core.Api;
using Amperfy.Core.Downloads;

namespace Amperfy.Core.Model;

public class Artwork : IDownloadable
{
    public virtual int Pk { get; set; }
    public virtual string Id { get; set; } = "";
    public virtual string? RelFilePath { get; set; }
    public virtual ImageStatus Status { get; set; } = ImageStatus.IsDefaultImage;
    public virtual string Type { get; set; } = "";
    public virtual string? Url { get; set; }

    public virtual int? AccountPk { get; set; }
    public virtual Account? Account { get; set; }
    public virtual Download? Download { get; set; }
    public virtual ICollection<AbstractLibraryEntity> Owners { get; set; } = new ObservableHashSet<AbstractLibraryEntity>();

    protected Artwork() { }

    public void MarkErrorIfNeeded()
    {
        if (Status != ImageStatus.CustomImage) Status = ImageStatus.FetchError;
    }

    public string? ImagePath =>
        Status == ImageStatus.CustomImage && RelFilePath is not null ? CacheFileManager.Shared.GetAbsoluteAmperfyPath(RelFilePath) : null;

    [NotMapped]
    public ArtworkRemoteInfo RemoteInfo
    {
        get => new(Id, Type);
        set
        {
            Id = value.Id;
            Type = value.Type;
        }
    }

    public bool IsCached => RelFilePath is not null;

    public string DisplayString => $"Artwork account: {Account?.ShortLogIdent ?? "-"}, id: {Id}, type: {Type}";

    public DownloadableType DownloadableType => DownloadableType.Artwork;
}
