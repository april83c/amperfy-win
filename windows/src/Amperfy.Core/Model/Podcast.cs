using Amperfy.Core.Api;

namespace Amperfy.Core.Model;

public class Podcast : AbstractLibraryEntity, IPlayableContainable
{
    public string Depiction { get; set; } = "";
    public int EpisodeCountRaw { get; set; }
    public bool IsCached { get; set; }
    public string TitleRaw { get; set; } = "";

    public virtual ICollection<PodcastEpisode> EpisodesRaw { get; set; } = new HashSet<PodcastEpisode>();

    /// used by parsers as a temporary buffer
    [NotMapped] public string TitleRawParsed { get; set; } = "";
    [NotMapped] public string DepictionRawParsed { get; set; } = "";

    protected Podcast() { }

    public string Identifier => Title;

    [NotMapped]
    public string Title
    {
        get => string.IsNullOrEmpty(TitleRaw) ? "Unknown Podcast" : TitleRaw;
        set
        {
            if (TitleRaw == value) return;
            TitleRaw = value;
            UpdateAlphabeticSectionInitial(value);
        }
    }

    public int EpisodeCount => EpisodeCountRaw;

    /// Episodes sorted by publish date (newest first)
    public List<PodcastEpisode> Episodes => EpisodesRaw.SortByPublishDate();

    public override ArtworkType DefaultArtworkType => ArtworkType.Podcast;

    public string Name => Title;
    public string? Subtitle => null;
    public string? Subsubtitle => null;

    public List<string> InfoDetails(ServerApiType? api, DetailInfoType details)
    {
        var info = new List<string>();
        if (details.Type != DetailType.NoCountInfo) PlayableContainableExtensions.AddCount(info, EpisodeCount, "Episode", "Episodes");
        if (details.Type is DetailType.Long or DetailType.NoCountInfo)
        {
            if (IsCached) info.Add("Cached");
            if (details.IsShowDetailedInfo) info.Add(PlayableContainableExtensions.IdInfo(Id));
        }
        return info;
    }

    public IReadOnlyList<AbstractPlayable> Playables => Episodes.Where(e => e.IsAvailableToUser()).ToList();
    public PlayerMode PlayContextType => PlayerMode.Podcast;
    public bool IsRateable => false;
    public bool IsFavoritable => false;
    public bool IsDownloadAvailable => true;

    public Task FetchFromServerAsync(ILibrarySyncer librarySyncer) => librarySyncer.SyncAsync(this);

    public Task RemoteToggleFavoriteAsync(LibraryStorage library, ILibrarySyncer syncer) => throw BackendError.NotSupported;

    public ArtworkCollection GetArtworkCollection() => new(DefaultArtworkType, this);

    public PlayableContainerIdentifier ContainerIdentifier => new(PlayableContainerBaseType.Podcast, Pk.ToString(CultureInfo.InvariantCulture));
}
