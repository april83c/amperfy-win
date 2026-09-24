using Amperfy.Core.Api;

namespace Amperfy.Core.Model;

/// A directory of the server's file structure (Subsonic "getMusicDirectory").
public class MusicDirectory : AbstractLibraryEntity, IPlayableContainable
{
    public bool IsCached { get; set; }
    public string? NameRaw { get; set; }
    public int SongCountRaw { get; set; }
    public int SubdirectoryCountRaw { get; set; }

    public int? MusicFolderPk { get; set; }
    public virtual MusicFolder? MusicFolder { get; set; }
    public int? ParentPk { get; set; }
    public virtual MusicDirectory? Parent { get; set; }
    public virtual ICollection<Song> SongsRaw { get; set; } = new HashSet<Song>();
    public virtual ICollection<MusicDirectory> SubdirectoriesRaw { get; set; } = new HashSet<MusicDirectory>();

    protected MusicDirectory() { }

    [NotMapped]
    public string Name
    {
        get => NameRaw ?? "";
        set
        {
            if (NameRaw == value) return;
            NameRaw = value;
            UpdateAlphabeticSectionInitial(value);
        }
    }

    public int SongCount => SongCountRaw;
    public int SubdirectoryCount => SubdirectoryCountRaw;
    public List<Song> Songs => SongsRaw.OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    public List<MusicDirectory> Subdirectories => SubdirectoriesRaw.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    public IReadOnlyList<AbstractPlayable> Playables => Songs;

    public override ArtworkType DefaultArtworkType => ArtworkType.Folder;

    public string? Subtitle => null;
    public string? Subsubtitle => null;

    public List<string> InfoDetails(ServerApiType? api, DetailInfoType details)
    {
        var info = new List<string>();
        var subCount = SubdirectoriesRaw.Count;
        if (subCount == 1) info.Add("1 Subdirectory");
        else if (subCount > 1) info.Add($"{subCount} Subdirectories");
        PlayableContainableExtensions.AddCount(info, SongCount, "Song", "Songs");
        if (details.Type == DetailType.Long)
        {
            if (IsCached) info.Add("Cached");
            if (details.IsShowDetailedInfo) info.Add(PlayableContainableExtensions.IdInfo(Id));
        }
        return info;
    }

    public PlayerMode PlayContextType => PlayerMode.Music;
    public bool IsRateable => false;
    public bool IsFavoritable => false;
    public bool IsDownloadAvailable => true;

    public Task RemoteToggleFavoriteAsync(LibraryStorage library, ILibrarySyncer syncer) => throw BackendError.NotSupported;

    public Task FetchFromServerAsync(ILibrarySyncer librarySyncer) => librarySyncer.SyncAsync(this);

    public ArtworkCollection GetArtworkCollection() => new(ArtworkType.Folder, this);

    public PlayableContainerIdentifier ContainerIdentifier => new(PlayableContainerBaseType.Directory, Pk.ToString(CultureInfo.InvariantCulture));
}
