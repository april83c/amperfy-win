using Amperfy.Core.Player;

namespace Amperfy.App.Services.Audio;

/// Creates the platform audio engine and system media controls for the player.
public static class AudioBackend
{
    public static IAudioStreamingPlayer CreateEngine() => new NullAudioStreamingPlayer();

    public static ISystemMediaControls? CreateSystemMediaControls() => null;
}

/// Placeholder engine (plays nothing) until the MediaPlayer based engine exists.
internal sealed class NullAudioStreamingPlayer : IAudioStreamingPlayer
{
    public void Play(string url, string? mimeType, IReadOnlyDictionary<string, string>? httpHeaders) => State = AudioStreamingPlayerState.Stopped;
    public void Queue(string url, string? mimeType, IReadOnlyDictionary<string, string>? httpHeaders) { }
    public void Pause() { }
    public void Resume() { }
    public void Stop() => State = AudioStreamingPlayerState.Stopped;
    public void Seek(double seconds) { }
    public float Rate { get; set; } = 1;
    public float Volume { get; set; } = 1;
    public double Progress => 0;
    public double Duration => 0;
    public AudioStreamingPlayerState State { get; private set; } = AudioStreamingPlayerState.Ready;
    public void SetEqualizer(float[] gains, bool enabled) { }
    public float ReplayGainVolume { get; set; } = 1;
#pragma warning disable CS0067
    public event Action<string>? DidStartPlaying;
    public event Action<string>? DidFinishPlaying;
    public event Action<Exception>? UnexpectedError;
    public event Action? DidCancel;
    public event Action<IReadOnlyDictionary<string, string>>? DidReadMetadata;
#pragma warning restore CS0067
    public void Dispose() { }
}
