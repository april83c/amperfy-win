using Amperfy.Core.Api;

namespace Amperfy.Core.Model;

public class Song : AbstractPlayable
{
    public DateTime? AddedDate { get; set; }
    public string? LyricsRelFilePath { get; set; }

    public int? AlbumPk { get; set; }
    public virtual Album? Album { get; set; }
    public int? ArtistPk { get; set; }
    public virtual Artist? Artist { get; set; }
    public int? DirectoryPk { get; set; }
    public virtual MusicDirectory? Directory { get; set; }
    public int? GenrePk { get; set; }
    public virtual Genre? Genre { get; set; }
    public int? MusicFolderPk { get; set; }
    public virtual MusicFolder? MusicFolder { get; set; }

    protected Song() { }

    public override DerivedPlayableType DerivedType => DerivedPlayableType.Song;

    public string Identifier => Title;

    public bool IsOrphaned => Album is null || Album.IsOrphaned;

    public override string CreatorName => Artist?.Name ?? "Unknown Artist";

    public override string? Subsubtitle => Album?.Name;

    public override void DeleteCache()
    {
        if (Album is { } album) album.IsCached = false;
        foreach (var item in PlaylistItems)
        {
            if (item.Playlist is { IsCached: true } playlist) playlist.IsCached = false;
        }
        if (Directory is { IsCached: true } dir) dir.IsCached = false;
        if (MusicFolder is { IsCached: true } folder) folder.IsCached = false;
    }

    public string DetailInfo =>
        $"{DisplayString} (album: {Album?.Name ?? "-"}, genre: {Genre?.Name ?? "-"}, id: {Id}, track: {Track}, year: {Year}, " +
        $"remote duration: {RemoteDuration}, disk: {Disk ?? "-"}, size: {Size}, contentType: {ContentType ?? "-"}, bitrate: {Bitrate})";

    public override List<string> InfoDetails(ServerApiType? api, DetailInfoType details)
    {
        var info = new List<string>();
        if (details.Type != DetailType.Long) return info;
        if (Track > 0) info.Add($"Track {Track}");
        if (Duration > 0) info.Add(Duration.AsDurationString());
        if (Year > 0) info.Add($"Year {Year}");
        else if (Album is { Year: > 0 } album) info.Add($"Year {album.Year}");
        if (Genre is { } genre) info.Add($"Genre: {genre.Name}");
        if (details.IsShowDetailedInfo)
        {
            if (Bitrate > 0) info.Add($"Bitrate: {Bitrate}");
            AddCacheMimeInfo(info);
            info.Add(PlayableContainableExtensions.IdInfo(Id));
        }
        return info;
    }

    /// See also LibraryStorage available-song query.
    public override bool IsAvailableToUser() => (Size > 0 && Album?.RemoteStatus == RemoteStatus.Available) || IsCached;
}

public static class SongListExtensions
{
    public static List<T> FilterServerDeleteUncachedSongs<T>(this IEnumerable<T> songs) where T : Song =>
        songs.Where(s => s.IsAvailableToUser()).ToList();

    public static List<T> SortByTrackNumber<T>(this IEnumerable<T> songs) where T : AbstractPlayable =>
        songs.OrderBy(s => s.Disk ?? "", StringComparer.Ordinal)
            .ThenBy(s => s.Track)
            .ThenBy(s => s.Title, StringComparer.Ordinal)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

    public static List<T> SortByAlbum<T>(this IEnumerable<T> songs) where T : Song =>
        songs.OrderBy(s => s.Album?.Year ?? 0)
            .ThenBy(s => s.Album?.Id ?? "", StringComparer.Ordinal)
            .ThenBy(s => s.Disk ?? "", StringComparer.Ordinal)
            .ThenBy(s => s.Track)
            .ThenBy(s => s.Title, StringComparer.Ordinal)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();
}
