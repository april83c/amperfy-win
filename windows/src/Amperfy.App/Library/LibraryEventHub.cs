using Amperfy.App.Controls;
using Amperfy.App.Services;
using Amperfy.Core.Common;
using Amperfy.Core.Player;
using Microsoft.UI.Dispatching;

namespace Amperfy.App.Library;

/// Fans out core notifications (player state, finished downloads, offline mode) to the library
/// views, which subscribe while they are loaded. Also refreshes visible artworks when artwork
/// downloads finished (debounced). Initialized lazily on first use (UI thread).
public static class LibraryEventHub
{
    private static bool _isInitialized;
    private static PlayerNotifier? _playerNotifier; // the player holds notifiers weakly
    private static readonly List<IDisposable> Subscriptions = [];
    private static DispatcherQueueTimer? _artworkTimer;

    /// Currently playing item or play/pause state changed.
    public static event Action? PlayerChanged;

    /// A download finished successfully (payload: unique download id, e.g. "playable-12").
    public static event Action<string>? DownloadFinished;

    /// An entity was changed by an action (favorite, rating, cache deleted, ...).
    public static event Action<object>? EntityChanged;

    /// Offline mode was switched.
    public static event Action? OfflineModeChanged;

    public static void EnsureInitialized()
    {
        if (_isInitialized) return;
        _isInitialized = true;
        var services = AppServices.Instance;
        try
        {
            _playerNotifier = new PlayerNotifier();
            services.Player.AddNotifier(_playerNotifier);
        }
        catch (Exception ex)
        {
            AmperfyLog.Error("LibraryEventHub", $"Player notifier registration failed: {ex.Message}");
        }
        Subscriptions.Add(services.Notifications.Register(AmperfyNotification.DownloadFinishedSuccess, OnDownloadFinished));
        Subscriptions.Add(services.Notifications.Register(AmperfyNotification.OfflineModeChanged, _ => OfflineModeChanged?.Invoke()));
        if (DispatcherQueue.GetForCurrentThread() is { } dispatcher)
        {
            _artworkTimer = dispatcher.CreateTimer();
            _artworkTimer.Interval = TimeSpan.FromMilliseconds(400);
            _artworkTimer.IsRepeating = false;
            _artworkTimer.Tick += (_, _) => ArtworkImage.NotifyArtworkChanged();
        }
    }

    private static void OnDownloadFinished(NotificationArgs args)
    {
        if (args.Payload is not DownloadNotification notification) return;
        if (notification.Id.StartsWith("artwork", StringComparison.Ordinal))
        {
            if (_artworkTimer is { IsRunning: false } timer) timer.Start();
            return;
        }
        try { DownloadFinished?.Invoke(notification.Id); }
        catch (Exception ex) { AmperfyLog.Error("LibraryEventHub", $"DownloadFinished handler failed: {ex.Message}"); }
    }

    private static void RaisePlayerChanged()
    {
        try { PlayerChanged?.Invoke(); }
        catch (Exception ex) { AmperfyLog.Error("LibraryEventHub", $"PlayerChanged handler failed: {ex.Message}"); }
    }

    public static void RaiseEntityChanged(object entity)
    {
        try { EntityChanged?.Invoke(entity); }
        catch (Exception ex) { AmperfyLog.Error("LibraryEventHub", $"EntityChanged handler failed: {ex.Message}"); }
    }

    private sealed class PlayerNotifier : IMusicPlayable
    {
        public void DidStartPlayingFromBeginning() => RaisePlayerChanged();
        public void DidStartPlaying() => RaisePlayerChanged();
        public void DidPause() => RaisePlayerChanged();
        public void DidStopPlaying() => RaisePlayerChanged();
        public void DidPlaylistChange() => RaisePlayerChanged();
    }
}
