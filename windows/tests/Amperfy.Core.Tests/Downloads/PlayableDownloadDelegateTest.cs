using System.Text;
using Amperfy.Core.Downloads;

namespace Amperfy.Core.Tests.Downloads;

[Collection(DownloadTestCollection.Name)]
public class PlayableDownloadDelegateTest : IDisposable
{
    private readonly DownloadTestContext _c = new();
    private readonly IBackendApi _api;
    private readonly InterfaceFake<IBackendApi> _apiFake;
    private readonly PlayableDownloadDelegate _delegate;
    private readonly List<AbstractPlayableInfo> _urlRequests = [];
    private readonly List<ApiDataResponse> _checkedResponses = [];

    public PlayableDownloadDelegateTest()
    {
        (_api, _apiFake) = InterfaceFake<IBackendApi>.Create();
        _apiFake.Handlers[nameof(IBackendApi.GenerateUrlForDownloadingPlayableAsync)] = args =>
        {
            var info = (AbstractPlayableInfo)args[0]!;
            _urlRequests.Add(info);
            return Task.FromResult(new Uri($"https://server.test/download?id={info.Id}"));
        };
        _apiFake.Handlers[nameof(IBackendApi.CheckForErrorResponse)] = args =>
        {
            var response = (ApiDataResponse)args[0]!;
            _checkedResponses.Add(response);
            var text = Encoding.UTF8.GetString(response.Data);
            return text.Contains("<error") ? new ResponseError(ResponseErrorType.Api, 70, "not found") : null;
        };
        _apiFake.Handlers[nameof(IUrlCleanser.Cleanse)] = args => new CleansedUrl(((Uri?)args[0])?.ToString() ?? "");
        _apiFake.Handlers["get_" + nameof(IBackendApi.HttpHeaders)] = _ => new Dictionary<string, string> { ["Authorization"] = "Bearer x" };
        _delegate = new PlayableDownloadDelegate(_api, new EmbeddedArtworkExtractor(_c.FileManager), _c.Network, _c.FileManager);
    }

    public void Dispose() => _c.Dispose();

    private string WriteTemp(byte[] data)
    {
        var path = _c.FileManager.CreateTempFilePathWithDirectory();
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public void Properties()
    {
        Assert.Equal(4, _delegate.ParallelDownloadsCount);
        Assert.Equal("Bearer x", _delegate.HttpHeaders["Authorization"]);
        Assert.Equal(DownloadableType.Playable, PlayableDownloadDelegate.RequestType);
    }

    [Fact]
    public void PrepareDownloadGeneratesUrlViaBackend()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var song = _c.CreateSong("s1");
            var url = await _delegate.PrepareDownloadAsync(song, _c.Library);
            Assert.Equal(new Uri("https://server.test/download?id=s1"), url);
            var info = Assert.Single(_urlRequests);
            Assert.Equal(song.Pk, info.Pk);
            Assert.Equal(DerivedPlayableType.Song, info.Type);
        });
    }

    [Fact]
    public void PrepareDownloadErrors()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var artwork = _c.Library.CreateArtwork(_c.Account);
            _c.Library.SaveContext();
            var ex1 = await Assert.ThrowsAsync<DownloadException>(() => _delegate.PrepareDownloadAsync(artwork, _c.Library));
            Assert.Equal(DownloadError.FetchFailed, ex1.Error);

            var cached = _c.CreateSong("cached");
            cached.RelFilePath = "x.mp3";
            var ex2 = await Assert.ThrowsAsync<DownloadException>(() => _delegate.PrepareDownloadAsync(cached, _c.Library));
            Assert.Equal(DownloadError.AlreadyDownloaded, ex2.Error);

            _c.Network.IsConnectedToNetwork = false;
            var ex3 = await Assert.ThrowsAsync<DownloadException>(() => _delegate.PrepareDownloadAsync(_c.CreateSong("s1"), _c.Library));
            Assert.Equal(DownloadError.NoConnectivity, ex3.Error);
            Assert.Empty(_urlRequests);
        });
    }

    [Theory]
    [InlineData("audio/mpeg", "mp3")]
    [InlineData("audio/flac", "flac")]
    [InlineData("audio/x-flac", "flac")]
    [InlineData("audio/mp4", "m4a")]
    [InlineData("audio/ogg; codecs=opus", "ogg")]
    [InlineData("application/x-something", "unknown")]
    public void SongIsStoredByMimeType(string mimeType, string extension)
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var song = _c.CreateSong("song/1", contentType: "audio/wav");
            var temp = WriteTemp([1, 2, 3]);
            await _delegate.CompletedDownloadAsync(song, temp, mimeType, _c.Library);

            Assert.Equal(mimeType, song.ContentTypeTranscoded);
            Assert.Equal($"{CacheFileManager.GetRelSongsDirectory(_c.Account.Info)}/song_1.{extension}", song.RelFilePath);
            Assert.True(song.IsCached);
            Assert.False(File.Exists(temp));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(_c.FileManager.GetAbsolutePath(song.RelFilePath!)));
            Assert.Null(song.EmbeddedArtwork); // not an audio file with tags
        });
    }

    [Fact]
    public void MissingMimeTypeFallsBackToOriginalContentType()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var song = _c.CreateSong("s1", contentType: "audio/flac");
            await _delegate.CompletedDownloadAsync(song, WriteTemp([1]), null, _c.Library);
            Assert.Null(song.ContentTypeTranscoded);
            Assert.EndsWith("/s1.flac", song.RelFilePath);
            Assert.Equal("audio/flac", song.FileContentType);
        });
    }

    [Fact]
    public void EpisodeIsStoredInEpisodesDirectory()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var episode = _c.CreateEpisode("e1");
            await _delegate.CompletedDownloadAsync(episode, WriteTemp([1, 2]), "audio/mpeg", _c.Library);
            Assert.Equal(CacheFileManager.GetRelEpisodeFilePath(_c.Account.Info, "e1", "audio/mpeg"), episode.RelFilePath);
            Assert.Contains($"/{CacheFileManager.EpisodesDir}/", episode.RelFilePath);
        });
    }

    [Fact]
    public void CompletedDownloadExtractsEmbeddedArtwork()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var song = _c.CreateSong("s1");
            var temp = WriteTemp(TestMediaFiles.CreateMp3((TagLib.PictureType.FrontCover, "image/jpeg", TestMediaFiles.JpegBytes)));
            await _delegate.CompletedDownloadAsync(song, temp, "audio/mpeg", _c.Library);
            Assert.True(song.IsCached);
            Assert.NotNull(song.EmbeddedArtwork);
            Assert.Equal(TestMediaFiles.JpegBytes, File.ReadAllBytes(song.EmbeddedArtwork.ImagePath!));
        });
    }

    [Fact]
    public void ValidateDownloadedData()
    {
        var url = new Uri("https://server.test/download?id=1");
        var invalid = _delegate.ValidateDownloadedData(null, url);
        Assert.NotNull(invalid);
        Assert.Equal("Invalid download", invalid.ErrorMessage);
        Assert.Equal(url.ToString(), invalid.CleansedUrl?.Description);

        // small file -> checked by the API
        var errorFile = WriteTemp(Encoding.UTF8.GetBytes("<subsonic-response status=\"failed\"><error code=\"70\"/></subsonic-response>"));
        var error = _delegate.ValidateDownloadedData(errorFile, url);
        Assert.Equal(70, error?.StatusCode);
        Assert.Same(url, Assert.Single(_checkedResponses).Url);

        var okFile = WriteTemp([1, 2, 3]);
        Assert.Null(_delegate.ValidateDownloadedData(okFile, url));

        // files of 2000 bytes and more can't be an error response -> not checked
        _checkedResponses.Clear();
        var bigFile = WriteTemp(Encoding.UTF8.GetBytes("<error" + new string(' ', 2000)));
        Assert.Null(_delegate.ValidateDownloadedData(bigFile, url));
        Assert.Empty(_checkedResponses);
    }

    [Fact]
    public void DownloadManagerWithPlayableDelegateEndToEnd()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            var mp3 = TestMediaFiles.CreateMp3((TagLib.PictureType.FrontCover, "image/png", TestMediaFiles.PngBytes));
            _c.Http.Handler = (_, _) => Task.FromResult(FakeHttpMessageHandler.Ok(mp3, "audio/mpeg"));
            var song = _c.CreateSong("s1");
            using var manager = _c.CreateManager(_delegate);

            manager.Download(song);
            await manager.WhenIdleAsync();

            Assert.True(song.IsCached);
            Assert.EndsWith("s1.mp3", song.RelFilePath);
            Assert.Equal(mp3, File.ReadAllBytes(_c.FileManager.GetAbsolutePath(song.RelFilePath!)));
            Assert.Equal(TestMediaFiles.PngBytes, File.ReadAllBytes(song.EmbeddedArtwork!.ImagePath!));
            Assert.True(song.Download!.IsFinishedSuccessfully);
            Assert.Equal(new Uri("https://server.test/download?id=s1"), Assert.Single(_c.Http.RequestedUrls));
            Assert.Equal("Bearer x", Assert.Single(_c.Http.Requests).Headers.Authorization?.ToString());
        });
    }

    [Fact]
    public void DownloadManagerWithPlayableDelegateApiError()
    {
        SingleThreadSynchronizationContext.Run(async () =>
        {
            _c.Http.Handler = (_, _) => Task.FromResult(FakeHttpMessageHandler.Ok(Encoding.UTF8.GetBytes("<error code=\"70\"/>"), "text/xml"));
            var song = _c.CreateSong("s1");
            using var manager = _c.CreateManager(_delegate);
            manager.Download(song);
            await manager.WhenIdleAsync();
            Assert.False(song.IsCached);
            Assert.Equal(DownloadError.ApiErrorResponse, song.Download!.Error);
        });
    }
}
