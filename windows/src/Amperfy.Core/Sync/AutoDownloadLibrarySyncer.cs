using Amperfy.Core.Api;
using Amperfy.Core.Downloads;

namespace Amperfy.Core.Sync;

/// Syncs the newest albums / podcast episodes and downloads the new ones automatically if enabled
/// in the account settings (port of Api/AutoDownloadLibrarySyncer.swift). Main thread.
public sealed class AutoDownloadLibrarySyncer
{
    private const string LogCategory = "AutoDownloadLibrarySyncer";
    private const int NewestPodcastEpisodeCount = 20;

    private readonly LibraryStorage _library;
    private readonly AmperfySettings _settings;
    private readonly Account _account;
    private readonly ILibrarySyncer _librarySyncer;
    private readonly IDownloadManageable _playableDownloadManager;

    public AutoDownloadLibrarySyncer(LibraryStorage library, AmperfySettings settings, Account account, ILibrarySyncer librarySyncer, IDownloadManageable playableDownloadManager)
    {
        _library = library;
        _settings = settings;
        _account = account;
        _librarySyncer = librarySyncer;
        _playableDownloadManager = playableDownloadManager;
    }

    public async Task SyncNewestLibraryElementsAsync(int offset = 0, int count = AmperfyInfo.NewestElementsFetchCount)
    {
        var oldNewestAlbums = _library.GetNewestAlbums(_account, 0, count).ToHashSet();

        await _librarySyncer.SyncNewestAlbumsAsync(offset, count);
        var updatedNewestAlbums = _library.GetNewestAlbums(_account, 0, count).ToHashSet();
        var newNewestAlbums = updatedNewestAlbums.Except(oldNewestAlbums).ToList();
        if (offset == 0)
        {
            AmperfyLog.Info(LogCategory, newNewestAlbums.Count == 0 ? "No new albums" : $"{newNewestAlbums.Count} new albums");
        }

        var fetchNeededNewestAlbums = newNewestAlbums.Where(a => !a.IsSongsMetaDataSynced).ToList();
        // all syncs run interleaved on the main thread (Swift: withThrowingTaskGroup on the MainActor)
        await Task.WhenAll(fetchNeededNewestAlbums.Select(album => _librarySyncer.SyncAsync(album)));

        if (offset == 0 && oldNewestAlbums.Count > 0 && newNewestAlbums.Count > 0 &&
            _settings.Accounts.GetSetting(_account.Info).IsAutoDownloadLatestSongsActive)
        {
            var newestSongs = new List<IDownloadable>();
            foreach (var album in newNewestAlbums) newestSongs.AddRange(album.Songs);
            _playableDownloadManager.Download(newestSongs);
        }
    }

    /// Returns the new synced podcast episodes if an initial sync already occurred.
    /// If this is the initial sync no episodes are returned.
    public async Task<List<PodcastEpisode>> SyncNewestPodcastEpisodesAsync()
    {
        var oldNewestEpisodes = _library.GetNewestPodcastEpisodes(_account, NewestPodcastEpisodeCount).ToHashSet();
        await _librarySyncer.SyncNewestPodcastEpisodesAsync();

        var updatedEpisodes = _library.GetNewestPodcastEpisodes(_account, NewestPodcastEpisodeCount);
        var newAddedNewestEpisodes = updatedEpisodes.Where(e => !oldNewestEpisodes.Contains(e)).ToList();
        if (oldNewestEpisodes.Count > 0 && newAddedNewestEpisodes.Count > 0 &&
            _settings.Accounts.GetSetting(_account.Info).IsAutoDownloadLatestPodcastEpisodesActive)
        {
            _playableDownloadManager.Download(newAddedNewestEpisodes.Cast<IDownloadable>().ToList());
        }
        return oldNewestEpisodes.Count > 0 ? newAddedNewestEpisodes : [];
    }
}
