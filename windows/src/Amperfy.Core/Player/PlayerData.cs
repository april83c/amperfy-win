namespace Amperfy.Core.Player;

/// Port of the Swift PlayerStatusPersistent protocol.
public interface IPlayerStatusPersistent
{
    void Stop();
    bool IsAutoCachePlayedItems { get; }
    void SetAutoCachePlayedItems(bool newValue);
    bool IsPopupBarAllowedToHide { get; }
    int MusicItemCount { get; }
    int PodcastItemCount { get; }
    PlayerMode PlayerMode { get; }
    void SetPlayerMode(PlayerMode newValue);
    bool IsShuffle { get; }
    void SetShuffle(bool newValue);
    RepeatMode RepeatMode { get; }
    void SetRepeatMode(RepeatMode newValue);
    PlaybackRate PlaybackRate { get; }
    void SetPlaybackRate(PlaybackRate newValue);
    PlaybackRate MusicPlaybackRate { get; }
    void SetMusicPlaybackRate(PlaybackRate newValue);
    PlaybackRate PodcastPlaybackRate { get; }
    void SetPodcastPlaybackRate(PlaybackRate newValue);
}

/// Port of the Swift PlayerQueuesPersistent protocol.
public interface IPlayerQueuesPersistent
{
    bool IsUserQueuePlaying { get; }
    void SetUserQueuePlaying(bool newValue);
    bool IsUserQueueVisible { get; }

    int CurrentIndex { get; }
    void SetCurrentIndex(int newValue);
    AbstractPlayable? CurrentItem { get; }
    AbstractPlayable? CurrentMusicItem { get; }
    AbstractPlayable? CurrentPodcastItem { get; }
    Playlist ActiveQueue { get; }
    Playlist InactiveQueue { get; }
    Playlist ContextQueue { get; }
    Playlist PodcastQueue { get; }
    string ContextName { get; }
    void SetContextName(string newValue);
    Playlist UserQueuePlaylist { get; }

    void InsertActiveQueue(IReadOnlyList<AbstractPlayable> playables);
    void AppendActiveQueue(IReadOnlyList<AbstractPlayable> playables);
    void InsertContextQueue(IReadOnlyList<AbstractPlayable> playables);
    void AppendContextQueue(IReadOnlyList<AbstractPlayable> playables);
    void InsertUserQueue(IReadOnlyList<AbstractPlayable> playables);
    void AppendUserQueue(IReadOnlyList<AbstractPlayable> playables);
    void InsertPodcastQueue(IReadOnlyList<AbstractPlayable> playables);
    void AppendPodcastQueue(IReadOnlyList<AbstractPlayable> playables);
    void ClearActiveQueue();
    void ClearUserQueue();
    void ClearContextQueue();
    void RemoveAllItems();

    /// C# addition: persists direct playlist modifications (the Swift Playlist wrapper saved after every change).
    void SaveContext();
}

/// Port of PlayerData.swift: wraps the persistent <see cref="PlayerState"/> (PlayerMO) and its four queue playlists.
public sealed class PlayerData : IPlayerStatusPersistent, IPlayerQueuesPersistent
{
    private readonly LibraryStorage _library;
    private readonly PlayerState _managedObject;
    private readonly Playlist _userQueuePlaylistInternal;
    private readonly Playlist _contextPlaylist;
    private readonly Playlist _shuffledContextPlaylist;
    private readonly Playlist _podcastPlaylist;

    public const string EntityName = "Player";

    public PlayerData(LibraryStorage library, PlayerState managedObject, Playlist userQueue, Playlist contextQueue,
        Playlist shuffledContextQueue, Playlist podcastQueue)
    {
        _library = library;
        _managedObject = managedObject;
        _userQueuePlaylistInternal = userQueue;
        _contextPlaylist = contextQueue;
        _shuffledContextPlaylist = shuffledContextQueue;
        _podcastPlaylist = podcastQueue;
    }

    /// Port of LibraryStorage.getPlayerData(): loads (or creates) the player state and resyncs the
    /// shuffled context queue if it went out of sync with the context queue.
    public static PlayerData Create(LibraryStorage library)
    {
        var state = library.GetPlayerState();
        var userQueuePlaylist = state.UserQueuePlaylist!;
        var contextPlaylist = state.ContextPlaylist!;
        var shuffledContextPlaylist = state.ShuffledContextPlaylist!;
        var podcastPlaylist = state.PodcastPlaylist!;

        if (shuffledContextPlaylist.ItemsRaw.Count != contextPlaylist.ItemsRaw.Count)
        {
            shuffledContextPlaylist.RemoveAllItems();
            shuffledContextPlaylist.Append(contextPlaylist.PlayablesList);
            shuffledContextPlaylist.Shuffle();
            library.SaveContext();
        }

        return new PlayerData(library, state, userQueuePlaylist, contextPlaylist, shuffledContextPlaylist, podcastPlaylist);
    }

    public PlayerState ManagedObject => _managedObject;

    public void SaveContext() => _library.SaveContext();

    public override bool Equals(object? obj) => obj is PlayerData other && ReferenceEquals(_managedObject, other._managedObject);
    public override int GetHashCode() => _managedObject.GetHashCode();

    // --- PlayerStatusPersistent ---------------------------------------------------------------

    public void Stop()
    {
        SetCurrentIndex(0);
        switch (PlayerMode)
        {
            case PlayerMode.Music:
                SetUserQueuePlaying(false);
                ClearUserQueue();
                break;
            case PlayerMode.Podcast:
                break;
        }
        _library.SaveContext();
    }

    public bool IsAutoCachePlayedItems => _managedObject.AutoCachePlayedItemSetting == 1;

    public void SetAutoCachePlayedItems(bool newValue)
    {
        _managedObject.AutoCachePlayedItemSetting = newValue ? 1 : 0;
        _library.SaveContext();
    }

    public bool IsPopupBarAllowedToHide =>
        _podcastPlaylist.SongCount == 0 && _contextPlaylist.SongCount == 0 && _userQueuePlaylistInternal.SongCount == 0;

    public int MusicItemCount => _contextPlaylist.SongCount + _userQueuePlaylistInternal.SongCount;

    public int PodcastItemCount => _podcastPlaylist.SongCount;

    public PlayerMode PlayerMode => Enum.IsDefined(_managedObject.PlayerMode) ? _managedObject.PlayerMode : PlayerMode.Music;

    public void SetPlayerMode(PlayerMode newValue)
    {
        _managedObject.PlayerMode = newValue;
        _library.SaveContext();
    }

    public bool IsShuffle => PlayerMode switch
    {
        PlayerMode.Music => _managedObject.ShuffleSetting == 1,
        _ => false,
    };

    public void SetShuffle(bool newValue)
    {
        if (newValue)
        {
            _shuffledContextPlaylist.Shuffle();
            if (CurrentItem is { } curPlayable &&
                _shuffledContextPlaylist.GetFirstIndex(curPlayable) is { } indexOfCurrentItemInShuffledPlaylist)
            {
                _shuffledContextPlaylist.MovePlaylistItem(indexOfCurrentItemInShuffledPlaylist, 0);
                SetCurrentIndex(0);
            }
        }
        else
        {
            if (CurrentItem is { } curPlayable &&
                _contextPlaylist.GetFirstIndex(curPlayable) is { } indexOfCurrentItemInNormalPlaylist)
            {
                SetCurrentIndex(indexOfCurrentItemInNormalPlaylist);
            }
        }
        _managedObject.ShuffleSetting = newValue ? 1 : 0;
        _library.SaveContext();
    }

    public RepeatMode RepeatMode => PlayerMode switch
    {
        PlayerMode.Music => Enum.IsDefined(_managedObject.RepeatSetting) ? _managedObject.RepeatSetting : RepeatMode.Off,
        _ => RepeatMode.Off,
    };

    public void SetRepeatMode(RepeatMode newValue)
    {
        _managedObject.RepeatSetting = newValue;
        _library.SaveContext();
    }

    public PlaybackRate PlaybackRate => PlayerMode switch
    {
        PlayerMode.Music => PlaybackRateExtensions.Create(_managedObject.MusicPlaybackRate),
        _ => PlaybackRateExtensions.Create(_managedObject.PodcastPlaybackRate),
    };

    public void SetPlaybackRate(PlaybackRate newValue)
    {
        switch (PlayerMode)
        {
            case PlayerMode.Music:
                _managedObject.MusicPlaybackRate = newValue.AsDouble();
                break;
            case PlayerMode.Podcast:
                _managedObject.PodcastPlaybackRate = newValue.AsDouble();
                break;
        }
        _library.SaveContext();
    }

    public PlaybackRate MusicPlaybackRate => PlaybackRateExtensions.Create(_managedObject.MusicPlaybackRate);

    public void SetMusicPlaybackRate(PlaybackRate newValue)
    {
        _managedObject.MusicPlaybackRate = newValue.AsDouble();
        _library.SaveContext();
    }

    public PlaybackRate PodcastPlaybackRate => PlaybackRateExtensions.Create(_managedObject.PodcastPlaybackRate);

    public void SetPodcastPlaybackRate(PlaybackRate newValue)
    {
        _managedObject.PodcastPlaybackRate = newValue.AsDouble();
        _library.SaveContext();
    }

    // --- PlayerQueuesPersistent ---------------------------------------------------------------

    public bool IsUserQueuePlaying => PlayerMode switch
    {
        PlayerMode.Music => IsUserQueuePlayingInternal,
        _ => false,
    };

    public void SetUserQueuePlaying(bool newValue)
    {
        switch (PlayerMode)
        {
            case PlayerMode.Music:
                SetUserQueuePlayingInternal(newValue);
                break;
            case PlayerMode.Podcast:
                break;
        }
    }

    private bool IsUserQueuePlayingInternal => _managedObject.IsUserQueuePlaying;

    private void SetUserQueuePlayingInternal(bool newValue)
    {
        _managedObject.IsUserQueuePlaying = newValue;
        _library.SaveContext();
    }

    public bool IsUserQueueVisible => PlayerMode switch
    {
        PlayerMode.Music => _userQueuePlaylistInternal.SongCount > 0 &&
                            !(IsUserQueuePlayingInternal && _userQueuePlaylistInternal.SongCount == 1),
        _ => false,
    };

    public Playlist ActiveMusicQueue => !IsShuffle ? _contextPlaylist : _shuffledContextPlaylist;

    public Playlist ActiveQueue => PlayerMode switch
    {
        PlayerMode.Music => ActiveMusicQueue,
        _ => _podcastPlaylist,
    };

    public Playlist InactiveQueue => PlayerMode switch
    {
        PlayerMode.Music => !IsShuffle ? _shuffledContextPlaylist : _contextPlaylist,
        _ => _podcastPlaylist,
    };

    public Playlist ContextQueue => _contextPlaylist;
    public Playlist PodcastQueue => _podcastPlaylist;

    public string ContextName => PlayerMode switch
    {
        PlayerMode.Music => _contextPlaylist.Name,
        _ => "Podcasts",
    };

    public void SetContextName(string newValue) => _contextPlaylist.Name = newValue;

    public int CurrentIndex => PlayerMode switch
    {
        PlayerMode.Music => CurrentMusicIndex,
        _ => CurrentPodcastIndex,
    };

    public void SetCurrentIndex(int newValue)
    {
        switch (PlayerMode)
        {
            case PlayerMode.Music:
                CurrentMusicIndex = newValue;
                break;
            case PlayerMode.Podcast:
                CurrentPodcastIndex = newValue;
                break;
        }
        _library.SaveContext();
    }

    private int CurrentMusicIndex
    {
        get
        {
            if (_managedObject.MusicIndex < 0 && !IsUserQueuePlayingInternal) return 0;
            if (_managedObject.MusicIndex >= ContextQueue.SongCount || _managedObject.MusicIndex < -1) return 0;
            return _managedObject.MusicIndex;
        }
        set
        {
            if (value >= -1 && value < ContextQueue.SongCount) _managedObject.MusicIndex = value;
            else _managedObject.MusicIndex = IsUserQueuePlayingInternal ? -1 : 0;
        }
    }

    private int CurrentPodcastIndex
    {
        get
        {
            if (_managedObject.PodcastIndex < 0 ||
                (_managedObject.PodcastIndex >= _podcastPlaylist.SongCount && _podcastPlaylist.SongCount > 0))
                return 0;
            return _managedObject.PodcastIndex;
        }
        set
        {
            if (value >= 0 && value < _podcastPlaylist.SongCount) _managedObject.PodcastIndex = value;
            else _managedObject.PodcastIndex = 0;
        }
    }

    public AbstractPlayable? CurrentItem => PlayerMode switch
    {
        PlayerMode.Music => GetCurrentMusicPlayable(ActiveQueue),
        _ => _podcastPlaylist.GetPlayable(CurrentPodcastIndex),
    };

    public AbstractPlayable? CurrentMusicItem => GetCurrentMusicPlayable(ActiveMusicQueue);

    public AbstractPlayable? CurrentPodcastItem => _podcastPlaylist.GetPlayable(CurrentPodcastIndex);

    private AbstractPlayable? GetCurrentMusicPlayable(Playlist queue)
    {
        if (IsUserQueuePlayingInternal && _userQueuePlaylistInternal.SongCount > 0)
            return _userQueuePlaylistInternal.GetPlayable(0);
        if (queue.SongCount <= 0) return null;
        var index = CurrentMusicIndex;
        if (index < 0 || index >= queue.SongCount) return queue.GetPlayable(0);
        return queue.GetPlayable(index);
    }

    public Playlist UserQueuePlaylist => _userQueuePlaylistInternal;

    public void InsertActiveQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        switch (PlayerMode)
        {
            case PlayerMode.Music: InsertContextQueue(playables); break;
            case PlayerMode.Podcast: InsertPodcastQueue(playables); break;
        }
    }

    public void AppendActiveQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        switch (PlayerMode)
        {
            case PlayerMode.Music: AppendContextQueue(playables); break;
            case PlayerMode.Podcast: AppendPodcastQueue(playables); break;
        }
    }

    public void InsertContextQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        var targetIndex = CurrentMusicIndex + 1;
        if (_contextPlaylist.SongCount == 0)
        {
            if (IsUserQueuePlayingInternal) CurrentMusicIndex = -1;
            targetIndex = 0;
        }
        _contextPlaylist.Insert(playables, targetIndex);
        _shuffledContextPlaylist.Insert(playables, targetIndex);
        _library.SaveContext();
    }

    public void AppendContextQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        _contextPlaylist.Append(playables);
        _shuffledContextPlaylist.Append(playables);
        _library.SaveContext();
    }

    public void InsertUserQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        var targetIndex = IsUserQueuePlayingInternal && _userQueuePlaylistInternal.SongCount > 0 ? 1 : 0;
        _userQueuePlaylistInternal.Insert(playables, targetIndex);
        _library.SaveContext();
    }

    public void AppendUserQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        _userQueuePlaylistInternal.Append(playables);
        _library.SaveContext();
    }

    public void InsertPodcastQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        var targetIndex = CurrentPodcastIndex + 1;
        if (_podcastPlaylist.SongCount == 0) targetIndex = 0;
        _podcastPlaylist.Insert(playables, targetIndex);
        _library.SaveContext();
    }

    public void AppendPodcastQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        _podcastPlaylist.Append(playables);
        _library.SaveContext();
    }

    public void ClearActiveQueue()
    {
        switch (PlayerMode)
        {
            case PlayerMode.Music:
                ClearContextQueue();
                break;
            case PlayerMode.Podcast:
                _podcastPlaylist.RemoveAllItems();
                SetCurrentIndex(0);
                break;
        }
    }

    public void ClearContextQueue()
    {
        SetContextName("");
        _contextPlaylist.RemoveAllItems();
        _shuffledContextPlaylist.RemoveAllItems();
        if (_userQueuePlaylistInternal.SongCount > 0)
        {
            SetUserQueuePlayingInternal(true);
            CurrentMusicIndex = -1;
        }
        else
        {
            CurrentMusicIndex = 0;
        }
        _library.SaveContext();
    }

    public void ClearUserQueue()
    {
        _userQueuePlaylistInternal.RemoveAllItems();
        _library.SaveContext();
    }

    public void RemoveAllItems()
    {
        SetCurrentIndex(0);
        SetUserQueuePlayingInternal(false);
        _contextPlaylist.RemoveAllItems();
        _shuffledContextPlaylist.RemoveAllItems();
        _userQueuePlaylistInternal.RemoveAllItems();
        _podcastPlaylist.RemoveAllItems();
        _library.SaveContext();
    }
}
