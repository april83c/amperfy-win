using Amperfy.Core.Api;
using Amperfy.Core.Common;
using Amperfy.Core.Player;
using Windows.Foundation;
using Windows.Media.Playback;

namespace Amperfy.App.Services.Audio;

/// Error raised by the audio engines (becomes <see cref="IAudioStreamingPlayer.UnexpectedError"/>).
public sealed class AudioEngineException : Exception
{
    public AudioEngineException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <see cref="IAudioStreamingPlayer"/> on Windows.Media.Playback.MediaPlayer + MediaPlaybackList.
///
/// * <see cref="Play"/> replaces the playback list, <see cref="Queue"/> appends to it: MediaPlaybackList plays
///   the entries gapless (the next item is opened/prefetched in advance).
/// * Entries are identified by the URL string exactly as passed (stored as a custom property of the media source).
/// * Transitions (MediaPlaybackList.CurrentItemChanged) raise DidFinishPlaying(old) + DidStartPlaying(new).
/// * Gain stage: MediaPlayer.Volume = Volume * min(ReplayGainVolume, 1). A replay gain boost (&gt; 1) can't be
///   applied by MediaPlayer (volume is limited to 1); the EQ is not supported by this engine (see
///   <see cref="AudioGraphAudioEngine"/>).
/// * The MediaPlayer's own system media transport controls integration is disabled (the app implements them in
///   <see cref="SystemMediaControls"/>).
/// * Radio streams: the ICY stream title is read with <see cref="IcyMetadataPoller"/> (separate connection).
/// Members are called on the main thread; all events are posted to the main thread.
public sealed class MediaPlayerAudioEngine : IAudioStreamingPlayer
{
    private const string Log = "MediaPlayerEngine";
    private const string EntryIdKey = "Amperfy.EntryId";
    private static readonly TimeSpan StartFallbackDelay = TimeSpan.FromSeconds(2);

    private sealed class Entry
    {
        public required int Id { get; init; }
        public required string Url { get; init; }
        public string? MimeType { get; init; }
        public IReadOnlyDictionary<string, string>? Headers { get; init; }
        public bool IsLiveStream { get; init; }
        public AudioSourceHandle? Handle { get; set; }
        public MediaPlaybackItem? Item { get; set; }
        public bool IsInList { get; set; }
        public Exception? Failure { get; set; }
        public bool IsStartReported { get; set; }
        public bool IsFinished { get; set; }
        public bool IsErrorReported { get; set; }
        public Task Ready { get; set; } = Task.CompletedTask;
    }

    private readonly MediaPlayer _player;
    private readonly Func<string, bool>? _isLiveStream;
    private readonly List<Entry> _entries = []; // entries of the current list (the current entry first)
    private MediaPlaybackList? _list;
    private Action? _unsubscribeList;
    private Entry? _current;
    private bool _isListEnded;
    private int _nextId;
    private int _generation;
    private CancellationTokenSource _cts = new();
    private IDisposable? _startFallbackTimer;
    private IcyMetadataPoller? _icyPoller;
    private float _volume = 1.0f;
    private float _replayGain = 1.0f;
    private float _rate = 1.0f;
    private AudioStreamingPlayerState _state = AudioStreamingPlayerState.Ready;
    private bool _isDisposed;

    /// <param name="isLiveStream">Returns true if the URL is a live (radio) stream: its ICY metadata is read.</param>
    public MediaPlayerAudioEngine(Func<string, bool>? isLiveStream = null)
    {
        _isLiveStream = isLiveStream;
        _player = new MediaPlayer
        {
            AutoPlay = false,
            AudioCategory = MediaPlayerAudioCategory.Media,
        };
        // The app drives the system media transport controls itself (SystemMediaControls).
        _player.CommandManager.IsEnabled = false;
        try { _player.SystemMediaTransportControls.IsEnabled = false; } catch (Exception) { /* not available */ }
        _player.MediaOpened += Player_MediaOpened;
        _player.MediaEnded += Player_MediaEnded;
        _player.MediaFailed += Player_MediaFailed;
        _player.PlaybackSession.PlaybackStateChanged += Session_PlaybackStateChanged;
        ApplyVolume();
    }

    public event Action<string>? DidStartPlaying;
    public event Action<string>? DidFinishPlaying;
    public event Action<Exception>? UnexpectedError;
#pragma warning disable CS0067 // queued entries are never cancelled silently (Play/Stop reset everything)
    public event Action? DidCancel;
#pragma warning restore CS0067
    public event Action<IReadOnlyDictionary<string, string>>? DidReadMetadata;

    // --- commands -----------------------------------------------------------------------------

    public void Play(string url, string? mimeType, IReadOnlyDictionary<string, string>? httpHeaders)
    {
        if (_isDisposed) return;
        Teardown();
        var entry = CreateEntry(url, mimeType, httpHeaders);
        _current = entry;
        _state = AudioStreamingPlayerState.Buffering;
        CreateList();
        _entries.Add(entry);
        entry.Ready = PrepareAsync(entry, Task.CompletedTask, isFirst: true);
    }

    public void Queue(string url, string? mimeType, IReadOnlyDictionary<string, string>? httpHeaders)
    {
        if (_isDisposed) return;
        if (_list is null || _current is null)
        {
            Play(url, mimeType, httpHeaders);
            return;
        }
        var entry = CreateEntry(url, mimeType, httpHeaders);
        var previous = _entries[^1].Ready;
        _entries.Add(entry);
        entry.Ready = PrepareAsync(entry, previous, isFirst: false);
    }

    public void Pause()
    {
        if (_isDisposed) return;
        try { _player.Pause(); } catch (Exception ex) { AmperfyLog.Warning(Log, $"Pause failed: {ex.Message}"); }
        if (_current is { IsStartReported: true, IsFinished: false }) _state = AudioStreamingPlayerState.Paused;
    }

    public void Resume()
    {
        if (_isDisposed || _current is null || _isListEnded) return;
        try
        {
            _player.Play();
            ApplyRate();
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(Log, $"Resume failed: {ex.Message}");
        }
        if (_current.IsStartReported) _state = AudioStreamingPlayerState.Playing;
    }

    public void Stop()
    {
        if (_isDisposed) return;
        Teardown();
        _state = AudioStreamingPlayerState.Stopped;
    }

    public void Seek(double seconds)
    {
        if (_isDisposed || _current is null) return;
        try
        {
            var session = _player.PlaybackSession;
            if (!session.CanSeek) return;
            var target = Math.Max(0, seconds);
            var duration = Duration;
            if (duration > 0) target = Math.Min(target, Math.Max(0, duration - 0.5));
            session.Position = TimeSpan.FromSeconds(target);
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
            ApplyRate();
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0f, 1.0f);
            ApplyVolume();
        }
    }

    public float ReplayGainVolume
    {
        get => _replayGain;
        set
        {
            _replayGain = value < 0 ? 0 : value;
            ApplyVolume();
        }
    }

    /// Linear gain applied to the MediaPlayer (volume * replay gain, limited to 1).
    public static double EffectiveVolume(float volume, float replayGain) => Math.Clamp(volume * Math.Min(replayGain, 1.0f), 0.0f, 1.0f);

    /// The EQ isn't supported by the MediaPlayer path (see AudioGraphAudioEngine).
    public void SetEqualizer(float[] gains, bool enabled) { }

    public double Progress
    {
        get
        {
            if (_isDisposed || _current is not { IsStartReported: true }) return 0;
            try
            {
                var seconds = _player.PlaybackSession.Position.TotalSeconds;
                return double.IsFinite(seconds) && seconds > 0 ? seconds : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }

    public double Duration
    {
        get
        {
            if (_isDisposed || _current is null) return 0;
            try
            {
                var seconds = _player.PlaybackSession.NaturalDuration.TotalSeconds;
                // live streams report 0 or TimeSpan.MaxValue
                return double.IsFinite(seconds) && seconds > 0 && seconds < TimeSpan.FromDays(2).TotalSeconds ? seconds : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }

    public AudioStreamingPlayerState State
    {
        get
        {
            if (_isDisposed) return AudioStreamingPlayerState.Disposed;
            if (_current is not { IsStartReported: true } current || current.IsFinished || _isListEnded) return _state;
            try
            {
                return _player.PlaybackSession.PlaybackState switch
                {
                    MediaPlaybackState.Playing => AudioStreamingPlayerState.Playing,
                    MediaPlaybackState.Paused => AudioStreamingPlayerState.Paused,
                    MediaPlaybackState.Buffering or MediaPlaybackState.Opening => _state == AudioStreamingPlayerState.Paused
                        ? AudioStreamingPlayerState.Paused
                        : AudioStreamingPlayerState.Buffering,
                    _ => _state,
                };
            }
            catch (Exception)
            {
                return _state;
            }
        }
    }

    // --- list / entries -----------------------------------------------------------------------

    private Entry CreateEntry(string url, string? mimeType, IReadOnlyDictionary<string, string>? headers)
    {
        var isLive = false;
        if (headers is null && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            try { isLive = _isLiveStream?.Invoke(url) ?? false; } catch (Exception) { /* ignore */ }
        }
        return new Entry { Id = ++_nextId, Url = url, MimeType = mimeType, Headers = headers, IsLiveStream = isLive };
    }

    private void CreateList()
    {
        var list = new MediaPlaybackList { AutoRepeatEnabled = false, ShuffleEnabled = false };
        var generation = _generation;
        TypedEventHandler<MediaPlaybackList, CurrentMediaPlaybackItemChangedEventArgs> currentItemChanged = (_, args) =>
        {
            var oldId = EntryIdOf(args.OldItem);
            var newId = EntryIdOf(args.NewItem);
            var reason = args.Reason;
            MainThread.Post(() => OnCurrentItemChanged(generation, oldId, newId, reason));
        };
        TypedEventHandler<MediaPlaybackList, MediaPlaybackItemFailedEventArgs> itemFailed = (_, args) =>
        {
            var id = EntryIdOf(args.Item);
            var error = args.Error;
            var message = $"{error?.ErrorCode} (0x{error?.ExtendedError?.HResult ?? 0:X8}) {error?.ExtendedError?.Message}";
            MainThread.Post(() => OnItemFailed(generation, id, message));
        };
        TypedEventHandler<MediaPlaybackList, MediaPlaybackItemOpenedEventArgs> itemOpened = (_, args) =>
        {
            var id = EntryIdOf(args.Item);
            MainThread.Post(() => OnItemOpened(generation, id));
        };
        list.CurrentItemChanged += currentItemChanged;
        list.ItemFailed += itemFailed;
        list.ItemOpened += itemOpened;
        _unsubscribeList = () =>
        {
            list.CurrentItemChanged -= currentItemChanged;
            list.ItemFailed -= itemFailed;
            list.ItemOpened -= itemOpened;
        };
        _list = list;
    }

    private static int? EntryIdOf(MediaPlaybackItem? item)
    {
        try
        {
            if (item?.Source?.CustomProperties is { } props && props.TryGetValue(EntryIdKey, out var value) && value is int id) return id;
        }
        catch (Exception)
        {
            // item already closed
        }
        return null;
    }

    private Entry? FindEntry(int? id) => id is { } i ? _entries.FirstOrDefault(e => e.Id == i) : null;

    /// Creates the media source of the entry and appends it to the list (in queue order).
    private async Task PrepareAsync(Entry entry, Task previous, bool isFirst)
    {
        var generation = _generation;
        var ct = _cts.Token;
        AudioSourceHandle? handle = null;
        Exception? failure = null;
        try
        {
            await Task.Yield(); // never run the preparation (and raise events) inside the Play/Queue call
            handle = await AudioSourceFactory.CreateAsync(entry.Url, entry.MimeType, entry.Headers, ct);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        try { await previous; } catch (Exception) { /* keep the queue order only */ }

        if (_isDisposed || generation != _generation || _list is null)
        {
            handle?.Dispose();
            return;
        }
        if (failure is not null || handle is null)
        {
            OnEntryPreparationFailed(entry, failure ?? new AudioEngineException("Media source could not be created"));
            return;
        }

        try
        {
            entry.Handle = handle;
            handle.Source.CustomProperties[EntryIdKey] = entry.Id;
            var item = new MediaPlaybackItem(handle.Source);
            entry.Item = item;
            _list.Items.Add(item);
            entry.IsInList = true;
            if (isFirst)
            {
                _player.Source = _list;
                ApplyVolume();
                ArmStartFallback(entry, generation);
            }
            else if (_isListEnded && _current == entry)
            {
                // The previous entry ended before this one was ready: continue with it.
                _isListEnded = false;
                _list.MoveTo((uint)(_list.Items.Count - 1));
                _player.Play();
                ApplyRate();
                ReportStart(entry);
            }
        }
        catch (Exception ex)
        {
            OnEntryPreparationFailed(entry, ex);
        }
    }

    private void OnEntryPreparationFailed(Entry entry, Exception failure)
    {
        AmperfyLog.Warning(Log, $"Preparing {Redact(entry.Url)} failed: {failure.Message}");
        entry.Failure = failure;
        if (entry == _current)
        {
            // the entry to play (or the queued one that became due) failed
            ReportError(entry, failure);
        }
        else
        {
            // queued entry: remove it from the list, the error is reported when it becomes due
            RemoveFromList(entry);
        }
    }

    private void RemoveFromList(Entry entry)
    {
        if (!entry.IsInList || _list is null || entry.Item is null) return;
        try { _list.Items.Remove(entry.Item); } catch (Exception) { /* ignore */ }
        entry.IsInList = false;
    }

    // --- MediaPlayer events (posted to the main thread) -----------------------------------------

    private void OnCurrentItemChanged(int generation, int? oldId, int? newId, MediaPlaybackItemChangedReason reason)
    {
        if (_isDisposed || generation != _generation) return;
        var newEntry = FindEntry(newId);
        if (newEntry is not null && newEntry == _current) return; // initial item

        var old = _current;
        if (old is null) return;
        if (oldId is { } o && o != old.Id && newEntry is null) return; // stale transition

        if (reason == MediaPlaybackItemChangedReason.Error && !old.IsErrorReported && !old.IsFinished)
        {
            ReportError(old, new AudioEngineException("Playback failed"));
            return;
        }
        FinishCurrentAndAdvance(newEntry);
    }

    /// The current entry ended: report it and continue with the next entry (if already in the list).
    private void FinishCurrentAndAdvance(Entry? newEntry)
    {
        var old = _current;
        if (old is null || old.IsFinished) return;
        old.IsFinished = true;
        StopIcy();
        var oldIndex = _entries.IndexOf(old);
        var next = oldIndex >= 0 && oldIndex + 1 < _entries.Count ? _entries[oldIndex + 1] : null;
        if (newEntry is not null && newEntry != next) next = newEntry;

        RaisePosted(() => DidFinishPlaying?.Invoke(old.Url));

        // release the finished entry (HTTP block cache, file handles)
        _entries.Remove(old);
        RemoveFromList(old);
        old.Handle?.Dispose();

        if (next is null)
        {
            _current = null;
            _isListEnded = true;
            _state = AudioStreamingPlayerState.Stopped;
            return;
        }
        _current = next;
        if (next.Failure is not null)
        {
            _isListEnded = true;
            ReportError(next, next.Failure);
            return;
        }
        if (!next.IsInList)
        {
            // still preparing: PrepareAsync continues with it
            _isListEnded = newEntry is null;
            _state = AudioStreamingPlayerState.Buffering;
            return;
        }
        _isListEnded = false;
        if (newEntry is null)
        {
            // the list ended (e.g. the next item was added too late): continue explicitly
            try
            {
                var index = _list?.Items.IndexOf(next.Item!) ?? -1;
                if (index >= 0) _list!.MoveTo((uint)index);
                _player.Play();
            }
            catch (Exception ex)
            {
                ReportError(next, ex);
                return;
            }
        }
        ApplyRate();
        ReportStart(next);
    }

    private void OnItemFailed(int generation, int? id, string message)
    {
        if (_isDisposed || generation != _generation) return;
        var entry = FindEntry(id);
        if (entry is null) return;
        var error = new AudioEngineException($"Playback failed: {message}");
        AmperfyLog.Warning(Log, $"Item failed {Redact(entry.Url)}: {message}");
        if (entry == _current)
        {
            ReportError(entry, error);
        }
        else
        {
            entry.Failure ??= error;
            RemoveFromList(entry);
        }
    }

    private void OnItemOpened(int generation, int? id)
    {
        if (_isDisposed || generation != _generation) return;
        if (FindEntry(id) is { } entry && entry == _current) ReportStart(entry);
    }

    private void Player_MediaOpened(MediaPlayer sender, object args)
    {
        var generation = Volatile.Read(ref _generation);
        MainThread.Post(() =>
        {
            if (_isDisposed || generation != _generation) return;
            if (_current is { IsInList: true } current && _entries.Count > 0 && _entries[0] == current) ReportStart(current);
        });
    }

    private void Player_MediaEnded(MediaPlayer sender, object args)
    {
        var generation = Volatile.Read(ref _generation);
        MainThread.Post(() =>
        {
            if (_isDisposed || generation != _generation) return;
            if (_current is { IsStartReported: true, IsFinished: false }) FinishCurrentAndAdvance(null);
        });
    }

    private void Player_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        var generation = Volatile.Read(ref _generation);
        var message = $"{args.Error} (0x{args.ExtendedErrorCode?.HResult ?? 0:X8}) {args.ErrorMessage}";
        MainThread.Post(() =>
        {
            if (_isDisposed || generation != _generation || _current is not { } current) return;
            try
            {
                // a failing prefetch of a queued item must not stop the current one (ItemFailed marks the item)
                if (current.IsStartReported && _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) return;
            }
            catch (Exception) { /* report */ }
            AmperfyLog.Warning(Log, $"Media failed {Redact(current.Url)}: {message}");
            ReportError(current, new AudioEngineException($"Playback failed: {message}"));
        });
    }

    private void Session_PlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        MediaPlaybackState state;
        try { state = sender.PlaybackState; } catch (Exception) { return; }
        var generation = Volatile.Read(ref _generation);
        MainThread.Post(() =>
        {
            if (_isDisposed || generation != _generation || _current is not { } current) return;
            if (state == MediaPlaybackState.Playing && current.IsInList)
            {
                ReportStart(current);
                _state = AudioStreamingPlayerState.Playing;
            }
        });
    }

    // --- reporting ----------------------------------------------------------------------------

    private void ReportStart(Entry entry)
    {
        if (entry.IsStartReported || entry.IsFinished) return;
        entry.IsStartReported = true;
        _startFallbackTimer?.Dispose();
        _startFallbackTimer = null;
        _state = AudioStreamingPlayerState.Playing;
        ApplyRate();
        if (entry.IsLiveStream) StartIcy(entry);
        RaisePosted(() => DidStartPlaying?.Invoke(entry.Url));
    }

    private void ReportError(Entry entry, Exception error)
    {
        if (entry.IsErrorReported) return;
        entry.IsErrorReported = true;
        _state = AudioStreamingPlayerState.Error;
        _startFallbackTimer?.Dispose();
        _startFallbackTimer = null;
        StopIcy();
        try { _player.Pause(); } catch (Exception) { /* ignore */ }
        RaisePosted(() => UnexpectedError?.Invoke(error));
    }

    /// If neither ItemOpened nor MediaOpened arrive, start the playback to force opening the source.
    private void ArmStartFallback(Entry entry, int generation)
    {
        _startFallbackTimer?.Dispose();
        _startFallbackTimer = MainThread.CreateTimer(StartFallbackDelay, () =>
        {
            _startFallbackTimer?.Dispose();
            _startFallbackTimer = null;
            if (_isDisposed || generation != _generation || entry != _current || entry.IsStartReported || entry.IsErrorReported) return;
            AmperfyLog.Info(Log, "Media not opened yet: starting playback");
            try { _player.Play(); } catch (Exception ex) { ReportError(entry, ex); }
        }, repeats: false);
    }

    /// Raises an event on the main thread (never synchronously inside a call into the engine).
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

    // --- ICY metadata -------------------------------------------------------------------------

    private void StartIcy(Entry entry)
    {
        StopIcy();
        if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri)) return;
        var generation = _generation;
        var poller = new IcyMetadataPoller();
        _icyPoller = poller;
        poller.Start(AmperfyHttp.Client, uri, entry.Headers, metadata => MainThread.Post(() =>
        {
            if (_isDisposed || generation != _generation || _current != entry || _icyPoller != poller) return;
            DidReadMetadata?.Invoke(metadata);
        }));
    }

    private void StopIcy()
    {
        _icyPoller?.Dispose();
        _icyPoller = null;
    }

    // --- helpers ------------------------------------------------------------------------------

    private void ApplyVolume()
    {
        try { _player.Volume = EffectiveVolume(_volume, _replayGain); }
        catch (Exception ex) { AmperfyLog.Warning(Log, $"Setting the volume failed: {ex.Message}"); }
    }

    private void ApplyRate()
    {
        try
        {
            var session = _player.PlaybackSession;
            if (Math.Abs(session.PlaybackRate - _rate) > 0.001) session.PlaybackRate = _rate;
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(Log, $"Setting the playback rate failed: {ex.Message}");
        }
    }

    private void Teardown()
    {
        _generation++;
        try { _cts.Cancel(); } catch (Exception) { /* ignore */ }
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        _startFallbackTimer?.Dispose();
        _startFallbackTimer = null;
        StopIcy();
        _unsubscribeList?.Invoke();
        _unsubscribeList = null;
        try { _player.Pause(); } catch (Exception) { /* ignore */ }
        try { _player.Source = null; } catch (Exception) { /* ignore */ }
        foreach (var entry in _entries) entry.Handle?.Dispose();
        _entries.Clear();
        _list = null;
        _current = null;
        _isListEnded = false;
    }

    /// URL without query (credentials) for logging.
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
        try
        {
            _player.MediaOpened -= Player_MediaOpened;
            _player.MediaEnded -= Player_MediaEnded;
            _player.MediaFailed -= Player_MediaFailed;
            _player.PlaybackSession.PlaybackStateChanged -= Session_PlaybackStateChanged;
            _player.Dispose();
        }
        catch (Exception ex)
        {
            AmperfyLog.Warning(Log, $"Dispose failed: {ex.Message}");
        }
        _cts.Dispose();
    }
}
