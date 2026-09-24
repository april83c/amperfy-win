using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Amperfy.Core.Downloads;

namespace Amperfy.Core.Tests.Downloads;

/// Download tests use static state (MainThread, CacheFileManager.Shared, DownloadManager.ProgressUpdateInterval)
/// -> run them without any other test in parallel.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DownloadTestCollection
{
    public const string Name = "Downloads (not parallel)";
}

/// HttpMessageHandler with a replaceable response function.
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private int _concurrent;
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Handler { get; set; }
    public List<Uri> RequestedUrls { get; } = [];
    public List<HttpRequestMessage> Requests { get; } = [];
    public int MaxConcurrent { get; private set; }
    public int RequestCount { get; private set; }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => Handler = handler;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (this)
        {
            RequestedUrls.Add(request.RequestUri!);
            Requests.Add(request);
            RequestCount++;
            _concurrent++;
            MaxConcurrent = Math.Max(MaxConcurrent, _concurrent);
        }
        try
        {
            return await Handler(request, cancellationToken);
        }
        finally
        {
            lock (this) _concurrent--;
        }
    }

    public static HttpResponseMessage Ok(byte[] content, string? mimeType = "audio/mpeg")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        if (mimeType is not null) response.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        return response;
    }

    public static byte[] Bytes(int count, byte seed = 1) => Enumerable.Range(0, count).Select(i => (byte)((i + seed) % 251)).ToArray();
}

/// Test download strategy: moves the file into the songs cache (like PlayableDownloadDelegate, without backend).
public sealed class FakeDownloadDelegate : IDownloadManagerDelegate
{
    private readonly CacheFileManager _fileManager;

    public FakeDownloadDelegate(CacheFileManager fileManager) => _fileManager = fileManager;

    public int ParallelDownloadsCount { get; set; } = 2;
    public IReadOnlyDictionary<string, string> HttpHeaders { get; set; } = new Dictionary<string, string>();
    public Func<IDownloadable, Uri> UrlProvider { get; set; } = d => new Uri($"https://download.test/{d.UniqueId()}");
    public Exception? PrepareException { get; set; }
    public Func<string?, Uri?, ResponseError?> Validator { get; set; } = (_, _) => null;
    public List<IDownloadable> Prepared { get; } = [];
    public List<(IDownloadable Element, string? MimeType, byte[] Data)> Completed { get; } = [];
    public List<IDownloadable> Failed { get; } = [];

    public Task<Uri> PrepareDownloadAsync(IDownloadable downloadable, LibraryStorage storage)
    {
        Prepared.Add(downloadable);
        if (PrepareException is { } ex) throw ex;
        return Task.FromResult(UrlProvider(downloadable));
    }

    public ResponseError? ValidateDownloadedData(string? filePath, Uri? downloadUrl) => Validator(filePath, downloadUrl);

    public Task CompletedDownloadAsync(IDownloadable downloadable, string tempFilePath, string? fileMimeType, LibraryStorage storage)
    {
        Completed.Add((downloadable, fileMimeType, File.ReadAllBytes(tempFilePath)));
        if (downloadable is AbstractPlayable playable && playable.Account is { } account)
        {
            playable.ContentTypeTranscoded = fileMimeType;
            var rel = _fileManager.CreateRelPath(playable)!;
            _fileManager.MoveItemIntoCache(tempFilePath, rel, account.Info);
            playable.RelFilePath = rel;
            storage.SaveContext();
        }
        return Task.CompletedTask;
    }

    public Task FailedDownloadAsync(IDownloadable downloadable, LibraryStorage storage)
    {
        Failed.Add(downloadable);
        return Task.CompletedTask;
    }
}

public sealed class FakeUrlCleanser : IUrlCleanser
{
    public CleansedUrl Cleanse(Uri? url) => new(url?.ToString() ?? "");
}

/// Minimal interface fake based on DispatchProxy: members without handler return default values
/// (Task.CompletedTask for Task, a completed Task&lt;T&gt; with default(T) for Task&lt;T&gt;).
public class InterfaceFake<T> : DispatchProxy where T : class
{
    public Dictionary<string, Func<object?[], object?>> Handlers { get; } = [];
    public List<string> Calls { get; } = [];

    public static (T Proxy, InterfaceFake<T> Fake) Create()
    {
        var proxy = DispatchProxy.Create<T, InterfaceFake<T>>();
        return (proxy, (InterfaceFake<T>)(object)proxy);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var name = targetMethod!.Name;
        lock (Calls) Calls.Add(name);
        if (Handlers.TryGetValue(name, out var handler)) return handler(args ?? []);
        var returnType = targetMethod.ReturnType;
        if (returnType == typeof(void)) return null;
        if (returnType == typeof(Task)) return Task.CompletedTask;
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var value = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType).Invoke(null, [value]);
        }
        if (returnType == typeof(IReadOnlyDictionary<string, string>)) return new Dictionary<string, string>();
        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }
}

/// Storage + download manager set up for tests.
public sealed class DownloadTestContext : IDisposable
{
    public Helper.TestStorage T { get; } = new();
    public LibraryStorage Library => T.Library;
    public Account Account => T.Account;
    public AmperfySettings Settings { get; } = new();
    public CacheFileManager FileManager { get; }
    public AlwaysOnlineNetworkMonitor Network { get; } = new();
    public EventNotificationHandler Notifications { get; } = new();
    public EventLogger EventLogger { get; }
    public FakeHttpMessageHandler Http { get; }
    public HttpClient HttpClient { get; }
    public FakeDownloadDelegate Delegate { get; }
    public List<DownloadNotification> FinishedNotifications { get; } = [];
    private readonly IDisposable _registration;

    public DownloadTestContext()
    {
        FileManager = CacheFileManager.Shared;
        EventLogger = new EventLogger(Library) { SuppressAlerts = true };
        Http = new FakeHttpMessageHandler((_, _) => Task.FromResult(FakeHttpMessageHandler.Ok(FakeHttpMessageHandler.Bytes(100))));
        HttpClient = new HttpClient(Http);
        Delegate = new FakeDownloadDelegate(FileManager);
        _registration = Notifications.Register(AmperfyNotification.DownloadFinishedSuccess, args => FinishedNotifications.Add((DownloadNotification)args.Payload!));
    }

    public DownloadManager CreateManager(IDownloadManagerDelegate? downloadDelegate = null, bool limitCacheSize = true, bool isFailWithPopupError = true,
        DownloadableType type = DownloadableType.Playable, bool start = true)
    {
        var del = downloadDelegate ?? Delegate;
        var manager = new DownloadManager("TestDownloader", Account, Library, type, () => del, EventLogger, Settings, Network,
            Notifications, new FakeUrlCleanser(), limitCacheSize, isFailWithPopupError, HttpClient, FileManager);
        manager.Initialize(isCheckForCachedNeeded: true, validationCallback: null);
        if (start) manager.Start();
        return manager;
    }

    public Song CreateSong(string id, string? contentType = "audio/mpeg")
    {
        var song = Library.CreateSong(Account);
        song.Id = id;
        song.Title = $"Song {id}";
        song.ContentType = contentType;
        Library.SaveContext();
        return song;
    }

    public PodcastEpisode CreateEpisode(string id, string? contentType = "audio/mpeg")
    {
        var episode = Library.CreatePodcastEpisode(Account);
        episode.Id = id;
        episode.Title = $"Episode {id}";
        episode.ContentType = contentType;
        Library.SaveContext();
        return episode;
    }

    public void Dispose()
    {
        _registration.Dispose();
        HttpClient.Dispose();
        T.Dispose();
    }
}
