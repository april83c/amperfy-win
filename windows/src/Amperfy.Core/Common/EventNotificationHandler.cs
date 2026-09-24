namespace Amperfy.Core.Common;

public enum AmperfyNotification
{
    AccountActiveChanged,
    AccountAdded,
    AccountDeleted,
    DownloadFinishedSuccess,
    PlayerPlay,
    PlayerPause,
    PlayerStop,
    FetchControllerSortChanged,
    OfflineModeChanged,
    NetworkStatusChanged,
    /// Library content changed (after a sync step) - lists should refresh.
    LibraryChanged,
    /// The sidebar library categories (LibraryDisplaySettings) of the active account changed.
    LibraryDisplaySettingsChanged,
}

public sealed record NotificationArgs(AmperfyNotification Name, object? Sender = null, object? Payload = null);

/// Replacement for NotificationCenter. Handlers are invoked on the thread that posts (the main thread).
public sealed class EventNotificationHandler
{
    private readonly Dictionary<AmperfyNotification, List<WeakReference<Action<NotificationArgs>>>> _handlers = [];
    private readonly List<Action<NotificationArgs>> _strongRefs = [];
    private readonly object _lock = new();

    /// Registers a handler. Keep the returned token to unregister (or dispose it).
    public IDisposable Register(AmperfyNotification name, Action<NotificationArgs> handler)
    {
        lock (_lock)
        {
            if (!_handlers.TryGetValue(name, out var list)) _handlers[name] = list = [];
            list.Add(new WeakReference<Action<NotificationArgs>>(handler));
            _strongRefs.Add(handler);
        }
        return new Token(this, name, handler);
    }

    public void Remove(AmperfyNotification name, Action<NotificationArgs> handler)
    {
        lock (_lock)
        {
            _strongRefs.Remove(handler);
            if (_handlers.TryGetValue(name, out var list))
                list.RemoveAll(w => !w.TryGetTarget(out var h) || h == handler);
        }
    }

    public void Post(AmperfyNotification name, object? sender = null, object? payload = null)
    {
        List<Action<NotificationArgs>> targets = [];
        lock (_lock)
        {
            if (!_handlers.TryGetValue(name, out var list)) return;
            list.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var w in list)
                if (w.TryGetTarget(out var h)) targets.Add(h);
        }
        var args = new NotificationArgs(name, sender, payload);
        foreach (var t in targets)
        {
            try { t(args); }
            catch (Exception ex) { AmperfyLog.Error("Notification", $"{name} handler failed: {ex}"); }
        }
    }

    private sealed class Token(EventNotificationHandler owner, AmperfyNotification name, Action<NotificationArgs> handler) : IDisposable
    {
        public void Dispose() => owner.Remove(name, handler);
    }
}

/// Payload of AmperfyNotification.DownloadFinishedSuccess
public sealed record DownloadNotification(string Id);
