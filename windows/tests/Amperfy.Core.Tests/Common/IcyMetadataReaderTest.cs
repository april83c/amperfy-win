using System.Globalization;
using System.Text;
using System.Net;

namespace Amperfy.Core.Tests.Common;

public class IcyMetadataReaderTest
{
    private sealed class IcyHandler(byte[] body, int? metaInt) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
            if (metaInt is { } i) response.Headers.TryAddWithoutValidation("icy-metaint", i.ToString(CultureInfo.InvariantCulture));
            return Task.FromResult(response);
        }
    }

    private static byte[] CreateStream(int metaInt, string metadata)
    {
        var audio = Enumerable.Repeat((byte)0xAB, metaInt).ToArray();
        var meta = Encoding.UTF8.GetBytes(metadata);
        var blocks = (meta.Length + 15) / 16;
        var metaBlock = new byte[blocks * 16];
        meta.CopyTo(metaBlock, 0);
        return [.. audio, (byte)blocks, .. metaBlock, .. audio];
    }

    [Fact]
    public void ParsesStreamTitle()
    {
        var result = IcyMetadataReader.ParseMetadataBlock("StreamTitle='Artist - Title';StreamUrl='http://x.y';");
        Assert.Equal("Artist - Title", result["StreamTitle"]);
        Assert.Equal("http://x.y", result["streamurl"]);
    }

    [Fact]
    public void ParsesValuesWithQuotesAndSemicolons()
    {
        var result = IcyMetadataReader.ParseMetadataBlock("StreamTitle='It's; a - Song';\0\0\0");
        Assert.Equal("It's; a - Song", result["StreamTitle"]);
        var unterminated = IcyMetadataReader.ParseMetadataBlock("StreamTitle='Only Title'");
        Assert.Equal("Only Title", unterminated["StreamTitle"]);
    }

    [Fact]
    public void DecodesLatin1Fallback()
    {
        var latin1 = Encoding.Latin1.GetBytes("StreamTitle='Motörhead';");
        Assert.Equal("StreamTitle='Motörhead';", IcyMetadataReader.DecodeMetadata(latin1));
        var utf8 = Encoding.UTF8.GetBytes("StreamTitle='Motörhead';\0\0");
        Assert.Equal("StreamTitle='Motörhead';", IcyMetadataReader.DecodeMetadata(utf8));
    }

    [Fact]
    public async Task ReadsFirstMetadataBlock()
    {
        var handler = new IcyHandler(CreateStream(1000, "StreamTitle='Band - Song';"), 1000);
        var (status, metadata) = await IcyMetadataReader.ReadOnceAsync(new HttpClient(handler), new Uri("http://radio.example/stream"),
            new Dictionary<string, string> { ["X-Test"] = "1" });
        Assert.Equal(IcyMetadataReader.ReadStatus.Success, status);
        Assert.Equal("Band - Song", metadata["StreamTitle"]);
        Assert.True(handler.LastRequest!.Headers.Contains("Icy-MetaData"));
        Assert.True(handler.LastRequest!.Headers.Contains("X-Test"));
    }

    [Fact]
    public async Task ReportsNotSupportedWithoutMetaInt()
    {
        var handler = new IcyHandler(new byte[100], null);
        var (status, metadata) = await IcyMetadataReader.ReadOnceAsync(new HttpClient(handler), new Uri("http://radio.example/stream"));
        Assert.Equal(IcyMetadataReader.ReadStatus.NotSupported, status);
        Assert.Empty(metadata);
    }

    [Fact]
    public async Task PollerReportsMetadata()
    {
        var handler = new IcyHandler(CreateStream(64, "StreamTitle='A - B';"), 64);
        using var poller = new IcyMetadataPoller { Interval = TimeSpan.FromMilliseconds(20) };
        var tcs = new TaskCompletionSource<IReadOnlyDictionary<string, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        poller.Start(new HttpClient(handler), new Uri("http://radio.example/stream"), null, m => tcs.TrySetResult(m));
        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("A - B", result["StreamTitle"]);
    }
}
