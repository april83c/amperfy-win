using Amperfy.Core.Api;
using Amperfy.Core.Tests.Helper;
using Xunit.Abstractions;

namespace Amperfy.Core.Tests.Integration;

/// End-to-end tests against a real Subsonic compatible server. Opt-in:
/// AMPERFY_IT_SERVER=http://127.0.0.1:4040 AMPERFY_IT_USER=admin AMPERFY_IT_PASSWORD=admin
/// (the server should serve windows/tests/TestLibrary). Without the variables the tests do nothing.
[Collection("Integration")]
public class ServerIntegrationTest(ITestOutputHelper output)
{
    private static (string Url, string User, string Password)? Config()
    {
        var url = Environment.GetEnvironmentVariable("AMPERFY_IT_SERVER");
        if (string.IsNullOrEmpty(url)) return null;
        return (url, Environment.GetEnvironmentVariable("AMPERFY_IT_USER") ?? "admin", Environment.GetEnvironmentVariable("AMPERFY_IT_PASSWORD") ?? "admin");
    }

    private sealed class SyncProgress : ISyncCallbacks
    {
        public List<ParsedObjectType> Started { get; } = [];
        public void NotifySyncStarted(ParsedObjectType parsedObjectType, int totalCount) => Started.Add(parsedObjectType);
        public void NotifyParsedObject(ParsedObjectType parsedObjectType) { }
    }

    [Fact]
    public void LoginSyncAndBrowse()
    {
        if (Config() is not { } cfg) return;
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var library = new CoreDataHelper().CreateInMemoryLibrary();
            var settings = new AmperfySettings();
            var logger = new EventLogger(library);
            var network = new AlwaysOnlineNetworkMonitor();
            var credentials = new LoginCredentials(cfg.Url, cfg.User, cfg.Password);

            var proxy = new BackendProxy(network, logger, settings);
            var apiType = await proxy.LoginAsync(BackendApiType.NotDetected, credentials);
            output.WriteLine($"Detected api: {apiType}");
            Assert.NotEqual(BackendApiType.NotDetected, apiType);
            credentials.BackendApi = apiType;
            proxy.SelectedApi = apiType;
            proxy.ProvideCredentials(credentials);

            var account = library.GetAccount(AccountInfo.Create(credentials));
            var syncer = new LibrarySyncerProxy(proxy, account, library);
            var progress = new SyncProgress();
            await syncer.SyncInitialAsync(progress);
            output.WriteLine($"Sync steps: {string.Join(", ", progress.Started)}");

            var artists = library.GetArtists(account);
            var albums = library.GetAlbums(account);
            output.WriteLine($"Artists: {string.Join(", ", artists.Select(a => a.Name))}");
            output.WriteLine($"Albums: {string.Join(", ", albums.Select(a => $"{a.Name} ({a.SongCount})"))}");
            Assert.Contains(artists, a => a.Name == "Aurora Lights");
            Assert.Contains(albums, a => a.Name == "Northern Skies");
            Assert.Contains(artists, a => a.Name == "Björk Sample");

            var album = albums.First(a => a.Name == "Frequencies");
            await syncer.SyncAsync(album);
            library.SaveContext();
            Assert.Equal(4, album.Songs.Count);
            Assert.Equal(["Four Forty", "Octave Up", "Low End", "Middle C"], album.Songs.Select(s => s.Title));
            Assert.All(album.Songs, s => Assert.True(s.Duration > 0));
            Assert.Equal("The Test Tones", album.Songs[0].Artist?.Name);

            var streamUrl = await proxy.GenerateUrlForStreamingPlayableAsync(album.Songs[0].Info, StreamingMaxBitratePreference.NoLimit, StreamingFormatPreference.Raw);
            output.WriteLine($"Stream url: {proxy.Cleanse(streamUrl).Description}");
            using var http = new HttpClient();
            var bytes = await http.GetByteArrayAsync(streamUrl);
            Assert.True(bytes.Length > 10_000);

            var artist = artists.First(a => a.Name == "Aurora Lights");
            await syncer.SyncAsync(artist);
            library.SaveContext();
            Assert.Equal(2, library.GetAlbums(account, artist).Count);

            // download (cache) a song incl. embedded artwork extraction and server artwork
            var notifications = new EventNotificationHandler();
            using var downloads = Amperfy.Core.Downloads.DownloadManagerFactory.CreatePlayableDownloadManager(account, library, proxy, logger, settings, network, notifications);
            downloads.Initialize(isCheckForCachedNeeded: true, validationCallback: null);
            downloads.Start();
            var songToCache = album.Songs[1];
            downloads.Download(songToCache);
            for (var i = 0; i < 100 && !songToCache.IsCached; i++) await Task.Delay(100);
            library.SaveContext();
            output.WriteLine($"Cached: {songToCache.RelFilePath}, embedded artwork: {songToCache.EmbeddedArtwork?.RelFilePath}");
            Assert.True(songToCache.IsCached);
            Assert.True(File.Exists(library.GetFilePath(songToCache)));
            Assert.NotNull(songToCache.EmbeddedArtwork);

            if (songToCache.Artwork is { } artwork)
            {
                using var artworkDownloads = Amperfy.Core.Downloads.DownloadManagerFactory.CreateArtworkDownloadManager(account, library, proxy, proxy.GetActiveArtworkDownloadDelegate, logger, settings, network, notifications);
                artworkDownloads.Initialize(isCheckForCachedNeeded: false, validationCallback: null);
                artworkDownloads.Start();
                artworkDownloads.Download(artwork);
                for (var i = 0; i < 100 && artwork.Status != ImageStatus.CustomImage; i++) await Task.Delay(100);
                output.WriteLine($"Artwork: {artwork.Status} {artwork.ImagePath}");
                Assert.Equal(ImageStatus.CustomImage, artwork.Status);
            }

            var random = library.CreatePlaylist(account);
            await syncer.RequestRandomSongsAsync(random, 5);
            output.WriteLine($"Random songs: {random.Playables.Count}");
        });
    }
}
