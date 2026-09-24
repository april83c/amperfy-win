namespace Amperfy.Core.Player;

/// Port of PlayerNotificationAdapter.swift: forwards play/pause/stop to the <see cref="EventNotificationHandler"/>.
public sealed class PlayerNotificationAdapter : IMusicPlayable
{
    private readonly EventNotificationHandler _notificationHandler;

    public PlayerNotificationAdapter(EventNotificationHandler notificationHandler)
    {
        _notificationHandler = notificationHandler;
    }

    public void DidStartPlaying() => _notificationHandler.Post(AmperfyNotification.PlayerPlay, this);

    public void DidPause() => _notificationHandler.Post(AmperfyNotification.PlayerPause, this);

    public void DidStopPlaying() => _notificationHandler.Post(AmperfyNotification.PlayerStop, this);
}
