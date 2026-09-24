namespace Amperfy.Core.Model;

public class ScrobbleEntry
{
    public int Pk { get; set; }
    public DateTime? Date { get; set; }
    public bool IsUploaded { get; set; }

    public int? AccountPk { get; set; }
    public virtual Account? Account { get; set; }
    public int? PlayablePk { get; set; }
    public virtual AbstractPlayable? Playable { get; set; }

    public ScrobbleEntry() { }
}

public class SearchHistoryItem
{
    public int Pk { get; set; }
    public DateTime? Date { get; set; }

    public int? AccountPk { get; set; }
    public virtual Account? Account { get; set; }
    public int? SearchedLibraryEntityPk { get; set; }
    public virtual AbstractLibraryEntity? SearchedLibraryEntity { get; set; }
    public int? SearchedPlaylistPk { get; set; }
    public virtual Playlist? SearchedPlaylist { get; set; }

    public SearchHistoryItem() { }

    [NotMapped]
    public IPlayableContainable? SearchedPlayableContainable
    {
        get
        {
            if (SearchedLibraryEntity is IPlayableContainable c and (Song or PodcastEpisode or Album or Artist or Podcast)) return c;
            return SearchedPlaylist;
        }
        set
        {
            switch (value)
            {
                case Playlist p:
                    SearchedLibraryEntity = null;
                    SearchedPlaylist = p;
                    break;
                case AbstractLibraryEntity e:
                    SearchedLibraryEntity = e;
                    SearchedPlaylist = null;
                    break;
            }
        }
    }
}

public class LogEntry
{
    public int Pk { get; set; }
    public DateTime CreationDate { get; set; } = DateTime.UtcNow;
    public string Message { get; set; } = "";
    public int StatusCode { get; set; }
    public int SuppressionTimeInterval { get; set; }
    public LogEntryType Type { get; set; } = LogEntryType.Error;

    public LogEntry() { }
}

/// Persistent player state (PlayerMO). The queue logic lives in Player.PlayerData.
public class PlayerState
{
    public int Pk { get; set; }
    public int AutoCachePlayedItemSetting { get; set; } = 1;
    public bool IsUserQueuePlaying { get; set; }
    public int MusicIndex { get; set; }
    public double MusicPlaybackRate { get; set; } = 1;
    public PlayerMode PlayerMode { get; set; } = PlayerMode.Music;
    public int PodcastIndex { get; set; }
    public double PodcastPlaybackRate { get; set; } = 1;
    public RepeatMode RepeatSetting { get; set; } = RepeatMode.Off;
    public int ShuffleSetting { get; set; }

    public int? ContextPlaylistPk { get; set; }
    public virtual Playlist? ContextPlaylist { get; set; }
    public int? ShuffledContextPlaylistPk { get; set; }
    public virtual Playlist? ShuffledContextPlaylist { get; set; }
    public int? UserQueuePlaylistPk { get; set; }
    public virtual Playlist? UserQueuePlaylist { get; set; }
    public int? PodcastPlaylistPk { get; set; }
    public virtual Playlist? PodcastPlaylist { get; set; }

    public PlayerState() { }
}
