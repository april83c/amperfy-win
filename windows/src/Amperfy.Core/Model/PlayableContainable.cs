using Amperfy.Core.Api;
using Amperfy.Core.Downloads;

namespace Amperfy.Core.Model;

public sealed class DetailInfoType
{
    public DetailType Type { get; init; }
    public bool IsShowDetailedInfo { get; init; }
    public bool IsShowAlbumDuration { get; init; }
    public bool IsShowArtistDuration { get; init; }
    public ArtistCategoryFilter ArtistFilterSetting { get; init; } = ArtistCategoryFilter.AlbumArtists;
    /// Optional storage used for count queries (e.g. albums of an artist).
    public LibraryStorage? Library { get; init; }

    public DetailInfoType() { }

    public DetailInfoType(DetailType type, AmperfySettings settings, LibraryStorage? library = null)
    {
        Type = type;
        IsShowDetailedInfo = settings.User.IsShowDetailedInfo;
        IsShowAlbumDuration = settings.User.IsShowAlbumDuration;
        IsShowArtistDuration = settings.User.IsShowArtistDuration;
        ArtistFilterSetting = settings.User.ArtistsFilterSetting;
        Library = library;
    }
}

public sealed record PlayableContainerIdentifier(PlayableContainerBaseType? Type, string? ObjectId);

public sealed class ArtworkCollection
{
    public ArtworkType DefaultArtworkType { get; }
    public AbstractLibraryEntity? SingleImageEntity { get; }
    public IReadOnlyList<AbstractLibraryEntity>? QuadImageEntity { get; }

    public ArtworkCollection(ArtworkType defaultArtworkType, AbstractLibraryEntity? singleImageEntity, IReadOnlyList<AbstractLibraryEntity>? quadImageEntity = null)
    {
        DefaultArtworkType = defaultArtworkType;
        SingleImageEntity = singleImageEntity;
        QuadImageEntity = quadImageEntity;
    }
}

/// Something that contains playables and can be played as a context (album, artist, playlist, ...).
public interface IPlayableContainable
{
    string Id { get; }
    string Name { get; }
    string? Subtitle { get; }
    string? Subsubtitle { get; }
    List<string> InfoDetails(ServerApiType? api, DetailInfoType details);
    IReadOnlyList<AbstractPlayable> Playables { get; }
    PlayerMode PlayContextType { get; }
    Account? Account { get; }
    bool IsRateable { get; }
    bool IsFavoritable { get; }
    bool IsFavorite { get; }
    bool IsDownloadAvailable { get; }
    ArtworkCollection GetArtworkCollection();
    Task FetchFromServerAsync(ILibrarySyncer librarySyncer);
    Task RemoteToggleFavoriteAsync(LibraryStorage library, ILibrarySyncer syncer);
    void PlayedViaContext();
    PlayableContainerIdentifier ContainerIdentifier { get; }
}

public static class PlayableContainableExtensions
{
    public const string OneMiddleDot = "·";

    public static string Info(this IPlayableContainable c, ServerApiType? api, DetailInfoType details) =>
        string.Join($" {OneMiddleDot} ", c.InfoDetails(api, details));

    public static void CachePlayables(this IPlayableContainable c, IDownloadManageable downloadManager)
    {
        downloadManager.Download(c.Playables.Where(p => !p.IsCached).Cast<IDownloadable>().ToList());
    }

    /// Fetches the container from the server if the app is in online mode.
    public static async Task FetchAsync(this IPlayableContainable c, AmperfySettings settings, ILibrarySyncer librarySyncer)
    {
        if (!settings.User.IsOnlineMode) return;
        await c.FetchFromServerAsync(librarySyncer);
    }

    internal static void AddCount(List<string> info, int count, string singular, string plural)
    {
        if (count == 1) info.Add($"1 {singular}");
        else if (count > 1) info.Add($"{count} {plural}");
    }

    internal static string IdInfo(string id) => $"ID: {(string.IsNullOrEmpty(id) ? "-" : id)}";
}
