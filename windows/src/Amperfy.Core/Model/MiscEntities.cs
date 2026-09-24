namespace Amperfy.Core.Model;

public class ScrobbleEntry
{
    public virtual int Pk { get; set; }
    public virtual DateTime? Date { get; set; }
    public virtual bool IsUploaded { get; set; }

    public virtual int? AccountPk { get; set; }
    public virtual Account? Account { get; set; }
    public virtual int? PlayablePk { get; set; }
    public virtual AbstractPlayable? Playable { get; set; }

    public ScrobbleEntry() { }
}

public class SearchHistoryItem
{
    public virtual int Pk { get; set; }
    public virtual DateTime? Date { get; set; }

    public virtual int? AccountPk { get; set; }
    public virtual Account? Account { get; set; }
    public virtual int? SearchedLibraryEntityPk { get; set; }
    public virtual AbstractLibraryEntity? SearchedLibraryEntity { get; set; }
    public virtual int? SearchedPlaylistPk { get; set; }
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
    public virtual int Pk { get; set; }
    public virtual DateTime CreationDate { get; set; } = DateTime.UtcNow;
    public virtual string Message { get; set; } = "";
    public virtual int StatusCode { get; set; }
    public virtual int SuppressionTimeInterval { get; set; }
    public virtual LogEntryType Type { get; set; } = LogEntryType.Error;

    public LogEntry() { }
}

/// Persistent player state (PlayerMO). The queue logic lives in Player.PlayerData.
public class PlayerState
{
    public virtual int Pk { get; set; }
    public virtual int AutoCachePlayedItemSetting { get; set; } = 1;
    public virtual bool IsUserQueuePlaying { get; set; }
    public virtual int MusicIndex { get; set; }
    public virtual double MusicPlaybackRate { get; set; } = 1;
    public virtual PlayerMode PlayerMode { get; set; } = PlayerMode.Music;
    public virtual int PodcastIndex { get; set; }
    public virtual double PodcastPlaybackRate { get; set; } = 1;
    public virtual RepeatMode RepeatSetting { get; set; } = RepeatMode.Off;
    public virtual int ShuffleSetting { get; set; }

    public virtual int? ContextPlaylistPk { get; set; }
    public virtual Playlist? ContextPlaylist { get; set; }
    public virtual int? ShuffledContextPlaylistPk { get; set; }
    public virtual Playlist? ShuffledContextPlaylist { get; set; }
    public virtual int? UserQueuePlaylistPk { get; set; }
    public virtual Playlist? UserQueuePlaylist { get; set; }
    public virtual int? PodcastPlaylistPk { get; set; }
    public virtual Playlist? PodcastPlaylist { get; set; }

    public PlayerState() { }
}
