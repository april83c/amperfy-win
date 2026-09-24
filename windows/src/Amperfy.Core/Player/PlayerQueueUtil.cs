namespace Amperfy.Core.Player;

/// Queue helpers of the player UI (port of parts of PlayerControlView.createPlayerOptionsMenu).
public static class PlayerQueueUtil
{
    /// Songs of "Add Context Queue to Playlist": previous items, the current song and the next items (songs
    /// only, no user queue). Empty if the songs belong to more than one account (a playlist belongs to one).
    public static List<Song> ContextQueueSongsForPlaylist(
        IEnumerable<AbstractPlayable> prev, AbstractPlayable? current, IEnumerable<AbstractPlayable> next)
    {
        var songs = prev.FilterSongs();
        if (current?.AsSong is { } currentSong) songs.Add(currentSong);
        songs.AddRange(next.FilterSongs());
        if (songs.Count == 0) return songs;
        var account = songs[0].Account;
        return account is not null && songs.All(s => ReferenceEquals(s.Account, account)) ? songs : [];
    }

    public static List<Song> ContextQueueSongsForPlaylist(IPlayerFacade player) =>
        ContextQueueSongsForPlaylist(player.GetAllPrevQueueItems(), player.CurrentlyPlaying, player.GetAllNextQueueItems());
}
