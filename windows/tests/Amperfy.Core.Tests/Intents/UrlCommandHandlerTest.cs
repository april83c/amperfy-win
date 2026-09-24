using Amperfy.Core.Intents;
using Amperfy.Core.Player;
using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Tests.Player;
using LibrarySyncerProxy = Amperfy.Core.Tests.Player.LibrarySyncerProxy;
using static Amperfy.Core.Tests.Player.TestUtil;

namespace Amperfy.Core.Tests.Intents;

public class UrlCommandHandlerTest : IDisposable
{
    private readonly CoreDataHelper cdHelper;
    private readonly LibraryStorage library;
    private readonly Account account;
    private readonly AmperfySettings settings;
    private readonly PlayerComponents components;
    private readonly LibrarySyncerProxy syncerRecorder;
    private readonly UrlCommandHandler handler;

    private IPlayerFacade Player => components.Player;

    public UrlCommandHandlerTest()
    {
        cdHelper = new CoreDataHelper();
        library = cdHelper.CreateSeededStorage();
        account = library.GetAccount(TestAccountInfo.Create1());
        settings = new AmperfySettings();
        var engine = new MockAudioStreamingPlayer();
        (var librarySyncer, syncerRecorder) = LibrarySyncerProxy.Create();
        var backendApi = new MockBackendApi();
        components = PlayerFactory.Create(library, settings, new EventLogger(library), new AlwaysOnlineNetworkMonitor(), () => engine,
            _ => backendApi, _ => new MockSongDownloader(), _ => librarySyncer, new UserStatistics(), new EventNotificationHandler());
        components.BackendAudioPlayer.TimerFactory = (_, _) => new NoopDisposable();
        handler = new UrlCommandHandler(library, settings, Player, new AlwaysOnlineNetworkMonitor(), () => account, _ => librarySyncer);
    }

    public void Dispose() => components.Dispose();

    private Task<UrlCommandResult> Handle(string url) => handler.HandleAsync(new Uri(url));

    [Fact]
    public void ParseQuery_DecodesValues()
    {
        var q = UrlCommandHandler.ParseQuery("?searchTerm=Hello%20World&x=a+b&flag");
        Assert.Equal("Hello World", q["searchTerm"]);
        Assert.Equal("a b", q["x"]);
        Assert.Equal("", q["flag"]);
    }

    [Fact]
    public void RejectsForeignUrls() => Run(async () =>
    {
        var result = await Handle("https://example.com/play");
        Assert.False(result.Success);
        result = await Handle("amperfy://x-callback-url/unknownAction");
        Assert.False(result.Success);
        Assert.Contains("Unknown action", result.ErrorMessage);
    });

    [Fact]
    public void PlayId_PlaysAlbumWithOptions() => Run(async () =>
    {
        var album = NN(library.GetAlbums(account).FirstOrDefault(a => a.Songs.Count > 1));
        var result = await Handle($"amperfy://x-callback-url/playID?id={Uri.EscapeDataString(album.Id)}&libraryElementType=album&repeatOption=1");
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(RepeatMode.All, Player.RepeatMode);
        Assert.Equal(album.Songs.Count, Player.PrevQueueCount + Player.NextQueueCount + 1);
    });

    [Fact]
    public void PlayId_MissingParameters() => Run(async () =>
    {
        Assert.Equal("Parameter id not provided.", (await Handle("amperfy://x-callback-url/playID?libraryElementType=album")).ErrorMessage);
        Assert.Equal("Parameter libraryElementType is not valid.", (await Handle("amperfy://x-callback-url/playID?id=1&libraryElementType=x")).ErrorMessage);
        Assert.Equal("Requested element could not be played.", (await Handle("amperfy://x-callback-url/playID?id=doesnotexist&libraryElementType=album")).ErrorMessage);
    });

    [Fact]
    public void SearchAndPlay_FindsArtist() => Run(async () =>
    {
        var artist = NN(library.GetArtists(account).FirstOrDefault(a => a.Songs.Count > 0));
        var result = await Handle($"amperfy://x-callback-url/searchAndPlay?searchTerm={Uri.EscapeDataString(artist.Name)}&searchCategory=artist&shuffleOption=1");
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(Player.IsShuffle);
        Assert.NotNull(Player.CurrentlyPlaying);
    });

    [Fact]
    public void ShuffleRepeatOfflineMode() => Run(async () =>
    {
        Assert.True((await Handle("amperfy://x-callback-url/setShuffle?shuffleOption=1")).Success);
        Assert.True(Player.IsShuffle);
        Assert.True((await Handle("amperfy://x-callback-url/setShuffle?shuffleOption=1")).Success);
        Assert.True(Player.IsShuffle);
        Assert.False((await Handle("amperfy://x-callback-url/setShuffle?shuffleOption=3")).Success);
        Assert.True((await Handle("amperfy://x-callback-url/setRepeat?repeatOption=2")).Success);
        Assert.Equal(RepeatMode.Single, Player.RepeatMode);
        Assert.True((await Handle("amperfy://x-callback-url/setOfflineMode?offlineMode=1")).Success);
        Assert.True(settings.User.IsOfflineMode);
        var rating = await Handle("amperfy://x-callback-url/rateCurrentlyPlayingSong?rating=3");
        Assert.Equal("Rating can only be changed in Online Mode.", rating.ErrorMessage);
    });

    [Fact]
    public void RateCurrentlyPlayingSong_CallsSyncer() => Run(async () =>
    {
        var song = NN(library.GetSongs(account).FirstOrDefault());
        Player.Play(new PlayContext("", [song]));
        var result = await Handle("amperfy://x-callback-url/rateCurrentlyPlayingSong?rating=4");
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(4, song.Rating);
        Assert.Contains(syncerRecorder.Calls, c => c.Method == nameof(ILibrarySyncer.SetRatingAsync));
    });

    [Fact]
    public void Callbacks_AreReturned() => Run(async () =>
    {
        var ok = await Handle("amperfy://x-callback-url/play?x-success=" + Uri.EscapeDataString("myapp://done"));
        Assert.Equal(new Uri("myapp://done"), ok.CallbackUrl);
        var failed = await Handle("amperfy://x-callback-url/setRepeat?x-error=" + Uri.EscapeDataString("myapp://failed"));
        Assert.StartsWith("myapp://failed", failed.CallbackUrl!.ToString());
        Assert.Contains("errorMessage=Parameter%20repeatOption%20not%20provided.", failed.CallbackUrl.AbsoluteUri);
    });

    [Fact]
    public void Documentation_CoversAllActions()
    {
        Assert.Equal(13, handler.Documentation.Count);
        Assert.All(handler.Documentation, d => Assert.NotEmpty(d.ExampleUrls));
    }
}
