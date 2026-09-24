namespace Amperfy.Core.Api;

/// Base of the Subsonic and Ampache library syncers.
public abstract class CommonLibrarySyncer
{
    protected Account Account { get; }
    protected INetworkMonitor NetworkMonitor { get; }
    protected LibraryStorage Library { get; }
    protected EventLogger EventLogger { get; }
    protected CacheFileManager FileManager => CacheFileManager.Shared;

    protected bool IsSyncAllowed => NetworkMonitor.IsConnectedToNetwork;

    protected AccountInfo AccountInfo => Account.Info;

    protected CommonLibrarySyncer(Account account, INetworkMonitor networkMonitor, LibraryStorage library, EventLogger eventLogger)
    {
        Account = account;
        NetworkMonitor = networkMonitor;
        Library = library;
        EventLogger = eventLogger;
    }

    /// Recreates database entries for files that are already in the cache (e.g. after a re-sync).
    protected void CreateCachedItemRepresentations(ISyncCallbacks? statusNotifier)
    {
        var info = Account.Info;
        var cachedArtworks = FileManager.GetCachedArtworks(info);
        var cachedEmbeddedArtworks = FileManager.GetCachedEmbeddedArtworks(info);
        var cachedLyrics = FileManager.GetCachedLyrics(info);
        var cachedSongs = FileManager.GetCachedSongs(info);
        var cachedEpisodes = FileManager.GetCachedEpisodes(info);
        var totalCount = cachedArtworks.Count + cachedEmbeddedArtworks.Count + cachedLyrics.Count + cachedSongs.Count + cachedEpisodes.Count;
        if (totalCount == 0) return;
        statusNotifier?.NotifySyncStarted(ParsedObjectType.Cache, totalCount);

        foreach (var cached in cachedArtworks)
        {
            var artwork = Library.GetArtwork(Account, new ArtworkRemoteInfo(cached.Id, cached.Type)) ?? Library.CreateArtwork(Account);
            artwork.Id = cached.Id;
            artwork.Type = cached.Type;
            artwork.RelFilePath = cached.RelFilePath;
            artwork.Status = ImageStatus.CustomImage;
            statusNotifier?.NotifyParsedObject(ParsedObjectType.Cache);
        }
        foreach (var cached in cachedSongs)
        {
            var song = Library.CreateSong(Account);
            song.Id = cached.Id;
            song.RelFilePath = cached.RelFilePath;
            song.ContentTypeTranscoded = cached.MimeType;
            statusNotifier?.NotifyParsedObject(ParsedObjectType.Cache);
        }
        foreach (var cached in cachedEpisodes)
        {
            var episode = Library.CreatePodcastEpisode(Account);
            episode.Id = cached.Id;
            episode.RelFilePath = cached.RelFilePath;
            episode.ContentTypeTranscoded = cached.MimeType;
            statusNotifier?.NotifyParsedObject(ParsedObjectType.Cache);
        }
        Library.SaveContext();
        // match embedded artworks after songs/episodes so that owners are already created
        foreach (var cached in cachedEmbeddedArtworks)
        {
            AbstractPlayable? owner = cached.IsSong ? Library.GetSong(Account, cached.Id) : Library.GetPodcastEpisode(Account, cached.Id);
            if (owner is not null)
            {
                var embedded = Library.CreateEmbeddedArtwork(Account);
                embedded.RelFilePath = cached.RelFilePath;
                embedded.Owner = owner;
            }
            statusNotifier?.NotifyParsedObject(ParsedObjectType.Cache);
        }
        foreach (var cached in cachedLyrics)
        {
            var song = Library.GetSong(Account, cached.Id);
            if (song is null)
            {
                song = Library.CreateSong(Account);
                song.Id = cached.Id;
            }
            song.LyricsRelFilePath = cached.RelFilePath;
            statusNotifier?.NotifyParsedObject(ParsedObjectType.Cache);
        }
        Library.SaveContext();
    }
}

/// Shared HTTP helpers for the server APIs.
public static class AmperfyHttp
{
    private static readonly Lazy<HttpClient> SharedClient = new(() => CreateClient(TimeSpan.FromSeconds(60)));

    /// Shared client for API requests (connection pooling). Per-request headers are set on the request message.
    public static HttpClient Client => SharedClient.Value;

    public static HttpClient CreateClient(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(20),
        };
        var client = new HttpClient(handler) { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Amperfy/{AmperfyInfo.Version} (Windows)");
        return client;
    }

    public static HttpRequestMessage CreateGet(Uri url, IReadOnlyDictionary<string, string>? headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (headers is not null)
        {
            foreach (var (k, v) in headers) request.Headers.TryAddWithoutValidation(k, v);
        }
        return request;
    }
}
