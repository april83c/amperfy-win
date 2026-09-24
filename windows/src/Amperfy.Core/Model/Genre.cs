using Microsoft.EntityFrameworkCore.ChangeTracking;
using Amperfy.Core.Api;

namespace Amperfy.Core.Model;

public class Genre : AbstractLibraryEntity, IPlayableContainable
{
    public virtual int AlbumCountRaw { get; set; }
    public virtual int ArtistCountRaw { get; set; }
    public virtual string NameRaw { get; set; } = "";
    public virtual int SongCountRaw { get; set; }

    public virtual ICollection<Album> AlbumsRaw { get; set; } = new ObservableHashSet<Album>();
    public virtual ICollection<Artist> ArtistsRaw { get; set; } = new ObservableHashSet<Artist>();
    public virtual ICollection<Song> SongsRaw { get; set; } = new ObservableHashSet<Song>();

    protected Genre() { }

    public string Identifier => Name;

    [NotMapped]
    public string Name
    {
        get => string.IsNullOrEmpty(NameRaw) ? "Unknown Genre" : NameRaw;
        set
        {
            if (NameRaw == value) return;
            NameRaw = value;
            UpdateAlphabeticSectionInitial(value);
        }
    }

    public int ArtistCount => ArtistCountRaw;
    public int AlbumCount => AlbumCountRaw;
    public int SongCount => SongCountRaw;
    public List<Artist> Artists => ArtistsRaw.ToList();
    public List<Album> Albums => AlbumsRaw.ToList();
    public List<Song> Songs => SongsRaw.ToList();
    public IReadOnlyList<AbstractPlayable> Playables => Songs;

    public override ArtworkType DefaultArtworkType => ArtworkType.Genre;

    public string? Subtitle => null;
    public string? Subsubtitle => null;

    public List<string> InfoDetails(ServerApiType? api, DetailInfoType details)
    {
        var info = new List<string>();
        if (api == ServerApiType.Ampache) PlayableContainableExtensions.AddCount(info, ArtistCount, "Artist", "Artists");
        PlayableContainableExtensions.AddCount(info, AlbumCount, "Album", "Albums");
        PlayableContainableExtensions.AddCount(info, SongCount, "Song", "Songs");
        if (details.Type == DetailType.Long && details.IsShowDetailedInfo) info.Add(PlayableContainableExtensions.IdInfo(Id));
        return info;
    }

    public PlayerMode PlayContextType => PlayerMode.Music;
    public bool IsRateable => false;
    public bool IsFavoritable => false;
    public bool IsDownloadAvailable => true;

    public Task FetchFromServerAsync(ILibrarySyncer librarySyncer) => librarySyncer.SyncAsync(this);

    public Task RemoteToggleFavoriteAsync(LibraryStorage library, ILibrarySyncer syncer) => throw BackendError.NotSupported;

    public ArtworkCollection GetArtworkCollection() => new(DefaultArtworkType, this);

    public PlayableContainerIdentifier ContainerIdentifier => new(PlayableContainerBaseType.Genre, Pk.ToString(CultureInfo.InvariantCulture));
}
