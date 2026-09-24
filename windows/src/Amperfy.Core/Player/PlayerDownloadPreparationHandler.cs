using Amperfy.Core.Downloads;

namespace Amperfy.Core.Player;

/// Port of PlayerDownloadPreparationHandler.swift: pre-downloads the next items when auto caching is enabled.
public sealed class PlayerDownloadPreparationHandler : IMusicPlayable
{
    public const int PreDownloadCount = 3;

    private readonly IPlayerStatusPersistent _playerStatus;
    private readonly PlayQueueHandler _queueHandler;
    private readonly Func<AccountInfo, IDownloadManageable> _getPlayableDownloaderCB;

    public PlayerDownloadPreparationHandler(IPlayerStatusPersistent playerStatus, PlayQueueHandler queueHandler,
        Func<AccountInfo, IDownloadManageable> getPlayableDownloaderCB)
    {
        _playerStatus = playerStatus;
        _queueHandler = queueHandler;
        _getPlayableDownloaderCB = getPlayableDownloaderCB;
    }

    private void PreDownloadNextItems()
    {
        var upcomingItemsCount = Math.Min(_queueHandler.UserQueueCount + _queueHandler.NextQueueCount, PreDownloadCount);
        if (upcomingItemsCount <= 0) return;

        var userQueueRangeEnd = Math.Min(_queueHandler.UserQueueCount, PreDownloadCount);
        for (var i = 0; i < userQueueRangeEnd; i++)
        {
            var playable = _queueHandler.GetUserQueueItem(i)!;
            if (!playable.IsCached && !playable.IsRadio && playable.Account?.Info is { } accountInfo)
                _getPlayableDownloaderCB(accountInfo).Download(playable);
        }
        var nextQueueRangeEnd = Math.Min(_queueHandler.NextQueueCount, PreDownloadCount - userQueueRangeEnd);
        for (var i = 0; i < nextQueueRangeEnd; i++)
        {
            var playable = _queueHandler.GetNextQueueItem(i)!;
            if (!playable.IsCached && !playable.IsRadio && playable.Account?.Info is { } accountInfo)
                _getPlayableDownloaderCB(accountInfo).Download(playable);
        }
    }

    public void DidStartPlayingFromBeginning()
    {
        if (_playerStatus.IsAutoCachePlayedItems) PreDownloadNextItems();
    }
}
