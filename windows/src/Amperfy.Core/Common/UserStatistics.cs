namespace Amperfy.Core.Common;

/// Usage statistics (the iOS app persisted them for diagnostics; here they are kept in memory
/// and exposed in the support settings).
public sealed class UserStatistics
{
    public int PlayedSongsCount { get; private set; }
    public int PlayedSongFromCacheCount { get; private set; }
    public int PlayedSongViaStreamCount { get; private set; }
    public int ActiveRepeatOffSongsCount { get; private set; }
    public int ActiveRepeatAllSongsCount { get; private set; }
    public int ActiveRepeatSingleSongsCount { get; private set; }
    public int ActiveShuffleOnSongsCount { get; private set; }
    public int ActiveShuffleOffSongsCount { get; private set; }
    public int AppSessionsStartedCount { get; private set; }

    public void SessionStarted() => AppSessionsStartedCount++;

    public void PlayedItem(RepeatMode repeatMode, bool isShuffle)
    {
        switch (repeatMode)
        {
            case RepeatMode.Off: ActiveRepeatOffSongsCount++; break;
            case RepeatMode.All: ActiveRepeatAllSongsCount++; break;
            case RepeatMode.Single: ActiveRepeatSingleSongsCount++; break;
        }
        if (isShuffle) ActiveShuffleOnSongsCount++;
        else ActiveShuffleOffSongsCount++;
    }

    public void PlayedSong(bool isPlayedFromCache)
    {
        PlayedSongsCount++;
        if (isPlayedFromCache) PlayedSongFromCacheCount++;
        else PlayedSongViaStreamCount++;
    }
}
