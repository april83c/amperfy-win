using Amperfy.Core.Common;
using Amperfy.Core.Model;
using Amperfy.Core.Player;

namespace Amperfy.App.Services.Audio;

/// Creates the platform audio engine and system media controls for the player (see WindowsAudioEngine,
/// MediaPlayerAudioEngine, AudioGraphAudioEngine and SystemMediaControls) and releases them on shutdown.
public static class AudioBackend
{
    private const string Log = "AudioBackend";
    private static readonly List<WeakReference<IAudioStreamingPlayer>> Engines = [];
    private static SystemMediaControls? _systemMediaControls;

    /// Engine factory for the player (BackendAudioPlayer creates a new engine after an error).
    public static IAudioStreamingPlayer CreateEngine()
    {
        var engine = new WindowsAudioEngine(IsLiveStreamUrl);
        Engines.RemoveAll(w => !w.TryGetTarget(out _));
        Engines.Add(new WeakReference<IAudioStreamingPlayer>(engine));
        return engine;
    }

    public static ISystemMediaControls? CreateSystemMediaControls()
    {
        if (_systemMediaControls is not null) return _systemMediaControls;
        try
        {
            _systemMediaControls = new SystemMediaControls();
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(Log, $"System media transport controls not available: {ex.Message}");
            _systemMediaControls = null;
        }
        return _systemMediaControls;
    }

    /// The engine currently used by the player (diagnostics, e.g. "Player Info").
    public static WindowsAudioEngine? CurrentEngine
    {
        get
        {
            for (var i = Engines.Count - 1; i >= 0; i--)
            {
                if (Engines[i].TryGetTarget(out var e) && e is WindowsAudioEngine w && w.State != AudioStreamingPlayerState.Disposed) return w;
            }
            return null;
        }
    }

    /// True if the URL is the stream URL of the radio that is playing / queued next (radio streams are live:
    /// no duration, ICY metadata).
    internal static bool IsLiveStreamUrl(string url)
    {
        var player = AppServicesOrNull()?.PlayerComponents?.Player;
        if (player is null) return false;
        return IsRadioWithUrl(player.CurrentlyPlaying, url) ||
               IsRadioWithUrl(player.GetPlayable(new PlayerIndex(PlayerQueueType.User, 0)), url) ||
               IsRadioWithUrl(player.GetPlayable(new PlayerIndex(PlayerQueueType.Next, 0)), url) ||
               IsRadioWithUrl(player.GetPlayable(new PlayerIndex(PlayerQueueType.Prev, 0)), url);
    }

    private static bool IsRadioWithUrl(AbstractPlayable? playable, string url) =>
        playable?.AsRadio is { } radio && string.Equals(radio.Url?.Trim(), url.Trim(), StringComparison.Ordinal);

    private static AppServices? AppServicesOrNull()
    {
        try { return AppServices.Instance; }
        catch (Exception) { return null; }
    }

    /// Releases the audio engines and the system media controls (app shutdown).
    public static void Shutdown()
    {
        foreach (var weak in Engines)
        {
            if (weak.TryGetTarget(out var engine))
            {
                try { engine.Dispose(); } catch (Exception ex) { AmperfyLog.Warning(Log, $"Engine dispose failed: {ex.Message}"); }
            }
        }
        Engines.Clear();
        _systemMediaControls?.Dispose();
        _systemMediaControls = null;
    }
}
