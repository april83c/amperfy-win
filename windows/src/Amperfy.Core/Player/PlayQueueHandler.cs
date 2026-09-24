namespace Amperfy.Core.Player;

/// Port of PlayQueueHandler.swift: maps the persistent queues onto the three visible queues
/// (previous / user ("next in queue") / next).
public sealed class PlayQueueHandler
{
    private readonly IPlayerQueuesPersistent _playerQueues;

    public PlayQueueHandler(IPlayerQueuesPersistent playerData)
    {
        _playerQueues = playerData;
    }

    public AbstractPlayable? CurrentlyPlaying => _playerQueues.CurrentItem;

    public AbstractPlayable? CurrentMusicItem => _playerQueues.CurrentMusicItem;

    public AbstractPlayable? CurrentPodcastItem => _playerQueues.CurrentPodcastItem;

    public int TotalPlayDuration => ActiveQueue.Duration + UserQueuePlaylist.Duration;

    public int RemainingPlayDuration => GetNextQueueItems(0, null).Sum(p => p.Duration) + UserQueuePlaylist.Duration;

    public int PrevQueueCount
    {
        get
        {
            var count = 0;
            if (IsUserQueuePlaying && CurrentIndex == -1)
            {
                count = 0; // prev is empty
            }
            else if (IsUserQueuePlaying && CurrentIndex == 0 && ActiveQueue.SongCount > 0)
            {
                count = 1;
            }
            else if (CurrentIndex > 0)
            {
                count = IsUserQueuePlaying ? CurrentIndex + 1 : CurrentIndex;
            }
            return count;
        }
    }

    public AbstractPlayable? GetPrevQueueItem(int at)
    {
        var count = PrevQueueCount;
        if (count <= 0 || at >= count || at < 0) return null;
        return ActiveQueue.GetPlayable(at);
    }

    public List<AbstractPlayable> GetPrevQueueItems(int from, int? to)
    {
        var count = PrevQueueCount;
        if (count <= 0) return [];
        var end = to ?? count - 1;
        if (!(from >= 0 && end >= 0 && from <= end && end < count)) return [];
        return ActiveQueue.GetPlayables(from, end);
    }

    public List<AbstractPlayable> GetAllPrevQueueItems()
    {
        var count = PrevQueueCount;
        if (count <= 0) return [];
        return ActiveQueue.GetPlayables(0, count - 1);
    }

    public int UserQueueCount
    {
        get
        {
            var userQueue = 0;
            if (IsUserQueueVisible)
            {
                userQueue = IsUserQueuePlaying ? UserQueuePlaylist.SongCount - 1 : UserQueuePlaylist.SongCount;
            }
            return userQueue;
        }
    }

    public AbstractPlayable? GetUserQueueItem(int at)
    {
        if (!IsUserQueueVisible) return null;
        return IsUserQueuePlaying ? UserQueuePlaylist.GetPlayable(at + 1) : UserQueuePlaylist.GetPlayable(at);
    }

    public List<AbstractPlayable> GetUserQueueItems(int from, int? to)
    {
        var count = UserQueueCount;
        if (count <= 0) return [];
        var end = to ?? count - 1;
        if (!(from >= 0 && end >= 0 && from <= end && end < count)) return [];

        var userQueue = new List<AbstractPlayable>();
        if (IsUserQueueVisible)
        {
            userQueue = IsUserQueuePlaying
                ? UserQueuePlaylist.GetPlayables(from + 1, end + 1)
                : UserQueuePlaylist.GetPlayables(from, end);
        }
        return userQueue;
    }

    public List<AbstractPlayable> GetAllUserQueueItems()
    {
        var userQueue = new List<AbstractPlayable>();
        if (IsUserQueueVisible)
        {
            userQueue = IsUserQueuePlaying ? UserQueuePlaylist.GetPlayables(1) : UserQueuePlaylist.GetPlayables(0);
        }
        return userQueue;
    }

    public int NextQueueCount
    {
        get
        {
            if (ActiveQueue.SongCount > 0 && CurrentIndex < ActiveQueue.SongCount - 1)
                return ActiveQueue.SongCount - CurrentIndex - 1;
            return 0;
        }
    }

    public AbstractPlayable? GetNextQueueItem(int at)
    {
        var count = NextQueueCount;
        if (count > 0 && at < count) return ActiveQueue.GetPlayable(at + CurrentIndex + 1);
        return null;
    }

    public List<AbstractPlayable> GetNextQueueItems(int from, int? to)
    {
        var count = NextQueueCount;
        if (count <= 0) return [];
        var end = to ?? count - 1;
        if (!(from >= 0 && end >= 0 && from <= end && end < count)) return [];
        var offset = CurrentIndex + 1;
        return ActiveQueue.GetPlayables(from + offset, end + offset);
    }

    public List<AbstractPlayable> GetAllNextQueueItems()
    {
        var count = NextQueueCount;
        if (count > 0) return ActiveQueue.GetPlayables(CurrentIndex + 1);
        return [];
    }

    public string ContextName => _playerQueues.ContextName;

    public void SetContextName(string newValue) => _playerQueues.SetContextName(newValue);

    public bool IsUserQueuePlaying => _playerQueues.IsUserQueuePlaying;

    public void InsertActiveQueue(IReadOnlyList<AbstractPlayable> playables) => _playerQueues.InsertActiveQueue(playables);

    public void AppendActiveQueue(IReadOnlyList<AbstractPlayable> playables) => _playerQueues.AppendActiveQueue(playables);

    public void InsertContextQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        _playerQueues.SetContextName("");
        _playerQueues.InsertContextQueue(playables);
    }

    public void AppendContextQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        _playerQueues.SetContextName("");
        _playerQueues.AppendContextQueue(playables);
    }

    public void InsertUserQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        _playerQueues.InsertUserQueue(playables);
        if (_playerQueues.ContextQueue.SongCount == 0) _playerQueues.SetUserQueuePlaying(true);
    }

    public void AppendUserQueue(IReadOnlyList<AbstractPlayable> playables)
    {
        _playerQueues.AppendUserQueue(playables);
        if (_playerQueues.ContextQueue.SongCount == 0) _playerQueues.SetUserQueuePlaying(true);
    }

    public void InsertPodcastQueue(IReadOnlyList<AbstractPlayable> playables) => _playerQueues.InsertPodcastQueue(playables);

    public void AppendPodcastQueue(IReadOnlyList<AbstractPlayable> playables) => _playerQueues.AppendPodcastQueue(playables);

    public void ClearActiveQueue() => _playerQueues.ClearActiveQueue();

    public void ClearContextQueue()
    {
        _playerQueues.SetContextName("");
        _playerQueues.ClearContextQueue();
    }

    public void ClearUserQueue()
    {
        if (IsUserQueuePlaying && CurrentlyPlaying is { } currentUserQueueItem)
        {
            _playerQueues.ClearUserQueue();
            InsertUserQueue([currentUserQueueItem]);
        }
        else
        {
            _playerQueues.ClearUserQueue();
        }
    }

    public void RemoveAllItems()
    {
        _playerQueues.SetContextName("");
        _playerQueues.RemoveAllItems();
    }

    public AbstractPlayable? MarkAndGetPlayableAsPlaying(PlayerIndex playerIndex)
    {
        AbstractPlayable? playable = null;
        if (playerIndex.QueueType == PlayerQueueType.User && playerIndex.Index >= 0 && playerIndex.Index < UserQueueCount)
        {
            playable = GetUserQueueItem(playerIndex.Index);
            if (IsUserQueuePlaying) RemoveItemFromUserQueue(0);
            for (var i = 1; i <= playerIndex.Index; i++) RemoveItemFromUserQueue(0);
            _playerQueues.SetUserQueuePlaying(true);
        }
        else if (playerIndex.QueueType == PlayerQueueType.Prev && playerIndex.Index >= 0 && playerIndex.Index < PrevQueueCount)
        {
            if (IsUserQueuePlaying) RemoveItemFromUserQueue(0);
            playable = GetPrevQueueItem(playerIndex.Index);
            SetCurrentIndex(playerIndex.Index);
            _playerQueues.SetUserQueuePlaying(false);
        }
        else if (playerIndex.QueueType == PlayerQueueType.Next && playerIndex.Index >= 0 && playerIndex.Index < NextQueueCount)
        {
            if (IsUserQueuePlaying) RemoveItemFromUserQueue(0);
            playable = GetNextQueueItem(playerIndex.Index);
            if (IsUserQueuePlaying) SetCurrentIndex(PrevQueueCount + playerIndex.Index);
            else SetCurrentIndex(PrevQueueCount + 1 + playerIndex.Index);
            _playerQueues.SetUserQueuePlaying(false);
        }
        return playable;
    }

    public void RemovePlayable(PlayerIndex at)
    {
        switch (at.QueueType)
        {
            case PlayerQueueType.User:
                RemoveItemFromUserQueue(IsUserQueuePlaying ? at.Index + 1 : at.Index);
                break;
            case PlayerQueueType.Prev:
                RemoveItemFromActiveQueue(at.Index);
                break;
            case PlayerQueueType.Next:
                var playlistIndex = PrevQueueCount + at.Index;
                if (!IsUserQueuePlaying) playlistIndex += 1;
                RemoveItemFromActiveQueue(playlistIndex);
                break;
        }
    }

    public void MovePlayable(PlayerIndex from, PlayerIndex to)
    {
        var userQueueOffsetIsUserQueuePlaying = IsUserQueuePlaying ? 1 : 0;
        var nextQueueOffsetIsUserQueuePlaying = IsUserQueuePlaying ? 0 : 1;
        var offsetToNext = PrevQueueCount + nextQueueOffsetIsUserQueuePlaying;

        if (from.Index < 0 || to.Index < 0) return;

        if (from.QueueType == PlayerQueueType.Prev && from.Index >= PrevQueueCount) return;
        if (from.QueueType == PlayerQueueType.User && from.Index >= UserQueueCount) return;
        if (from.QueueType == PlayerQueueType.Next && from.Index >= NextQueueCount) return;

        if (to.QueueType == PlayerQueueType.Prev && to.Index > PrevQueueCount) return;
        if (to.QueueType == PlayerQueueType.User && to.Index > UserQueueCount) return;
        if (to.QueueType == PlayerQueueType.Next && to.Index > NextQueueCount) return;

        if (from.QueueType == PlayerQueueType.Prev && to.QueueType == PlayerQueueType.Prev)
        {
            // Prev <=> Prev
            MoveContextItem(from.Index, to.Index);
        }
        else if (from.QueueType == PlayerQueueType.Next && to.QueueType == PlayerQueueType.Next)
        {
            // Next <=> Next
            MoveContextItem(offsetToNext + from.Index, offsetToNext + to.Index);
        }
        else if (from.QueueType == PlayerQueueType.User && to.QueueType == PlayerQueueType.User)
        {
            // User <=> User
            MoveUserQueueItem(from.Index + userQueueOffsetIsUserQueuePlaying, to.Index + userQueueOffsetIsUserQueuePlaying);
        }
        else if (from.QueueType == PlayerQueueType.Prev && to.QueueType == PlayerQueueType.Next)
        {
            // Prev ==> Next
            if (!IsUserQueuePlaying)
            {
                MoveContextItem(from.Index, offsetToNext + to.Index - 1);
            }
            else if (from.Index == CurrentIndex && to.Index == 0)
            {
                SetCurrentIndex(CurrentIndex - 1);
            }
            else
            {
                MoveContextItem(from.Index, offsetToNext + to.Index - 1);
                SetCurrentIndex(CurrentIndex - 1);
            }
        }
        else if (from.QueueType == PlayerQueueType.Next && to.QueueType == PlayerQueueType.Prev)
        {
            // Next ==> Prev
            if (!IsUserQueuePlaying)
            {
                MoveContextItem(offsetToNext + from.Index, to.Index);
            }
            else if (from.Index == 0 && to.Index == CurrentIndex + 1)
            {
                SetCurrentIndex(CurrentIndex + 1);
            }
            else
            {
                MoveContextItem(offsetToNext + from.Index, to.Index);
                SetCurrentIndex(CurrentIndex + 1);
            }
        }
        else if (from.QueueType == PlayerQueueType.User && to.QueueType == PlayerQueueType.Next)
        {
            // User ==> Next
            _playerQueues.AppendContextQueue([GetUserQueueItem(from.Index)!]);
            var fromIndex = ActiveQueue.SongCount - 1;
            MoveContextItem(fromIndex, offsetToNext + to.Index);
            RemoveItemFromUserQueue(from.Index + userQueueOffsetIsUserQueuePlaying);
        }
        else if (from.QueueType == PlayerQueueType.User && to.QueueType == PlayerQueueType.Prev)
        {
            // User ==> Prev
            _playerQueues.AppendContextQueue([GetUserQueueItem(from.Index)!]);
            var fromIndex = ActiveQueue.SongCount - 1;
            MoveContextItem(fromIndex, to.Index);
            if (IsUserQueuePlaying) SetCurrentIndex(CurrentIndex + 1);
            RemoveItemFromUserQueue(from.Index + userQueueOffsetIsUserQueuePlaying);
        }
        else if (from.QueueType == PlayerQueueType.Prev && to.QueueType == PlayerQueueType.User)
        {
            // Prev ==> User
            _playerQueues.AppendUserQueue([GetPrevQueueItem(from.Index)!]);
            MoveUserQueueItem(UserQueuePlaylist.SongCount - 1, to.Index + userQueueOffsetIsUserQueuePlaying);
            RemoveItemFromActiveQueue(from.Index);
        }
        else if (from.QueueType == PlayerQueueType.Next && to.QueueType == PlayerQueueType.User)
        {
            // Next ==> User
            _playerQueues.AppendUserQueue([GetNextQueueItem(from.Index)!]);
            MoveUserQueueItem(UserQueuePlaylist.SongCount - 1, to.Index + userQueueOffsetIsUserQueuePlaying);
            RemoveItemFromActiveQueue(offsetToNext + from.Index);
        }
    }

    public AbstractPlayable? GetPlayable(PlayerIndex playerIndex) => playerIndex.QueueType switch
    {
        PlayerQueueType.Prev => GetPrevQueueItem(playerIndex.Index),
        PlayerQueueType.User => GetUserQueueItem(playerIndex.Index),
        _ => GetNextQueueItem(playerIndex.Index),
    };

    /// returns true if the player has been reseted
    public bool Logout(Account account)
    {
        // if one playlist contain an element from the logout account -> reset complete player
        if (_playerQueues.ContextQueue.PlayablesList.Any(p => ReferenceEquals(p.Account, account)) ||
            _playerQueues.UserQueuePlaylist.PlayablesList.Any(p => ReferenceEquals(p.Account, account)) ||
            _playerQueues.PodcastQueue.PlayablesList.Any(p => ReferenceEquals(p.Account, account)))
        {
            RemoveAllItems();
            return true;
        }
        return false;
    }

    private int CurrentIndex => _playerQueues.CurrentIndex;

    public void SetCurrentIndex(int newValue) => _playerQueues.SetCurrentIndex(newValue);

    private Playlist ActiveQueue => _playerQueues.ActiveQueue;

    private Playlist UserQueuePlaylist => _playerQueues.UserQueuePlaylist;

    private bool IsUserQueueVisible => _playerQueues.IsUserQueueVisible;

    private void RemoveItemFromUserQueue(int index)
    {
        if (index >= UserQueuePlaylist.SongCount) return;
        UserQueuePlaylist.Remove(index);
        _playerQueues.SaveContext();
    }

    private void RemoveItemFromActiveQueue(int index)
    {
        if (index >= ActiveQueue.SongCount) return;
        var playableToRemove = ActiveQueue.GetPlayable(index)!;
        if (index < CurrentIndex) SetCurrentIndex(CurrentIndex - 1);
        else if (IsUserQueuePlaying && index == CurrentIndex) SetCurrentIndex(CurrentIndex - 1);
        ActiveQueue.Remove(index);
        _playerQueues.InactiveQueue.RemoveFirstOccurrence(playableToRemove);
        _playerQueues.SaveContext();
    }

    private void MoveContextItem(int fromIndex, int to)
    {
        if (fromIndex >= ActiveQueue.SongCount || to >= ActiveQueue.SongCount || fromIndex == to) return;
        ActiveQueue.MovePlaylistItem(fromIndex, to);
        _playerQueues.SaveContext();
        if (IsUserQueuePlaying) return;
        if (CurrentIndex == fromIndex) SetCurrentIndex(to);
        else if (fromIndex < CurrentIndex && CurrentIndex <= to) SetCurrentIndex(CurrentIndex - 1);
        else if (to <= CurrentIndex && CurrentIndex < fromIndex) SetCurrentIndex(CurrentIndex + 1);
    }

    private void MoveUserQueueItem(int fromIndex, int to)
    {
        if (fromIndex >= UserQueuePlaylist.SongCount || to >= UserQueuePlaylist.SongCount || fromIndex == to) return;
        UserQueuePlaylist.MovePlaylistItem(fromIndex, to);
        _playerQueues.SaveContext();
    }
}
