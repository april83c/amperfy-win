using Amperfy.Core.Api;

namespace Amperfy.Core.Downloads;

/// Creates the playable and artwork download managers of an account like MetaManager.swift
/// (playableDownloadManager / artworkDownloadManager).
public static class DownloadManagerFactory
{
    public const string PlayableDownloaderName = "PlayableDownloader";
    public const string ArtworkDownloaderName = "ArtworkDownloader";

    public static DownloadManager CreatePlayableDownloadManager(
        Account account,
        LibraryStorage library,
        IBackendApi backendApi,
        EventLogger eventLogger,
        AmperfySettings settings,
        INetworkMonitor networkMonitor,
        EventNotificationHandler notificationHandler,
        HttpClient? httpClient = null)
    {
        var playableDownloadDelegate = new PlayableDownloadDelegate(backendApi, new EmbeddedArtworkExtractor(), networkMonitor);
        var manager = new DownloadManager(
            PlayableDownloaderName, account, library, DownloadableType.Playable, () => playableDownloadDelegate,
            eventLogger, settings, networkMonitor, notificationHandler, backendApi,
            limitCacheSize: true, isFailWithPopupError: true, httpClient: httpClient);
        manager.Initialize(isCheckForCachedNeeded: true, validationCallback: null);
        return manager;
    }

    /// <param name="getArtworkDownloadDelegate">Swift: backendApi.getActiveArtworkDownloadDelegate()</param>
    public static DownloadManager CreateArtworkDownloadManager(
        Account account,
        LibraryStorage library,
        IUrlCleanser urlCleanser,
        Func<IDownloadManagerDelegate> getArtworkDownloadDelegate,
        EventLogger eventLogger,
        AmperfySettings settings,
        INetworkMonitor networkMonitor,
        EventNotificationHandler notificationHandler,
        HttpClient? httpClient = null)
    {
        var manager = new DownloadManager(
            ArtworkDownloaderName, account, library, DownloadableType.Artwork, getArtworkDownloadDelegate,
            eventLogger, settings, networkMonitor, notificationHandler, urlCleanser,
            limitCacheSize: false, isFailWithPopupError: false, httpClient: httpClient);
        manager.RequestManager.ClearAllDownloadsIfAllHaveFinished();
        manager.Initialize(isCheckForCachedNeeded: false, validationCallback: CreateArtworkValidation(settings, account.Info));
        return manager;
    }

    /// Filters artwork downloads according to the account's <see cref="ArtworkDownloadSetting"/>
    /// (MetaManager.swift artwork validationCB).
    public static PreDownloadIsValidCallback CreateArtworkValidation(AmperfySettings settings, AccountInfo accountInfo) =>
        downloadables =>
        {
            var artworks = downloadables.OfType<Artwork>().ToList();
            var artworkDownloadSetting = settings.Accounts.GetSetting(accountInfo).ArtworkDownloadSetting;
            if (artworkDownloadSetting == ArtworkDownloadSetting.Never) return [];
            if (artworkDownloadSetting == ArtworkDownloadSetting.UpdateOncePerSession) return artworks;
            // only once
            return artworks.Where(a => a.Status is ImageStatus.FetchError or ImageStatus.NotChecked).ToList<IDownloadable>();
        };
}
