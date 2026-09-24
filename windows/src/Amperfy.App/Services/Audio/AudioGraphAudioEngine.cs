using Amperfy.Core.Common;
using Amperfy.Core.Player;
using Amperfy.Core.Storage;
using Windows.Media.Audio;
using Windows.Media.Render;

namespace Amperfy.App.Services.Audio;

/// <see cref="IAudioStreamingPlayer"/> on Windows.Media.Audio.AudioGraph, used while the equalizer is active.
///
/// Graph: MediaSourceAudioInputNode (OutgoingGain = replay gain, may be &gt; 1) → submix node with three built-in
/// <see cref="EqualizerEffectDefinition"/>s (4 bands each, 10 used: <see cref="EqualizerSetting.Frequencies"/>,
/// bandwidth 1 octave; OutgoingGain = user volume) → device output node.
/// Built-in effects need no WinRT activation registration, so this works in the unpackaged app (custom
/// IBasicAudioEffect implementations would need a registered activatable class).
///
/// Gapless: the queued entry's input node is created in advance (stopped, already connected) and started from the
/// MediaSourceCompleted callback on the audio thread.
/// Limitations (compared to the MediaPlayer path): the graph is bound to the default output device at creation
/// (a device change raises an error and the player restarts the engine); live (radio) streams are not played by
/// this engine; the playback rate is applied with PlaybackSpeedFactor. If the graph or an input node can't be
/// created, <see cref="PlaybackUnsupported"/> asks the owner to play the entry with the MediaPlayer engine.
public sealed class AudioGraphAudioEngine : IAudioStreamingPlayer
{
    private const string Log = "AudioGraphEngine";
    private static readonly TimeSpan NodeCreationTimeout = TimeSpan.FromSeconds(20);
    private const int BandsPerEffect = 4;
    private const double MinBandGain = 0.126; // -18 dB (EqualizerBand limit)
    private const double MaxBandGain = 7.94; // +18 dB

    private static bool _isUnavailable;

    /// False after the audio graph couldn't be created in this session (no audio device, API unavailable, …).
    public static bool IsAvailable => !_isUnavailable;

    private sealed class Entry
    {
        public required int Id { get; init; }
        public required string Url { get; init; }
        public string? MimeType { get; init; }
        public IReadOnlyDictionary<string, string>? Headers { get; init; }
        public AudioSourceHandle? Handle { get; set; }
        public MediaSourceAudioInputNode? Node { get; set; }
        public Exception? Failure { get; set; }
        public bool IsReady { get; set; }
        public bool IsStartReported { get; set; }
        public bool IsFinished { get; set; }
        public bool IsErrorReported { get; set; }
        /// Started by the audio thread (gapless transition).
        public volatile bool IsStartedByAudioThread;
    }

    private AudioGraph? _graph;
    private AudioSubmixNode? _submix;
    private AudioDeviceOutputNode? _output;
    private readonly List<EqualizerEffectDefinition> _equalizers = [];
    private Task<bool>? _graphCreation;
    private bool _isGraphRunning;

    private Entry? _current;
    private Entry? _next;
    private Entry? _gaplessNext; // read by the audio thread
    private int _nextId;
    private int _generation;
    private CancellationTokenSource _cts = new();
    private float _volume = 1.0f;
    private float _replayGain = 1.0f;
    private float _rate = 1.0f;
    private float[] _eqGains = new float[EqualizerSetting.Frequencies.Length];
    private bool _isEqEnabled;
    private AudioStreamingPlayerState _state = AudioStreamingPlayerState.Ready;
    private bool _isDisposed;

    public event Action<string>? DidStartPlaying;
    public event Action<string>? DidFinishPlaying;
    public event Action<Exception>? UnexpectedError;
#pragma warning disable CS0067
    public event Action? DidCancel;
    public event Action<IReadOnlyDictionary<string, string>>? DidReadMetadata;
#pragma warning restore CS0067

    /// The entry can't be played by the audio graph (url, mime type, headers): play it with another engine.
    public event Action<string, string?, IReadOnlyDictionary<string, string>?>? PlaybackUnsupported;

    // --- commands -----------------------------------------------------------------------------

    public void Play(string url, string? mimeType, IReadOnlyDictionary<string, string>? httpHeaders)
    {
        if (_isDisposed) return;
        Teardown();
        var entry = new Entry { Id = ++_nextId, Url = url, MimeType = mimeType, Headers = httpHeaders };
        _current = entry;
        _state = AudioStreamingPlayerState.Buffering;
        _ = PrepareAsync(entry, isFirst: true);
    }

    public void Queue(string url, string? mimeType, IReadOnlyDictionary<string, string>? httpHeaders)
    {
        if (_isDisposed) return;
        if (_current is null)
        {
            Play(url, mimeType, httpHeaders);
            return;
        }
        DisposeEntry(_next);
        var entry = new Entry { Id = ++_nextId, Url = url, MimeType = mimeType, Headers = httpHeaders };
        _next = entry;
        Volatile.Write(ref _gaplessNext, null);
        _ = PrepareAsync(entry, isFirst: false);
    }

    public void Pause()
    {
        if (_isDisposed) return;
        StopGraph();
        if (_current is { IsStartReported: true }) _state = AudioStreamingPlayerState.Paused;
    }

    public void Resume()
    {
        if (_isDisposed || _current is not { IsStartReported: true, IsFinished: false }) return;
        StartGraph();
        _state = AudioStreamingPlayerState.Playing;
    }

    public void Stop()
    {
        if (_isDisposed) return;
        Teardown();
        _state = AudioStreamingPlayerState.Stopped;
    }

    public void Seek(double seconds)
    {
        if (_current?.Node is not { } node) return;
        try
        {
            var duration = node.Duration.TotalSeconds;
            var target = Math.Max(0, seconds);
            if (duration > 0) target = Math.Min(target, Math.Max(0, duration - 0.5));
            node.Seek(TimeSpan.FromSeconds(target));
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(Log, $"Seek failed: {ex.Message}");
        }
    }

    public float Rate
    {
        get => _rate;
        set
        {
            _rate = value <= 0 ? 1.0f : value;
            ApplyNodeSettings(_current);
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0f, 1.0f);
            if (_submix is { } submix) submix.OutgoingGain = _volume;
        }
    }

    public float ReplayGainVolume
    {
        get => _replayGain;
        set
        {
            _replayGain = Math.Max(0, value);
            ApplyNodeSettings(_current);
        }
    }

    public void SetEqualizer(float[] gains, bool enabled)
    {
        _eqGains = gains.ToArray();
        _isEqEnabled = enabled;
        ApplyEqualizer();
    }

    public double Progress
    {
        get
        {
            if (_current is not { IsStartReported: true, Node: { } node }) return 0;
            try { return Math.Max(0, node.Position.TotalSeconds); }
            catch (Exception) { return 0; }
        }
    }

    public double Duration
    {
        get
        {
            if (_current?.Node is not { } node) return 0;
            try
            {
                var seconds = node.Duration.TotalSeconds;
                return double.IsFinite(seconds) && seconds > 0 ? seconds : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }

    public AudioStreamingPlayerState State => _isDisposed ? AudioStreamingPlayerState.Disposed : _state;

    // --- graph --------------------------------------------------------------------------------

    private Task<bool> EnsureGraphAsync() => _graphCreation ??= CreateGraphAsync();

    private async Task<bool> CreateGraphAsync()
    {
        try
        {
            var settings = new AudioGraphSettings(AudioRenderCategory.Media);
            var result = await AudioGraph.CreateAsync(settings);
            if (result.Status != AudioGraphCreationStatus.Success)
            {
                AmperfyLog.Warning(Log, $"AudioGraph creation failed: {result.Status} {result.ExtendedError?.Message}");
                _isUnavailable = true;
                return false;
            }
            var graph = result.Graph;
            var outputResult = await graph.CreateDeviceOutputNodeAsync();
            if (outputResult.Status != AudioDeviceNodeCreationStatus.Success)
            {
                AmperfyLog.Warning(Log, $"Audio output node creation failed: {outputResult.Status} {outputResult.ExtendedError?.Message}");
                graph.Dispose();
                _isUnavailable = true;
                return false;
            }
            if (_isDisposed)
            {
                graph.Dispose();
                return false;
            }
            _graph = graph;
            _output = outputResult.DeviceOutputNode;
            _submix = graph.CreateSubmixNode();
            _submix.AddOutgoingConnection(_output);
            _submix.OutgoingGain = _volume;
            var effectCount = (EqualizerSetting.Frequencies.Length + BandsPerEffect - 1) / BandsPerEffect;
            for (var i = 0; i < effectCount; i++)
            {
                var eq = new EqualizerEffectDefinition(graph);
                _equalizers.Add(eq);
                _submix.EffectDefinitions.Add(eq);
            }
            ApplyEqualizer();
            graph.UnrecoverableErrorOccurred += Graph_UnrecoverableErrorOccurred;
            AmperfyLog.Info(Log, $"AudioGraph created ({graph.EncodingProperties.SampleRate} Hz, {graph.SamplesPerQuantum} samples/quantum)");
            return true;
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(Log, $"AudioGraph not available: {ex.Message}");
            _isUnavailable = true;
            return false;
        }
    }

    /// Linear EqualizerBand gain for a gain in dB (clamped to the band's supported range).
    public static double BandGain(float gainDb) => Math.Clamp(Math.Pow(10, gainDb / 20.0), MinBandGain, MaxBandGain);

    private void ApplyEqualizer()
    {
        if (_submix is null || _equalizers.Count == 0) return;
        try
        {
            var frequencies = EqualizerSetting.Frequencies;
            for (var effect = 0; effect < _equalizers.Count; effect++)
            {
                var bands = _equalizers[effect].Bands;
                for (var b = 0; b < bands.Count; b++)
                {
                    var index = effect * BandsPerEffect + b;
                    var band = bands[b];
                    if (index < frequencies.Length)
                    {
                        band.FrequencyCenter = frequencies[index];
                        band.Bandwidth = 1.0;
                        band.Gain = BandGain(_isEqEnabled && index < _eqGains.Length ? _eqGains[index] : 0);
                    }
                    else
                    {
                        band.FrequencyCenter = 1000;
                        band.Bandwidth = 1.0;
                        band.Gain = 1.0; // neutral
                    }
                }
                if (_isEqEnabled) _submix.EnableEffectsByDefinition(_equalizers[effect]);
                else _submix.DisableEffectsByDefinition(_equalizers[effect]);
            }
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(Log, $"Applying the equalizer failed: {ex.Message}");
        }
    }

    private void StartGraph()
    {
        if (_graph is null || _isGraphRunning) return;
        try
        {
            _graph.Start();
            _isGraphRunning = true;
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(Log, $"Starting the graph failed: {ex.Message}");
        }
    }

    private void StopGraph()
    {
        if (_graph is null || !_isGraphRunning) return;
        try { _graph.Stop(); } catch (Exception ex) { AmperfyLog.Warning(Log, $"Stopping the graph failed: {ex.Message}"); }
        _isGraphRunning = false;
    }

    private void Graph_UnrecoverableErrorOccurred(AudioGraph sender, AudioGraphUnrecoverableErrorOccurredEventArgs args)
    {
        var error = args.Error;
        var generation = Volatile.Read(ref _generation);
        MainThread.Post(() =>
        {
            if (_isDisposed) return;
            AmperfyLog.Warning(Log, $"AudioGraph error: {error}");
            _isGraphRunning = false;
            if (generation == _generation && _current is { } current) ReportError(current, new AudioEngineException($"Audio output failed: {error}"));
        });
    }

    // --- entries ------------------------------------------------------------------------------

    private async Task PrepareAsync(Entry entry, bool isFirst)
    {
        var generation = _generation;
        var ct = _cts.Token;
        await Task.Yield();
        if (!await EnsureGraphAsync() || _graph is null)
        {
            if (generation == _generation && !_isDisposed) OnUnsupported(entry, isFirst, new AudioEngineException("Audio graph not available"));
            return;
        }
        AudioSourceHandle? handle = null;
        MediaSourceAudioInputNode? node = null;
        Exception? failure = null;
        var isUnsupported = false;
        try
        {
            handle = await AudioSourceFactory.CreateAsync(entry.Url, entry.MimeType, entry.Headers, ct);
            var creation = _graph.CreateMediaSourceAudioInputNodeAsync(handle.Source).AsTask(ct);
            var finished = await Task.WhenAny(creation, Task.Delay(NodeCreationTimeout, ct));
            if (finished != creation)
            {
                isUnsupported = true;
                failure = new AudioEngineException("Opening the media source timed out");
                _ = creation.ContinueWith(t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion) t.Result.Node?.Dispose();
                }, TaskScheduler.Default);
            }
            else
            {
                var result = await creation;
                if (result.Status == MediaSourceAudioInputNodeCreationStatus.Success)
                {
                    node = result.Node;
                }
                else
                {
                    isUnsupported = result.Status != MediaSourceAudioInputNodeCreationStatus.NetworkError;
                    failure = new AudioEngineException($"Audio input creation failed: {result.Status}", result.ExtendedError);
                }
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (_isDisposed || generation != _generation || (entry != _current && entry != _next))
        {
            node?.Dispose();
            handle?.Dispose();
            return;
        }
        if (node is null || handle is null)
        {
            handle?.Dispose();
            var error = failure ?? new AudioEngineException("Audio input creation failed");
            AmperfyLog.Warning(Log, $"Preparing {Redact(entry.Url)} failed: {error.Message}");
            if (isUnsupported) OnUnsupported(entry, isFirst, error);
            else OnEntryFailed(entry, error);
            return;
        }

        try
        {
            entry.Handle = handle;
            entry.Node = node;
            node.Stop();
            ApplyNodeSettings(entry);
            var entryId = entry.Id;
            node.MediaSourceCompleted += (_, _) => Node_MediaSourceCompleted(generation, entryId);
            node.AddOutgoingConnection(_submix);
            entry.IsReady = true;
            if (entry == _current)
            {
                // first entry, or the queued one became due before it was ready
                node.Start();
                ReportStart(entry, isFirst ? AudioStreamingPlayerState.Paused : AudioStreamingPlayerState.Playing);
                if (!isFirst) StartGraph();
            }
            else
            {
                Volatile.Write(ref _gaplessNext, entry);
            }
        }
        catch (Exception ex)
        {
            OnEntryFailed(entry, ex);
        }
    }

    private void OnUnsupported(Entry entry, bool isFirst, Exception error)
    {
        if (isFirst && entry == _current)
        {
            // let the owner play it with the MediaPlayer engine
            _current = null;
            _state = AudioStreamingPlayerState.Stopped;
            var generation = _generation;
            MainThread.Post(() =>
            {
                if (_isDisposed || generation != _generation) return;
                PlaybackUnsupported?.Invoke(entry.Url, entry.MimeType, entry.Headers);
            });
        }
        else
        {
            OnEntryFailed(entry, error);
        }
    }

    private void OnEntryFailed(Entry entry, Exception error)
    {
        entry.Failure = error;
        if (entry == _current) ReportError(entry, error);
        // a failed queued entry is reported when it becomes due
    }

    /// Audio thread: the current input node finished. Starts the prepared next node immediately (gapless).
    private void Node_MediaSourceCompleted(int generation, int entryId)
    {
        if (generation != Volatile.Read(ref _generation)) return;
        var next = Volatile.Read(ref _gaplessNext);
        if (next?.Node is { } nextNode)
        {
            try
            {
                nextNode.Start();
                next.IsStartedByAudioThread = true;
            }
            catch (Exception)
            {
                // handled on the main thread
            }
        }
        MainThread.Post(() => OnEntryCompleted(generation, entryId));
    }

    private void OnEntryCompleted(int generation, int entryId)
    {
        if (_isDisposed || generation != _generation || _current is not { } old || old.Id != entryId || old.IsFinished) return;
        old.IsFinished = true;
        RaisePosted(() => DidFinishPlaying?.Invoke(old.Url));
        var next = _next;
        _next = null;
        Volatile.Write(ref _gaplessNext, null);
        DisposeEntry(old);
        _current = next;
        if (next is null)
        {
            _state = AudioStreamingPlayerState.Stopped;
            StopGraph();
            return;
        }
        if (next.Failure is not null)
        {
            ReportError(next, next.Failure);
            return;
        }
        if (!next.IsReady)
        {
            // PrepareAsync starts it when ready
            _state = AudioStreamingPlayerState.Buffering;
            return;
        }
        if (!next.IsStartedByAudioThread)
        {
            try { next.Node?.Start(); } catch (Exception ex) { ReportError(next, ex); return; }
        }
        ApplyNodeSettings(next);
        ReportStart(next, AudioStreamingPlayerState.Playing);
    }

    private void ApplyNodeSettings(Entry? entry)
    {
        if (entry?.Node is not { } node) return;
        try
        {
            node.OutgoingGain = _replayGain;
            node.PlaybackSpeedFactor = _rate;
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(Log, $"Applying gain/rate failed: {ex.Message}");
        }
    }

    // --- reporting ----------------------------------------------------------------------------

    private void ReportStart(Entry entry, AudioStreamingPlayerState state)
    {
        if (entry.IsStartReported) return;
        entry.IsStartReported = true;
        _state = state;
        RaisePosted(() => DidStartPlaying?.Invoke(entry.Url));
    }

    private void ReportError(Entry entry, Exception error)
    {
        if (entry.IsErrorReported) return;
        entry.IsErrorReported = true;
        _state = AudioStreamingPlayerState.Error;
        StopGraph();
        RaisePosted(() => UnexpectedError?.Invoke(error));
    }

    private void RaisePosted(Action raise)
    {
        var generation = _generation;
        MainThread.Post(() =>
        {
            if (_isDisposed || generation != _generation) return;
            try { raise(); }
            catch (Exception ex) { AmperfyLog.Error(Log, $"Event handler failed: {ex}"); }
        });
    }

    // --- cleanup ------------------------------------------------------------------------------

    private void DisposeEntry(Entry? entry)
    {
        if (entry is null) return;
        if (entry.Node is { } node)
        {
            try
            {
                node.Stop();
                if (_submix is not null) node.RemoveOutgoingConnection(_submix);
                node.Dispose();
            }
            catch (Exception) { /* ignore */ }
            entry.Node = null;
        }
        entry.Handle?.Dispose();
        entry.Handle = null;
    }

    private void Teardown()
    {
        Interlocked.Increment(ref _generation);
        try { _cts.Cancel(); } catch (Exception) { /* ignore */ }
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        Volatile.Write(ref _gaplessNext, null);
        StopGraph();
        DisposeEntry(_current);
        DisposeEntry(_next);
        _current = null;
        _next = null;
    }

    private static string Redact(string url)
    {
        var q = url.IndexOf('?');
        return q < 0 ? url : url[..q];
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        Teardown();
        _isDisposed = true;
        _state = AudioStreamingPlayerState.Disposed;
        if (_graph is { } graph)
        {
            try
            {
                graph.UnrecoverableErrorOccurred -= Graph_UnrecoverableErrorOccurred;
                graph.Dispose();
            }
            catch (Exception ex)
            {
                AmperfyLog.Warning(Log, $"Dispose failed: {ex.Message}");
            }
        }
        _graph = null;
        _submix = null;
        _output = null;
        _equalizers.Clear();
        _cts.Dispose();
    }
}
