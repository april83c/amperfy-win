using System.Net;
using System.Net.Http.Headers;

namespace Amperfy.Core.Common;

/// Read-only, seekable stream over HTTP using range requests. Used to stream audio when custom
/// HTTP headers are required (e.g. Cloudflare Access), which platform media APIs can't add.
/// Data is fetched in blocks and cached; seeking outside the cached window issues a new request.
public sealed class HttpRangeStream : Stream
{
    private readonly HttpClient _client;
    private readonly Uri _url;
    private readonly IReadOnlyDictionary<string, string> _headers;
    private readonly int _blockSize;
    private long _position;
    private long? _length;
    private bool _supportsRanges = true;
    private readonly Dictionary<long, byte[]> _blocks = [];
    private readonly LinkedList<long> _lru = new();
    private const int MaxCachedBlocks = 64;

    // Non-range fallback: sequential response stream
    private Stream? _sequentialStream;
    private long _sequentialPosition;

    public string? ContentType { get; private set; }

    public HttpRangeStream(HttpClient client, Uri url, IReadOnlyDictionary<string, string>? headers = null, int blockSize = 256 * 1024)
    {
        _client = client;
        _url = url;
        _headers = headers ?? new Dictionary<string, string>();
        _blockSize = blockSize;
    }

    /// Issues a first request to determine length, content type and range support.
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        using var request = CreateRequest(0, _blockSize - 1);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        ContentType = response.Content.Headers.ContentType?.MediaType;
        if (response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange is { } range)
        {
            _length = range.Length;
            var data = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            StoreBlock(0, data);
        }
        else
        {
            // Server ignores ranges: fall back to a sequential (forward-only) stream.
            _supportsRanges = false;
            _length = response.Content.Headers.ContentLength;
        }
    }

    private HttpRequestMessage CreateRequest(long from, long to)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _url);
        foreach (var (k, v) in _headers) request.Headers.TryAddWithoutValidation(k, v);
        request.Headers.Range = new RangeHeaderValue(from, to);
        return request;
    }

    private void StoreBlock(long index, byte[] data)
    {
        _blocks[index] = data;
        _lru.Remove(index);
        _lru.AddFirst(index);
        while (_lru.Count > MaxCachedBlocks)
        {
            var last = _lru.Last!.Value;
            _lru.RemoveLast();
            _blocks.Remove(last);
        }
    }

    private async Task<byte[]> GetBlockAsync(long index, CancellationToken ct)
    {
        if (_blocks.TryGetValue(index, out var cached))
        {
            _lru.Remove(index);
            _lru.AddFirst(index);
            return cached;
        }
        var from = index * _blockSize;
        var to = from + _blockSize - 1;
        if (_length is { } len) to = Math.Min(to, len - 1);
        using var request = CreateRequest(from, to);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) return [];
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        StoreBlock(index, data);
        return data;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        await ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return 0;
        if (_length is { } len && _position >= len) return 0;
        if (!_supportsRanges) return await ReadSequentialAsync(buffer, cancellationToken).ConfigureAwait(false);

        var blockIndex = _position / _blockSize;
        var block = await GetBlockAsync(blockIndex, cancellationToken).ConfigureAwait(false);
        var offsetInBlock = (int)(_position - blockIndex * _blockSize);
        if (offsetInBlock >= block.Length) return 0;
        var n = Math.Min(buffer.Length, block.Length - offsetInBlock);
        block.AsMemory(offsetInBlock, n).CopyTo(buffer);
        _position += n;
        return n;
    }

    private async Task<int> ReadSequentialAsync(Memory<byte> buffer, CancellationToken ct)
    {
        if (_sequentialStream is null || _sequentialPosition != _position)
        {
            _sequentialStream?.Dispose();
            var request = new HttpRequestMessage(HttpMethod.Get, _url);
            foreach (var (k, v) in _headers) request.Headers.TryAddWithoutValidation(k, v);
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            _sequentialStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            _sequentialPosition = 0;
            // skip to the requested position
            var toSkip = _position;
            var skipBuffer = new byte[81920];
            while (toSkip > 0)
            {
                var r = await _sequentialStream.ReadAsync(skipBuffer.AsMemory(0, (int)Math.Min(skipBuffer.Length, toSkip)), ct).ConfigureAwait(false);
                if (r == 0) return 0;
                toSkip -= r;
                _sequentialPosition += r;
            }
        }
        var n = await _sequentialStream.ReadAsync(buffer, ct).ConfigureAwait(false);
        _position += n;
        _sequentialPosition += n;
        return n;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => (_length ?? throw new NotSupportedException("Unknown length")) + offset,
            _ => _position,
        };
        if (target < 0) throw new IOException("Seek before begin");
        _position = target;
        return _position;
    }

    public override bool CanRead => true;
    public override bool CanSeek => _length.HasValue;
    public override bool CanWrite => false;
    public override long Length => _length ?? throw new NotSupportedException();
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _sequentialStream?.Dispose();
        base.Dispose(disposing);
    }
}
