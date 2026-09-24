using Amperfy.Core.Downloads;

namespace Amperfy.Core.Model;

public class Download
{
    public virtual int Pk { get; set; }
    public virtual DateTime? CreationDate { get; set; } = DateTime.UtcNow;
    public virtual DateTime? ErrorDate { get; set; }
    public virtual int ErrorTypeRaw { get; set; }
    public virtual DateTime? FinishDate { get; set; }
    public virtual string Id { get; set; } = "";
    public virtual float Progress { get; set; }
    public virtual DateTime? StartDate { get; set; }
    public virtual string? TotalSizeRaw { get; set; }
    public virtual string UrlString { get; set; } = "";

    public virtual int? AccountPk { get; set; }
    public virtual Account? Account { get; set; }
    public virtual int? ArtworkPk { get; set; }
    public virtual Artwork? Artwork { get; set; }
    public virtual int? PlayablePk { get; set; }
    public virtual AbstractPlayable? Playable { get; set; }

    public Download() { }

    public string Title => Element?.DisplayString ?? "";

    public bool IsFinishedSuccessfully => FinishDate is not null && ErrorDate is null;

    public bool IsCanceled => Error == DownloadError.Canceled;

    public void Reset()
    {
        StartDate = null;
        FinishDate = null;
        Error = null;
        Progress = 0;
        TotalSize = "";
    }

    [NotMapped]
    public bool IsDownloading
    {
        get => StartDate is not null && FinishDate is null && ErrorDate is null;
        set
        {
            if (value) StartDate = DateTime.UtcNow;
            else FinishDate = DateTime.UtcNow;
        }
    }

    public void Suspend()
    {
        if (StartDate is not null) StartDate = null;
    }

    [NotMapped]
    public DownloadError? Error
    {
        get => ErrorDate is null ? null : Enum.IsDefined(typeof(DownloadError), ErrorTypeRaw) ? (DownloadError)ErrorTypeRaw : null;
        set
        {
            if (value is { } e)
            {
                ErrorDate = DateTime.UtcNow;
                ErrorTypeRaw = (int)e;
            }
            else
            {
                ErrorDate = null;
                ErrorTypeRaw = 0;
            }
        }
    }

    [NotMapped]
    public string TotalSize
    {
        get => TotalSizeRaw ?? "";
        set => TotalSizeRaw = value;
    }

    public DownloadableType BaseType => Artwork is not null ? DownloadableType.Artwork : Playable is not null ? DownloadableType.Playable : DownloadableType.Unknown;

    [NotMapped]
    public IDownloadable? Element
    {
        get => (IDownloadable?)Artwork ?? Playable;
        set
        {
            switch (value)
            {
                case AbstractPlayable p: Playable = p; break;
                case Artwork a: Artwork = a; break;
            }
        }
    }
}
