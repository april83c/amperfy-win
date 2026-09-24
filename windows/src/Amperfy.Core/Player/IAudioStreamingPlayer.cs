namespace Amperfy.Core.Player;

/// Mirrors AudioStreaming.AudioPlayerState (the states BackendAudioPlayer looks at are
/// Playing, Paused, Buffering and Stopped).
public enum AudioStreamingPlayerState
{
    Ready,
    Running,
    Playing,
    Buffering,
    Paused,
    Stopped,
    Error,
    Disposed,
}

/// Abstraction of the audio engine used by <see cref="BackendAudioPlayer"/> (the iOS app uses the
/// third party "AudioStreaming" engine; the Windows app implements it with Windows.Media.Playback.MediaPlayer).
///
/// Contract:
/// * All members are called on the main thread.
/// * An entry is identified by its URL string exactly as passed to <see cref="Play"/> / <see cref="Queue"/>
///   (http(s) stream URL or a file:// URI of a cached file).
/// * All events MUST be raised on the main thread (post them, never raise them synchronously from inside
///   a call into the engine).
/// * <see cref="Queue"/> enqueues an entry that starts automatically (gapless) after the current one
///   finished: the engine then raises <see cref="DidFinishPlaying"/> for the old entry and
///   <see cref="DidStartPlaying"/> for the queued one. <see cref="Play"/> replaces the current entry and clears the queue.
/// * EQ / replay gain: the engine applies the 10 band parametric EQ (<see cref="EqualizerSetting.Frequencies"/>,
///   gains in dB, bandwidth 1 octave) followed by a gain stage with the linear gain <see cref="ReplayGainVolume"/>
///   (can be &gt; 1), and finally the user volume <see cref="Volume"/>.
public interface IAudioStreamingPlayer : IDisposable
{
    /// Starts playing the entry (replacing the current one and the queue).
    void Play(string url, string? mimeType, IReadOnlyDictionary<string, string>? httpHeaders);

    /// Enqueues the entry to be played gapless after the current entry.
    void Queue(string url, string? mimeType, IReadOnlyDictionary<string, string>? httpHeaders);

    void Pause();
    void Resume();
    /// Stops playback and clears the queue.
    void Stop();
    void Seek(double seconds);

    /// Playback rate (1.0 = normal).
    float Rate { get; set; }
    /// User volume 0..1
    float Volume { get; set; }
    /// Elapsed time of the current entry in seconds.
    double Progress { get; }
    /// Duration of the current entry in seconds (0 if unknown).
    double Duration { get; }
    AudioStreamingPlayerState State { get; }

    /// Sets the EQ band gains (dB, one per <see cref="EqualizerSetting.Frequencies"/> entry).
    /// <paramref name="enabled"/> = false means all gains are 0 and the EQ may be bypassed.
    void SetEqualizer(float[] gains, bool enabled);

    /// Linear output gain of the replay gain stage (replay gain * EQ volume compensation).
    float ReplayGainVolume { get; set; }

    /// Entry (url) started playing.
    event Action<string>? DidStartPlaying;
    /// Entry (url) finished playing (end of stream reached or stopped).
    event Action<string>? DidFinishPlaying;
    /// Playback failed unexpectedly.
    event Action<Exception>? UnexpectedError;
    /// Queued entries were cancelled.
    event Action? DidCancel;
    /// Stream metadata (e.g. ICY "StreamTitle" of radio streams) has been read.
    event Action<IReadOnlyDictionary<string, string>>? DidReadMetadata;
}
