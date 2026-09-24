using System.Net;
using System.Net.Http.Headers;

namespace Amperfy.Core.Tests.Common;

public class HttpRangeStreamTest
{
    private sealed class RangeHandler(byte[] content, bool supportsRanges) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (!request.Headers.TryGetValues("X-Test", out _)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            if (supportsRanges && request.Headers.Range?.Ranges.FirstOrDefault() is { } r)
            {
                var from = r.From ?? 0;
                var to = Math.Min(r.To ?? content.Length - 1, content.Length - 1);
                var part = content[(int)from..((int)to + 1)];
                var resp = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(part) };
                resp.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, content.Length);
                resp.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
                return Task.FromResult(resp);
            }
            var full = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
            full.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
            return Task.FromResult(full);
        }
    }

    private static readonly byte[] Content = Enumerable.Range(0, 10_000).Select(i => (byte)(i % 251)).ToArray();
    private static readonly Dictionary<string, string> Headers = new() { ["X-Test"] = "1" };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadsWholeContentAndSeeks(bool supportsRanges)
    {
        var handler = new RangeHandler(Content, supportsRanges);
        using var stream = new HttpRangeStream(new HttpClient(handler), new Uri("https://example.com/song.mp3"), Headers, blockSize: 1024);
        await stream.InitializeAsync();
        Assert.Equal("audio/mpeg", stream.ContentType);
        var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        Assert.Equal(Content, ms.ToArray());

        stream.Seek(5000, SeekOrigin.Begin);
        var buf = new byte[100];
        var read = await stream.ReadAsync(buf);
        Assert.True(read > 0);
        Assert.Equal(Content.AsSpan(5000, read).ToArray(), buf.AsSpan(0, read).ToArray());
        if (supportsRanges)
        {
            Assert.Equal(Content.Length, stream.Length);
            Assert.All(handler.Requests, r => Assert.NotNull(r.Headers.Range));
        }
    }
}

public class XmlSniffingTest
{
    [Theory]
    [InlineData("<subsonic-response/>", true)]
    [InlineData("  \r\n<?xml version=\"1.0\"?><a/>", true)]
    [InlineData("ID3\u0003", false)]
    [InlineData("", false)]
    public void LooksLikeXml(string text, bool expected) =>
        Assert.Equal(expected, Amperfy.Core.Api.GenericXmlParser.LooksLikeXml(System.Text.Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void LooksLikeXml_WithBom() =>
        Assert.True(Amperfy.Core.Api.GenericXmlParser.LooksLikeXml([0xEF, 0xBB, 0xBF, (byte)'<', (byte)'a', (byte)'/', (byte)'>']));
}
