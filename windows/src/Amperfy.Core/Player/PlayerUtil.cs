namespace Amperfy.Core.Player;

/// Port of the Swift MusicPlayable protocol: observer of the player. All methods are called on the
/// main thread and have empty default implementations (implement only what you need).
public interface IMusicPlayable
{
    void DidStartPlayingFromBeginning() { }
    void DidStartPlaying() { }
    void DidPause() { }
    void DidStopPlaying() { }
    void DidElapsedTimeChange() { }
    /// high refresh count (every 0.1s while playing)
    void DidLyricsTimeChange(TimeSpan time) { }
    void DidPlaylistChange() { }
    void DidArtworkChange() { }
    void DidNowPlayingInfoChange() { }
    void DidShuffleChange() { }
    void DidRepeatChange() { }
    void DidPlaybackRateChange() { }
    void ErrorOccurred(Exception error) { }
}

/// Port of Swift PlayType
public enum PlayType
{
    Stream,
    Cache,
}

/// Port of Swift BackendAudioQueueType
public enum BackendAudioQueueType
{
    Play,
    Queue,
}

/// Port of the Swift BackendAudioPlayerNotifiable protocol (implemented by <see cref="AudioPlayer"/>).
public interface IBackendAudioPlayerNotifiable
{
    void DidElapsedTimeChange();
    void DidLyricsTimeChange(TimeSpan time);
    void Stop();
    void PlayPrevious();
    void PlayNext();
    void DidItemFinishedPlaying();
    void NotifyItemPreparationFinished();
    void NotifyErrorOccurred(Exception error);
    void DidReadStreamMetadata(IReadOnlyDictionary<string, string> metadata);
}
