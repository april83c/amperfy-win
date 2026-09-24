using Microsoft.EntityFrameworkCore.ChangeTracking;
using Amperfy.Core.Api;
using Amperfy.Core.Downloads;

namespace Amperfy.Core.Model;

public abstract class AbstractPlayable : AbstractLibraryEntity, IDownloadable, IPlayableContainable
{
    public virtual int Bitrate { get; set; } // byte per second
    public virtual int CombinedDuration { get; set; }
    public virtual string? ContentType { get; set; }
    public virtual string? ContentTypeTranscoded { get; set; }
    public virtual string? Disk { get; set; }
    public virtual int PlayDurationRaw { get; set; }
    public virtual int PlayProgress { get; set; }
    public virtual string? RelFilePath { get; set; }
    public virtual int RemoteDurationRaw { get; set; }
    public virtual float ReplayGainAlbumGain { get; set; }
    public virtual float ReplayGainAlbumPeak { get; set; }
    public virtual float ReplayGainTrackGain { get; set; }
    public virtual float ReplayGainTrackPeak { get; set; }
    public virtual long Size { get; set; }
    public virtual string? TitleRaw { get; set; }
    public virtual int Track { get; set; }
    public virtual string? Url { get; set; }
    public virtual int Year { get; set; }

    public virtual Download? Download { get; set; }
    public virtual EmbeddedArtwork? EmbeddedArtwork { get; set; }
    public virtual ICollection<PlaylistItem> PlaylistItems { get; set; } = new ObservableHashSet<PlaylistItem>();
    public virtual ICollection<ScrobbleEntry> ScrobbleEntries { get; set; } = new ObservableHashSet<ScrobbleEntry>();

    protected AbstractPlayable() { }

    [NotMapped]
    public string Title
    {
        get => TitleRaw ?? "Unknown Title";
        set
        {
            if (TitleRaw == value) return;
            TitleRaw = value;
            UpdateAlphabeticSectionInitial(value);
        }
    }

    public int Duration => CombinedDuration;

    /// duration based on the data from the xml parser
    [NotMapped]
    public int RemoteDuration
    {
        get => RemoteDurationRaw;
        set
        {
            if (RemoteDurationRaw == value) return;
            RemoteDurationRaw = value;
            if (PlayDurationRaw == 0) CombinedDuration = value;
        }
    }

    /// duration based on the downloaded/streamed file reported by the player
    [NotMapped]
    public int PlayDuration
    {
        get => PlayDurationRaw;
        set
        {
            if (PlayDurationRaw == value) return;
            PlayDurationRaw = value;
            CombinedDuration = value;
        }
    }

    public bool UpdateDuration()
    {
        var combined = PlayDurationRaw > 0 ? PlayDurationRaw : RemoteDurationRaw;
        if (CombinedDuration == combined) return false;
        CombinedDuration = combined;
        return true;
    }

    public bool IsCached => RelFilePath is not null;

    public string? FileContentType => IsCached ? ContentTypeTranscoded ?? ContentType : ContentType;

    public bool IsPlayableOnWindows
    {
        get
        {
            var type = FileContentType;
            return type is null || MimeFileConverter.IsMimeTypePlayable(type);
        }
    }

    public string? CompatibleContentType =>
        !IsPlayableOnWindows || FileContentType is null ? null : MimeFileConverter.ConvertToValidMimeTypeWhenNecessary(FileContentType);

    public abstract DerivedPlayableType DerivedType { get; }

    public bool IsSong => DerivedType == DerivedPlayableType.Song;
    public bool IsPodcastEpisode => DerivedType == DerivedPlayableType.PodcastEpisode;
    public bool IsRadio => DerivedType == DerivedPlayableType.Radio;
    public Song? AsSong => this as Song;
    public PodcastEpisode? AsPodcastEpisode => this as PodcastEpisode;
    public Radio? AsRadio => this as Radio;

    public virtual string CreatorName => "Unknown";

    public string DisplayString => DerivedType == DerivedPlayableType.Radio ? Title : $"{CreatorName} - {Title}";

    public AbstractPlayableInfo Info => new(Id, Pk, DerivedType, AsPodcastEpisode?.StreamId);

    public DownloadableType DownloadableType => DownloadableType.Playable;

    public override string? ImagePath(ArtworkDisplayPreference setting) => setting switch
    {
        ArtworkDisplayPreference.Id3TagOnly => EmbeddedArtwork?.ImagePath,
        ArtworkDisplayPreference.ServerArtworkOnly => Artwork?.ImagePath,
        ArtworkDisplayPreference.PreferServerArtwork => Artwork?.ImagePath ?? EmbeddedArtwork?.ImagePath,
        _ => EmbeddedArtwork?.ImagePath ?? Artwork?.ImagePath,
    };

    /// Marks containers (album, playlists, ...) as not completely cached anymore.
    public virtual void DeleteCache() { }

    public abstract bool IsAvailableToUser();

    public virtual List<string> InfoDetails(ServerApiType? api, DetailInfoType details)
    {
        var info = new List<string>();
        if (details.Type != DetailType.Long) return info;
        if (Year > 0) info.Add($"Year {Year}");
        if (Duration > 0) info.Add(Duration.AsDurationString());
        if (Bitrate > 0) info.Add($"Bitrate {Bitrate}");
        if (details.IsShowDetailedInfo) AddCacheMimeInfo(info);
        return info;
    }

    protected void AddCacheMimeInfo(List<string> info)
    {
        if (!IsCached) return;
        var fileContentType = FileContentType;
        if (ContentType is not null && fileContentType is not null && ContentType != fileContentType)
        {
            info.Add($"Transcoded MIME Type: {fileContentType}");
            info.Add($"Original MIME Type: {ContentType}");
        }
        else if (ContentType is not null) info.Add($"Cache MIME Type: {ContentType}");
        else if (fileContentType is not null) info.Add($"Cache MIME Type: {fileContentType}");
    }

    public override ArtworkType DefaultArtworkType => DerivedType switch
    {
        DerivedPlayableType.Song => ArtworkType.Song,
        DerivedPlayableType.PodcastEpisode => ArtworkType.PodcastEpisode,
        _ => ArtworkType.Radio,
    };

    /// keep empty to ignore context based play
    public override void PlayedViaContext() { }

    public void CountPlayed()
    {
        LastTimePlayed = DateTime.UtcNow;
        PlayCount += 1;
    }

    // IPlayableContainable
    public string Name => Title;
    public virtual string? Subtitle => CreatorName;
    public virtual string? Subsubtitle => null;
    public IReadOnlyList<AbstractPlayable> Playables => [this];
    public PlayerMode PlayContextType => DerivedType == DerivedPlayableType.PodcastEpisode ? PlayerMode.Podcast : PlayerMode.Music;
    public bool IsRateable => DerivedType == DerivedPlayableType.Song;
    public bool IsFavoritable => DerivedType == DerivedPlayableType.Song;
    public virtual bool IsDownloadAvailable => DerivedType switch
    {
        DerivedPlayableType.Song => true,
        DerivedPlayableType.PodcastEpisode => IsAvailableToUser(),
        _ => false,
    };

    public async Task FetchFromServerAsync(ILibrarySyncer librarySyncer)
    {
        if (AsSong is { } song) await librarySyncer.SyncAsync(song);
    }

    public async Task RemoteToggleFavoriteAsync(LibraryStorage library, ILibrarySyncer syncer)
    {
        if (AsSong is not { } song) return;
        IsFavorite = !IsFavorite;
        library.SaveContext();
        await syncer.SetFavoriteAsync(song, IsFavorite);
    }

    public ArtworkCollection GetArtworkCollection() => new(DefaultArtworkType, this);

    public PlayableContainerIdentifier ContainerIdentifier => new(DerivedType switch
    {
        DerivedPlayableType.Song => PlayableContainerBaseType.Song,
        DerivedPlayableType.PodcastEpisode => PlayableContainerBaseType.PodcastEpisode,
        _ => PlayableContainerBaseType.Radio,
    }, Pk.ToString(CultureInfo.InvariantCulture));
}

public static class PlayableListExtensions
{
    public static List<T> FilterCached<T>(this IEnumerable<T> list) where T : AbstractPlayable => list.Where(p => p.IsCached).ToList();

    public static List<T> FilterCached<T>(this IEnumerable<T> list, bool isFilterActive) where T : AbstractPlayable =>
        isFilterActive ? list.Where(p => p.IsCached).ToList() : list.ToList();

    public static List<T> FilterCustomArt<T>(this IEnumerable<T> list) where T : AbstractPlayable => list.Where(p => p.Artwork is not null).ToList();

    public static bool HasCachedItems<T>(this IEnumerable<T> list) where T : AbstractPlayable => list.Any(p => p.IsCached);

    public static bool IsCachedCompletely<T>(this IReadOnlyCollection<T> list) where T : AbstractPlayable => list.All(p => p.IsCached);

    public static List<T> SortById<T>(this IEnumerable<T> list) where T : AbstractPlayable =>
        list.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();

    public static List<T> SortByTitle<T>(this IEnumerable<T> list) where T : AbstractPlayable =>
        list.OrderBy(p => p.Title, StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();

    public static List<Song> FilterSongs(this IEnumerable<AbstractPlayable> list) => list.OfType<Song>().ToList();
}
