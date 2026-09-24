namespace Amperfy.Core.Storage;

/// Disk usage of the cache of one account (settings: library/artwork cache info).
public sealed record CacheSizeInfo(long Songs, long PodcastEpisodes, long Artworks, long EmbeddedArtworks, long Lyrics)
{
    /// Downloaded songs and podcast episodes (the part limited by <see cref="UserSettings.CacheLimit"/>).
    public long Playables => Songs + PodcastEpisodes;

    public long Total => Songs + PodcastEpisodes + Artworks + EmbeddedArtworks + Lyrics;

    public static CacheSizeInfo Empty { get; } = new(0, 0, 0, 0, 0);

    public static CacheSizeInfo operator +(CacheSizeInfo a, CacheSizeInfo b) =>
        new(a.Songs + b.Songs, a.PodcastEpisodes + b.PodcastEpisodes, a.Artworks + b.Artworks,
            a.EmbeddedArtworks + b.EmbeddedArtworks, a.Lyrics + b.Lyrics);
}

public static class CacheFileManagerSizeExtensions
{
    /// Calculates the cache size of an account by walking its cache directories. Only file system
    /// access: may be called from a background thread.
    public static CacheSizeInfo CalculateCacheSize(this CacheFileManager fileManager, AccountInfo account) => new(
        CacheFileManager.DirectorySize(fileManager.GetAbsolutePath(CacheFileManager.GetRelSongsDirectory(account))),
        CacheFileManager.DirectorySize(fileManager.GetAbsolutePath(CacheFileManager.GetRelPodcastEpisodesDirectory(account))),
        CacheFileManager.DirectorySize(fileManager.GetAbsolutePath(CacheFileManager.GetRelArtworkDirectory(account))),
        CacheFileManager.DirectorySize(fileManager.GetAbsolutePath(CacheFileManager.GetRelEmbeddedArtworkDirectory(account))),
        CacheFileManager.DirectorySize(fileManager.GetAbsolutePath(CacheFileManager.GetRelLyricsDirectory(account))));

    /// Cache size of all accounts that have a cache directory.
    public static CacheSizeInfo CalculateCompleteCacheSize(this CacheFileManager fileManager) =>
        fileManager.GetAccounts().Aggregate(CacheSizeInfo.Empty, (sum, account) => sum + fileManager.CalculateCacheSize(account));
}

/// Cache size limit as shown in the settings (a value with the unit MB or GB).
public readonly record struct CacheSizeLimit(double Value, bool IsGigabyte)
{
    public const long BytesPerMegabyte = 1_000_000;
    public const long BytesPerGigabyte = 1_000_000_000;

    /// 0 means no limit.
    public long Bytes => Value <= 0 ? 0 : (long)Math.Round(Value * (IsGigabyte ? BytesPerGigabyte : BytesPerMegabyte));

    public bool IsNoLimit => Bytes == 0;

    /// Splits a byte count into the largest unit that gives a value >= 1 (GB from 1 GB on).
    public static CacheSizeLimit FromBytes(long bytes)
    {
        if (bytes <= 0) return new CacheSizeLimit(0, IsGigabyte: false);
        if (bytes >= BytesPerGigabyte) return new CacheSizeLimit(Math.Round((double)bytes / BytesPerGigabyte, 2), IsGigabyte: true);
        return new CacheSizeLimit(Math.Round((double)bytes / BytesPerMegabyte, 2), IsGigabyte: false);
    }

    public override string ToString() => IsNoLimit ? "No Limit" : Bytes.AsByteString();
}
