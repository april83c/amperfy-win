using Amperfy.Core.Common;
using Amperfy.Core.Player;

namespace Amperfy.App.Services.Audio;

/// The app's <see cref="IAudioStreamingPlayer"/>: plays with <see cref="MediaPlayerAudioEngine"/> (default: follows the
/// default audio device, radio/ICY support, pitch preserving rate) and switches to <see cref="AudioGraphAudioEngine"/>
/// for entries started while the equalizer is active (10 band EQ, replay gain boost &gt; 1).
///
/// The engine is chosen per <see cref="Play"/>; <see cref="Queue"/> uses the active engine (gapless). Turning the EQ
/// on therefore applies from the next started track; changing the EQ gains or turning it off applies immediately
/// while the audio graph plays. Events of the inactive engine are dropped.
public sealed class WindowsAudioEngine : IAudioStreamingPlayer
{
    private const string Log = "AudioEngine";
    private const float EqGainThreshold = 0.05f;

    private readonly Func<string, bool>? _isLiveStream;
    private MediaPlayerAudioEngine? _mediaPlayerEngine;
    private AudioGraphAudioEngine? _audioGraphEngine;
    private IAudioStreamingPlayer? _active;
    private float[] _eqGains = [];
    private bool _isEqEnabled;
    private float _rate = 1.0f;
    private float _volume = 1.0f;
    private float _replayGain = 1.0f;
    private bool _isDisposed;

    public WindowsAudioEngine(Func<string, bool>? isLiveStream = null)
    {
        _isLiveStream = isLiveStream;
    }

    public event Action<string>? DidStartPlaying;
    public event Action<string>? DidFinishPlaying;
    public event Action<Exception>? UnexpectedError;
    public event Action? DidCancel;
    public event Action<IReadOnlyDictionary<string, string>>? DidReadMetadata;

    /// True if the equalizer changes the sound (enabled and at least one band gain != 0).
    public bool IsEqualizerActive => _isEqEnabled && _eqGains.Any(g => Math.Abs(g) > EqGainThreshold);

    /// The engine used for the current entry (diagnostics).
    public string ActiveEngineName => _active switch
    {
        AudioGraphAudioEngine => "AudioGraph",
        MediaPlayerAudioEngine => "MediaPlayer",
        _ => "-",
    };

    private MediaPlayerAudioEngine MediaPlayerEngine
    {
        get
        {
            if (_mediaPlayerEngine is not null) return _mediaPlayerEngine;
            var engine = new MediaPlayerAudioEngine(_isLiveStream);
            Subscribe(engine);
            _mediaPlayerEngine = engine;
            Configure(engine);
            return engine;
        }
    }

    private AudioGraphAudioEngine AudioGraphEngine
    {
        get
        {
            if (_audioGraphEngine is not null) return _audioGraphEngine;
            var engine = new AudioGraphAudioEngine();
            Subscribe(engine);
            engine.PlaybackUnsupported += (url, mimeType, headers) =>
            {
                if (_isDisposed || !ReferenceEquals(_active, engine)) return;
                AmperfyLog.Info(Log, "Audio graph can't play the entry: using MediaPlayer");
                _active = MediaPlayerEngine;
                _active.Play(url, mimeType, headers);
            };
            _audioGraphEngine = engine;
            Configure(engine);
            return engine;
        }
    }

    private void Configure(IAudioStreamingPlayer engine)
    {
        engine.Volume = _volume;
        engine.ReplayGainVolume = _replayGain;
        engine.Rate = _rate;
        engine.SetEqualizer(_eqGains, _isEqEnabled);
    }

    private void Subscribe(IAudioStreamingPlayer engine)
    {
        engine.DidStartPlaying += url => { if (IsActive(engine)) DidStartPlaying?.Invoke(url); };
        engine.DidFinishPlaying += url => { if (IsActive(engine)) DidFinishPlaying?.Invoke(url); };
        engine.UnexpectedError += error => { if (IsActive(engine)) UnexpectedError?.Invoke(error); };
        engine.DidCancel += () => { if (IsActive(engine)) DidCancel?.Invoke(); };
        engine.DidReadMetadata += metadata => { if (IsActive(engine)) DidReadMetadata?.Invoke(metadata); };
    }

    private bool IsActive(IAudioStreamingPlayer engine) => !_isDisposed && ReferenceEquals(_active, engine);

    private bool IsLiveStream(string url, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is not null || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
        try { return _isLiveStream?.Invoke(url) ?? false; }
        catch (Exception) { return false; }
    }

    public void Play(string url, string? mimeType, IReadOnlyDictionary<string, string>? httpHeaders)
    {
        if (_isDisposed) return;
        var useGraph = IsEqualizerActive && AudioGraphAudioEngine.IsAvailable && !IsLiveStream(url, httpHeaders);
        IAudioStreamingPlayer target = useGraph ? AudioGraphEngine : MediaPlayerEngine;
        if (_active is not null && !ReferenceEquals(_active, target)) _active.Stop();
        _active = target;
        target.Play(url, mimeType, httpHeaders);
    }

    public void Queue(string url, string? mimeType, IReadOnlyDictionary<string, string>? httpHeaders)
    {
        if (_isDisposed) return;
        if (_active is null)
        {
            Play(url, mimeType, httpHeaders);
            return;
        }
        _active.Queue(url, mimeType, httpHeaders);
    }

    public void Pause() => _active?.Pause();

    public void Resume() => _active?.Resume();

    public void Stop() => _active?.Stop();

    public void Seek(double seconds) => _active?.Seek(seconds);

    public float Rate
    {
        get => _rate;
        set
        {
            _rate = value;
            if (_mediaPlayerEngine is not null) _mediaPlayerEngine.Rate = value;
            if (_audioGraphEngine is not null) _audioGraphEngine.Rate = value;
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = value;
            if (_mediaPlayerEngine is not null) _mediaPlayerEngine.Volume = value;
            if (_audioGraphEngine is not null) _audioGraphEngine.Volume = value;
        }
    }

    public float ReplayGainVolume
    {
        get => _replayGain;
        set
        {
            _replayGain = value;
            if (_mediaPlayerEngine is not null) _mediaPlayerEngine.ReplayGainVolume = value;
            if (_audioGraphEngine is not null) _audioGraphEngine.ReplayGainVolume = value;
        }
    }

    public void SetEqualizer(float[] gains, bool enabled)
    {
        _eqGains = gains.ToArray();
        _isEqEnabled = enabled;
        _audioGraphEngine?.SetEqualizer(_eqGains, enabled);
    }

    public double Progress => _active?.Progress ?? 0;

    public double Duration => _active?.Duration ?? 0;

    public AudioStreamingPlayerState State => _isDisposed
        ? AudioStreamingPlayerState.Disposed
        : _active?.State ?? AudioStreamingPlayerState.Ready;

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _active = null;
        _mediaPlayerEngine?.Dispose();
        _audioGraphEngine?.Dispose();
        _mediaPlayerEngine = null;
        _audioGraphEngine = null;
    }
}
