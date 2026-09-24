using Amperfy.Core.Downloads;

namespace Amperfy.Core.Player;

/// Port of NowPlayingInfoCenterHandler.swift (logic only): keeps the system media controls'
/// now playing information and playback state up to date.
public sealed class NowPlayingInfoHandler : IMusicPlayable, IDisposable
{
    private readonly AudioPlayer _musicPlayer;
    private readonly BackendAudioPlayer _backendAudioPlayer;
    private readonly AmperfySettings _settings;
    private readonly ISystemMediaControls _nowPlayingInfoCenter;
    private readonly Func<AccountInfo, IDownloadManageable>? _getArtworkDownloaderCB;
    private readonly IDisposable _downloadFinishedRegistration;

    public NowPlayingInfoHandler(AudioPlayer musicPlayer, BackendAudioPlayer backendAudioPlayer, ISystemMediaControls nowPlayingInfoCenter,
        AmperfySettings settings, EventNotificationHandler notificationHandler, Func<AccountInfo, IDownloadManageable>? getArtworkDownloaderCB)
    {
        _musicPlayer = musicPlayer;
        _backendAudioPlayer = backendAudioPlayer;
        _nowPlayingInfoCenter = nowPlayingInfoCenter;
        _settings = settings;
        _getArtworkDownloaderCB = getArtworkDownloaderCB;

        _nowPlayingInfoCenter.UpdatePlaybackStatus(SystemMediaPlaybackStatus.Stopped);

        // Swift registered for the download notifications of the artwork and playable download managers of all accounts
        _downloadFinishedRegistration = notificationHandler.Register(AmperfyNotification.DownloadFinishedSuccess, DownloadFinishedSuccessful);
    }

    private void UpdateNowPlayingInfo(AbstractPlayable playable)
    {
        var albumTitle = playable.AsSong?.Album?.Name ?? "";
        var nowPlaying = DisplayNowPlayingInfo(playable);

        string? artworkPath = null;
        if (playable.Account?.Info is { } accountInfo)
        {
            artworkPath = playable.ImagePath(_settings.Accounts.GetSetting(accountInfo).ArtworkDisplayPreference);
            if (playable.Artwork is { } artwork) _getArtworkDownloaderCB?.Invoke(accountInfo).Download(artwork);
        }

        _nowPlayingInfoCenter.UpdateNowPlaying(new NowPlayingMetadata(
            Title: nowPlaying.Title,
            Artist: nowPlaying.Artist,
            AlbumTitle: albumTitle,
            ArtworkPath: artworkPath,
            Duration: _backendAudioPlayer.Duration,
            ElapsedTime: _backendAudioPlayer.ElapsedTime,
            PlaybackRate: _backendAudioPlayer.PlaybackRate.AsDouble(),
            IsLiveStream: playable.IsRadio,
            IsCloudItem: !playable.IsCached,
            PlayableType: playable.DerivedType));
    }

    private RadioNowPlayingInfo DisplayNowPlayingInfo(AbstractPlayable playable)
    {
        if (playable.IsRadio && _musicPlayer.CurrentRadioNowPlaying is { IsEmpty: false } radioInfo) return radioInfo;
        return new RadioNowPlayingInfo(playable.Title, playable.CreatorName);
    }

    private void DownloadFinishedSuccessful(NotificationArgs args)
    {
        if (args.Payload is not DownloadNotification downloadNotification || _musicPlayer.CurrentlyPlaying is not { } curPlayable) return;
        if (curPlayable.UniqueId() == downloadNotification.Id) UpdateNowPlayingInfo(curPlayable);
        if (curPlayable.Artwork is { } artwork && artwork.UniqueId() == downloadNotification.Id) UpdateNowPlayingInfo(curPlayable);
    }

    public void DidStartPlaying()
    {
        if (_musicPlayer.CurrentlyPlaying is { } curPlayable) UpdateNowPlayingInfo(curPlayable);
        _nowPlayingInfoCenter.UpdatePlaybackStatus(SystemMediaPlaybackStatus.Playing);
    }

    public void DidPause()
    {
        if (_musicPlayer.CurrentlyPlaying is { } curPlayable) UpdateNowPlayingInfo(curPlayable);
        _nowPlayingInfoCenter.UpdatePlaybackStatus(SystemMediaPlaybackStatus.Paused);
    }

    public void DidStopPlaying()
    {
        _nowPlayingInfoCenter.UpdateNowPlaying(null);
        _nowPlayingInfoCenter.UpdatePlaybackStatus(SystemMediaPlaybackStatus.Stopped);
    }

    public void DidElapsedTimeChange()
    {
        if (_musicPlayer.CurrentlyPlaying is { } curPlayable) UpdateNowPlayingInfo(curPlayable);
    }

    public void DidNowPlayingInfoChange()
    {
        if (_musicPlayer.CurrentlyPlaying is { } curPlayable) UpdateNowPlayingInfo(curPlayable);
    }

    public void Dispose() => _downloadFinishedRegistration.Dispose();
}
