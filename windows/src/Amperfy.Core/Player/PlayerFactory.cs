using Amperfy.Core.Api;
using Amperfy.Core.Downloads;

namespace Amperfy.Core.Player;

/// Everything created by <see cref="PlayerFactory.Create"/>. Keep this object alive as long as the player is
/// used (the observers are registered as weak references like in Swift).
public sealed class PlayerComponents : IDisposable
{
    public required IPlayerFacade Player { get; init; }
    public required AudioPlayer MusicPlayer { get; init; }
    public required BackendAudioPlayer BackendAudioPlayer { get; init; }
    public required PlayerData PlayerData { get; init; }
    public required PlayQueueHandler QueueHandler { get; init; }
    public required PlayerDownloadPreparationHandler DownloadPreparationHandler { get; init; }
    public required PlayerNotificationAdapter NotificationAdapter { get; init; }
    public required SleepTimer SleepTimer { get; init; }
    public NowPlayingInfoHandler? NowPlayingInfoHandler { get; init; }
    public RemoteCommandHandler? RemoteCommandHandler { get; init; }

    public void Dispose()
    {
        SleepTimer.Dispose();
        NowPlayingInfoHandler?.Dispose();
    }
}

/// Composition of the player (port of AmperKit.createPlayer() in AmperfyKit.swift).
public static class PlayerFactory
{
    public static PlayerComponents Create(
        LibraryStorage library,
        AmperfySettings settings,
        EventLogger eventLogger,
        INetworkMonitor networkMonitor,
        Func<IAudioStreamingPlayer> createAudioStreamingPlayer,
        Func<AccountInfo, IBackendApi> getBackendApi,
        Func<AccountInfo, IDownloadManageable> getPlayableDownloader,
        Func<AccountInfo, ILibrarySyncer> getLibrarySyncer,
        UserStatistics userStatistics,
        EventNotificationHandler notificationHandler,
        ISystemMediaControls? systemMediaControls = null,
        Func<AccountInfo, IDownloadManageable>? getArtworkDownloader = null)
    {
        var backendAudioPlayer = new BackendAudioPlayer(createAudioStreamingPlayer, eventLogger, getBackendApi, networkMonitor,
            getPlayableDownloader, library, userStatistics);

        backendAudioPlayer.SetStreamingMaxBitrates(new StreamingMaxBitrates(
            settings.User.StreamingMaxBitrateWifiPreference,
            settings.User.StreamingMaxBitrateCellularPreference));
        backendAudioPlayer.SetStreamingTranscodings(new StreamingTranscodings(
            settings.User.StreamingFormatWifiPreference,
            settings.User.StreamingFormatCellularPreference));

        var playerData = library.GetPlayerData();
        var queueHandler = new PlayQueueHandler(playerData);
        var curPlayer = new AudioPlayer(playerData, queueHandler, backendAudioPlayer, settings, userStatistics);
        backendAudioPlayer.TriggerReinsertPlayableCB = curPlayer.Play;
        curPlayer.AutoplayCB = async song =>
        {
            if (song.Account?.Info is not { } accountInfo) return [];
            return await getLibrarySyncer(accountInfo).RequestSimilarSongsAsync(song, 99);
        };
        backendAudioPlayer.UpdateEqualizerEnabled(settings.User.IsEqualizerEnabled);
        backendAudioPlayer.UpdateEqualizerSetting(settings.User.ActiveEqualizerSetting);
        backendAudioPlayer.UpdateReplayGainEnabled(settings.User.IsReplayGainEnabled);
        backendAudioPlayer.Volume = settings.User.PlayerVolume;

        var downloadPreparationHandler = new PlayerDownloadPreparationHandler(playerData, queueHandler, getPlayableDownloader);
        curPlayer.AddNotifier(downloadPreparationHandler);

        var facadeImpl = new PlayerFacadeImpl(playerData, queueHandler, curPlayer, library, backendAudioPlayer, userStatistics)
        {
            IsOfflineMode = settings.User.IsOfflineMode,
        };

        NowPlayingInfoHandler? nowPlayingInfoHandler = null;
        RemoteCommandHandler? remoteCommandHandler = null;
        if (systemMediaControls is not null)
        {
            nowPlayingInfoHandler = new NowPlayingInfoHandler(curPlayer, backendAudioPlayer, systemMediaControls, settings,
                notificationHandler, getArtworkDownloader);
            curPlayer.AddNotifier(nowPlayingInfoHandler);
            remoteCommandHandler = new RemoteCommandHandler(facadeImpl, getLibrarySyncer, eventLogger, library, systemMediaControls);
            remoteCommandHandler.ConfigureRemoteCommands();
            curPlayer.AddNotifier(remoteCommandHandler);
        }

        var notificationAdapter = new PlayerNotificationAdapter(notificationHandler);
        curPlayer.AddNotifier(notificationAdapter);

        return new PlayerComponents
        {
            Player = facadeImpl,
            MusicPlayer = curPlayer,
            BackendAudioPlayer = backendAudioPlayer,
            PlayerData = playerData,
            QueueHandler = queueHandler,
            DownloadPreparationHandler = downloadPreparationHandler,
            NotificationAdapter = notificationAdapter,
            SleepTimer = new SleepTimer(facadeImpl, eventLogger),
            NowPlayingInfoHandler = nowPlayingInfoHandler,
            RemoteCommandHandler = remoteCommandHandler,
        };
    }
}
