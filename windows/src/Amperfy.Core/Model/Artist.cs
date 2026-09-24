using Amperfy.Core.Api;

namespace Amperfy.Core.Model;

public class Artist : AbstractLibraryEntity, IPlayableContainable
{
    public int AlbumCountRaw { get; set; }
    public long DurationRaw { get; set; }
    public string? NameRaw { get; set; }
    public int RemoteAlbumCount { get; set; }
    public int SongCountRaw { get; set; }

    public int? GenrePk { get; set; }
    public virtual Genre? Genre { get; set; }
    public virtual ICollection<Album> AlbumsRaw { get; set; } = new HashSet<Album>();
    public virtual ICollection<Song> SongsRaw { get; set; } = new HashSet<Song>();

    protected Artist() { }

    public string Identifier => Name;

    [NotMapped]
    public string Name
    {
        get => NameRaw ?? "Unknown Artist";
        set
        {
            if (NameRaw == value) return;
            NameRaw = value;
            UpdateAlphabeticSectionInitial(value);
        }
    }

    public int SongCount => SongCountRaw;
    public int AlbumCount => AlbumCountRaw;
    public int Duration => (int)DurationRaw;

    public long RemoteDurationRaw { get; set; }

    [NotMapped]
    public int RemoteDuration
    {
        get => (int)RemoteDurationRaw;
        set
        {
            RemoteDurationRaw = value;
            DurationRaw = value;
        }
    }

    /// Songs sorted by album year, album, disk and track
    public List<Song> Songs => SongsRaw.SortByAlbum();
    public IReadOnlyList<AbstractPlayable> Playables => Songs;
    public List<Album> Albums => AlbumsRaw.ToList();

    public override ArtworkType DefaultArtworkType => ArtworkType.Artist;

    // IPlayableContainable
    public string? Subtitle => null;
    public string? Subsubtitle => null;

    public List<string> InfoDetails(ServerApiType? api, DetailInfoType details)
    {
        var info = new List<string>();
        if (details.Library is { } library && Account is { } account)
        {
            PlayableContainableExtensions.AddCount(info, library.GetAlbumCountContainingSongs(account, this), "Album", "Albums");
        }
        else PlayableContainableExtensions.AddCount(info, AlbumCount, "Album", "Albums");

        if (details.ArtistFilterSetting == ArtistCategoryFilter.AlbumArtists && details.Library is { } lib && Account is { } acc)
            PlayableContainableExtensions.AddCount(info, lib.GetSongCountOfArtist(acc, this), "Song", "Songs");
        else PlayableContainableExtensions.AddCount(info, SongCount, "Song", "Songs");

        if (details.Type == DetailType.Short && details.IsShowArtistDuration && Duration > 0)
            info.Add(Duration.AsDurationShortString());
        if (details.Type == DetailType.Long)
        {
            if (Genre is { } genre) info.Add($"Genre: {genre.Name}");
            if (Duration > 0) info.Add(Duration.AsDurationShortString());
            if (details.IsShowDetailedInfo) info.Add(PlayableContainableExtensions.IdInfo(Id));
        }
        return info;
    }

    public PlayerMode PlayContextType => PlayerMode.Music;
    public bool IsRateable => true;
    public bool IsFavoritable => true;
    public bool IsDownloadAvailable => true;

    public async Task RemoteToggleFavoriteAsync(LibraryStorage library, ILibrarySyncer syncer)
    {
        IsFavorite = !IsFavorite;
        library.SaveContext();
        await syncer.SetFavoriteAsync(this, IsFavorite);
    }

    public Task FetchFromServerAsync(ILibrarySyncer librarySyncer) => librarySyncer.SyncAsync(this);

    public ArtworkCollection GetArtworkCollection() => new(ArtworkType.Artist, this);

    public PlayableContainerIdentifier ContainerIdentifier => new(PlayableContainerBaseType.Artist, Pk.ToString(CultureInfo.InvariantCulture));
}
