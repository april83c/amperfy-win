using Amperfy.Core.Api;
using Amperfy.Core.Common;
using Amperfy.Core.Storage;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Amperfy.App.Services.Audio;

/// A <see cref="MediaSource"/> together with the resources that have to be released with it.
internal sealed class AudioSourceHandle : IDisposable
{
    private readonly IDisposable[] _resources;
    private bool _disposed;

    public AudioSourceHandle(MediaSource source, params IDisposable[] resources)
    {
        Source = source;
        _resources = resources;
    }

    public MediaSource Source { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Source.Dispose(); } catch (Exception) { /* already closed */ }
        foreach (var r in _resources)
        {
            try { r.Dispose(); } catch (Exception) { /* ignore */ }
        }
    }
}

/// Creates media sources for the engine entries:
/// * file:// URIs of cached files (via StorageFile; files with an unknown extension are opened as a stream
///   with the playable's MIME type),
/// * http(s) streams without custom headers: <see cref="MediaSource.CreateFromUri"/> (Media Foundation's
///   network source, supports range requests, radio streams, …),
/// * http(s) streams with custom HTTP headers (e.g. Cloudflare Access): the headers can't be passed to
///   Media Foundation, so the stream is read through <see cref="HttpRangeStream"/> (range requests with the
///   headers) wrapped as an IRandomAccessStream. That requires a seekable stream (known length); if the server
///   doesn't provide a length (e.g. live transcoding without estimated content length) the URI is used without
///   the headers as a fallback.
internal static class AudioSourceFactory
{
    private const string Log = "AudioSource";
    private const string DefaultMimeType = "audio/mpeg";

    public static async Task<AudioSourceHandle> CreateAsync(string url, string? mimeType, IReadOnlyDictionary<string, string>? headers,
        CancellationToken ct)
    {
        var uri = new Uri(url);
        if (uri.IsFile) return await CreateFromFileAsync(uri.LocalPath, mimeType);

        if (headers is { Count: > 0 } && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var rangeStream = new HttpRangeStream(AmperfyHttp.Client, uri, headers);
            try
            {
                await rangeStream.InitializeAsync(ct);
            }
            catch
            {
                rangeStream.Dispose();
                throw;
            }
            if (rangeStream.CanSeek)
            {
                var synchronized = new SynchronizedReadStream(rangeStream);
                var randomAccessStream = synchronized.AsRandomAccessStream();
                var contentType = NormalizeMimeType(mimeType ?? rangeStream.ContentType);
                return new AudioSourceHandle(MediaSource.CreateFromStream(randomAccessStream, contentType), randomAccessStream, synchronized);
            }
            rangeStream.Dispose();
            AmperfyLog.Warning(Log, $"Stream of {uri.Host} has no known length: playing it without the custom HTTP headers");
        }
        return new AudioSourceHandle(MediaSource.CreateFromUri(uri));
    }

    private static async Task<AudioSourceHandle> CreateFromFileAsync(string path, string? mimeType)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var extensionMime = MimeFileConverter.GetMimeType(Path.GetExtension(path));
        if (extensionMime is not null && extensionMime != MimeFileConverter.MimeTypeUnknown)
        {
            return new AudioSourceHandle(MediaSource.CreateFromStorageFile(file));
        }
        // Unknown file extension: Media Foundation needs the content type to pick the decoder.
        IRandomAccessStreamWithContentType stream = await file.OpenReadAsync();
        return new AudioSourceHandle(MediaSource.CreateFromStream(stream, NormalizeMimeType(mimeType)), stream);
    }

    private static string NormalizeMimeType(string? mimeType) =>
        string.IsNullOrWhiteSpace(mimeType) || mimeType == MimeFileConverter.MimeTypeUnknown
            ? DefaultMimeType
            : MimeFileConverter.ConvertToValidMimeTypeWhenNecessary(mimeType.Split(';')[0].Trim());
}

/// Serializes the access to a (not thread safe) read-only stream: Media Foundation reads from its worker threads.
internal sealed class SynchronizedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SynchronizedReadStream(Stream inner) => _inner = inner;

    public override bool CanRead => true;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set
        {
            _lock.Wait();
            try { _inner.Position = value; }
            finally { _lock.Release(); }
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        _lock.Wait();
        try { return _inner.Read(buffer, offset, count); }
        finally { _lock.Release(); }
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        await ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); }
        finally { _lock.Release(); }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _lock.Wait();
        try { return _inner.Seek(offset, origin); }
        finally { _lock.Release(); }
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
