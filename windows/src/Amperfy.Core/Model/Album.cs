using Microsoft.EntityFrameworkCore.ChangeTracking;
using Amperfy.Core.Api;

namespace Amperfy.Core.Model;

public class Album : AbstractLibraryEntity, IPlayableContainable
{
    public const string OrphanedName = "Unknown (Orphaned)";

    public virtual long DurationRaw { get; set; }
    public virtual bool IsCached { get; set; }
    public virtual bool IsSongsMetaDataSynced { get; set; }
    public virtual string? NameRaw { get; set; }
    public virtual int NewestIndex { get; set; }
    public virtual int RecentIndex { get; set; }
    public virtual long RemoteDurationRaw { get; set; }
    public virtual int RemoteSongCount { get; set; }
    public virtual int SongCountRaw { get; set; }
    public virtual int Year { get; set; }

    public virtual int? ArtistPk { get; set; }
    public virtual Artist? Artist { get; set; }
    public virtual int? GenrePk { get; set; }
    public virtual Genre? Genre { get; set; }
    public virtual ICollection<Song> SongsRaw { get; set; } = new ObservableHashSet<Song>();

    protected Album() { }

    public string Identifier => Name;

    [NotMapped]
    public string Name
    {
        get => NameRaw ?? "Unknown Album";
        set
        {
            if (NameRaw == value) return;
            NameRaw = value;
            UpdateAlphabeticSectionInitial(value);
        }
    }

    public int Duration => (int)DurationRaw;

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

    public void UpdateIsNewestInfo(int index) => NewestIndex = index;
    public void MarkAsNotNewAnymore() => NewestIndex = 0;
    public void UpdateIsRecentInfo(int index) => RecentIndex = index;
    public void MarkAsNotRecentAnymore() => RecentIndex = 0;

    public int SongCount => SongCountRaw != 0 ? SongCountRaw : RemoteSongCount;

    /// Songs sorted by disk and track number
    public List<Song> Songs => SongsRaw.SortByTrackNumber();

    public IReadOnlyList<AbstractPlayable> Playables => Songs;

    public bool IsOrphaned => Identifier == OrphanedName;

    public override ArtworkType DefaultArtworkType => ArtworkType.Album;

    public void MarkAsRemoteDeleted()
    {
        RemoteStatus = RemoteStatus.Deleted;
        foreach (var song in SongsRaw) song.RemoteStatus = RemoteStatus.Deleted;
    }

    // IPlayableContainable
    public string? Subtitle => Artist?.Name;
    public string? Subsubtitle => null;

    public List<string> InfoDetails(ServerApiType? api, DetailInfoType details)
    {
        var info = new List<string>();
        PlayableContainableExtensions.AddCount(info, SongCount, "Song", "Songs");
        if (details.Type == DetailType.Short && details.IsShowAlbumDuration && Duration > 0)
            info.Add(Duration.AsDurationShortString());
        if (details.Type == DetailType.Long)
        {
            if (IsCached) info.Add("Cached");
            if (Year > 0) info.Add($"Year {Year}");
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

    public ArtworkCollection GetArtworkCollection() => new(DefaultArtworkType, this);

    public PlayableContainerIdentifier ContainerIdentifier => new(PlayableContainerBaseType.Album, Pk.ToString(CultureInfo.InvariantCulture));
}
