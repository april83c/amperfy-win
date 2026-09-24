using Amperfy.Core.Api;

namespace Amperfy.Core.Model;

public class PodcastEpisode : AbstractPlayable
{
    public string? Depiction { get; set; }
    public DateTime? PublishDateRaw { get; set; }
    public PodcastEpisodeRemoteStatus PodcastStatus { get; set; } = PodcastEpisodeRemoteStatus.Undefined;
    public string? StreamId { get; set; }

    public int? PodcastPk { get; set; }
    public virtual Podcast? Podcast { get; set; }

    /// used by parsers as a temporary buffer
    [NotMapped] public string TitleRawParsed { get; set; } = "";
    [NotMapped] public string? DepictionRawParsed { get; set; }

    protected PodcastEpisode() { }

    public override DerivedPlayableType DerivedType => DerivedPlayableType.PodcastEpisode;

    public override string CreatorName => Podcast?.Title ?? "Unknown Podcast";

    [NotMapped]
    public DateTime PublishDate
    {
        get => PublishDateRaw ?? DateTime.UtcNow;
        set => PublishDateRaw = value;
    }

    public PodcastEpisodeUserStatus UserStatus =>
        IsCached ? PodcastEpisodeUserStatus.Cached :
        PodcastStatus == PodcastEpisodeRemoteStatus.Completed ? PodcastEpisodeUserStatus.AvailableOnServer :
        PodcastStatus == PodcastEpisodeRemoteStatus.Deleted ? PodcastEpisodeUserStatus.Deleted :
        PodcastEpisodeUserStatus.SyncingOnServer;

    public override bool IsAvailableToUser() =>
        UserStatus is PodcastEpisodeUserStatus.Cached or PodcastEpisodeUserStatus.AvailableOnServer;

    public int? RemainingTimeInSec => PlayDuration > 0 && PlayProgress > 0 ? PlayDuration - PlayProgress : null;

    public float? PlayProgressPercent => PlayDuration > 0 && PlayProgress > 0 ? (float)PlayProgress / PlayDuration : null;

    public override void DeleteCache()
    {
        if (Podcast is { } podcast) podcast.IsCached = false;
        foreach (var item in PlaylistItems)
        {
            if (item.Playlist is { IsCached: true } playlist) playlist.IsCached = false;
        }
    }

    public string DetailInfo =>
        $"{Title} (album: {Podcast?.Title ?? "-"}, id: {Id}, track: {Track}, year: {Year}, remote duration: {RemoteDuration}, " +
        $"disk: {Disk ?? "-"}, size: {Size}, contentType: {ContentType ?? "-"}, bitrate: {Bitrate}, description: {Depiction ?? "-"} " +
        $"publishDate: {PublishDate.AsIso8601String()}, podcastStatus: {PodcastStatus})";

    public override List<string> InfoDetails(ServerApiType? api, DetailInfoType details)
    {
        var info = new List<string>();
        if (details.Type != DetailType.Long) return info;
        info.Add(PublishDate.AsShortDayMonthString());
        if (!IsAvailableToUser() && !IsCached) info.Add("Not Available");
        else if (RemainingTimeInSec is { } remaining) info.Add($"{remaining.AsDurationString()} left");
        else if (Duration > 0) info.Add(Duration.AsDurationString());
        if (details.IsShowDetailedInfo)
        {
            if (Bitrate > 0) info.Add($"Bitrate: {Bitrate}");
            AddCacheMimeInfo(info);
            info.Add(PlayableContainableExtensions.IdInfo(Id));
        }
        return info;
    }
}

public static class PodcastEpisodeListExtensions
{
    public static List<T> SortByPublishDate<T>(this IEnumerable<T> episodes) where T : PodcastEpisode =>
        episodes.OrderByDescending(e => e.PublishDate).ToList();
}
