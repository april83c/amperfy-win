using System.Net.Http.Headers;

namespace Amperfy.Core.Common;

/// Reads ICY (SHOUTcast/Icecast) in-stream metadata ("StreamTitle") of internet radio streams.
///
/// Platform media APIs (Windows MediaPlayer) don't expose the ICY metadata of a stream. The reader opens a
/// separate connection with "Icy-MetaData: 1", skips the first audio block (icy-metaint bytes), parses the
/// following metadata block and closes the connection again. <see cref="IcyMetadataPoller"/> repeats that
/// periodically, so the additional traffic is only one audio block per poll.
public static class IcyMetadataReader
{
    /// Result of a single read.
    public enum ReadStatus
    {
        /// Metadata block read (may be empty: the station didn't change the title since the last block).
        Success,
        /// The server doesn't send ICY metadata (no icy-metaint header).
        NotSupported,
    }

    private const int MaxMetaInt = 1024 * 1024;

    /// Parses an ICY metadata block like <c>StreamTitle='Artist - Title';StreamUrl='';</c>.
    /// Values may contain quotes and semicolons; a value ends at the first <c>';</c> (or the final <c>'</c>).
    public static Dictionary<string, string> ParseMetadataBlock(string block)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = block.TrimEnd('\0', ' ', '\r', '\n');
        var pos = 0;
        while (pos < text.Length)
        {
            var eq = text.IndexOf('=', pos);
            if (eq < 0) break;
            var key = text[pos..eq].Trim().TrimStart(';').Trim();
            var valueStart = eq + 1;
            string value;
            if (valueStart < text.Length && text[valueStart] == '\'')
            {
                valueStart++;
                var end = text.IndexOf("';", valueStart, StringComparison.Ordinal);
                if (end < 0)
                {
                    // last value: ends with a single quote (or is unterminated)
                    end = text.EndsWith('\'') && text.Length - 1 >= valueStart ? text.Length - 1 : text.Length;
                    value = text[valueStart..end];
                    pos = text.Length;
                }
                else
                {
                    value = text[valueStart..end];
                    pos = end + 2;
                }
            }
            else
            {
                var end = text.IndexOf(';', valueStart);
                if (end < 0) end = text.Length;
                value = text[valueStart..end];
                pos = end + 1;
            }
            if (key.Length > 0) result[key] = value;
        }
        return result;
    }

    /// Decodes a metadata block: UTF-8 if valid, otherwise Latin-1 (older SHOUTcast servers).
    public static string DecodeMetadata(ReadOnlySpan<byte> data)
    {
        var length = data.IndexOf((byte)0);
        if (length >= 0) data = data[..length];
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(data);
        }
    }

    /// Connects to the stream, reads the first metadata block and disconnects.
    /// Returns <see cref="ReadStatus.NotSupported"/> if the server doesn't provide ICY metadata.
    public static async Task<(ReadStatus Status, Dictionary<string, string> Metadata)> ReadOnceAsync(HttpClient client, Uri url,
        IReadOnlyDictionary<string, string>? headers = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (headers is not null)
        {
            foreach (var (k, v) in headers) request.Headers.TryAddWithoutValidation(k, v);
        }
        request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var metaInt = GetMetaInt(response.Headers) ?? GetMetaInt(response.Content.Headers);
        if (metaInt is not { } interval || interval <= 0 || interval > MaxMetaInt) return (ReadStatus.NotSupported, []);

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await SkipAsync(stream, interval, ct).ConfigureAwait(false);
        var lengthByte = new byte[1];
        await stream.ReadExactlyAsync(lengthByte, ct).ConfigureAwait(false);
        var metaLength = lengthByte[0] * 16;
        if (metaLength == 0) return (ReadStatus.Success, []);
        var block = new byte[metaLength];
        await stream.ReadExactlyAsync(block, ct).ConfigureAwait(false);
        return (ReadStatus.Success, ParseMetadataBlock(DecodeMetadata(block)));
    }

    private static int? GetMetaInt(HttpHeaders headers)
    {
        if (!headers.TryGetValues("icy-metaint", out var values)) return null;
        foreach (var v in values)
        {
            if (int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        }
        return null;
    }

    private static async Task SkipAsync(Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[Math.Min(count, 16 * 1024)];
        var remaining = count;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), ct).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("ICY stream ended before the metadata block");
            remaining -= read;
        }
    }
}

/// Polls the ICY metadata of a radio stream periodically (see <see cref="IcyMetadataReader"/>) and reports
/// changed metadata. Stops by itself if the server doesn't support ICY metadata.
public sealed class IcyMetadataPoller : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    /// Interval between two reads.
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(15);

    public bool IsRunning { get; private set; }

    /// Starts polling. <paramref name="onMetadata"/> is invoked (on the thread pool) with each non empty
    /// metadata block that differs from the previous one.
    public void Start(HttpClient client, Uri url, IReadOnlyDictionary<string, string>? headers,
        Action<IReadOnlyDictionary<string, string>> onMetadata)
    {
        if (IsRunning) return;
        IsRunning = true;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            string? last = null;
            var failures = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var (status, metadata) = await IcyMetadataReader.ReadOnceAsync(client, url, headers, ct).ConfigureAwait(false);
                    if (status == IcyMetadataReader.ReadStatus.NotSupported)
                    {
                        AmperfyLog.Info("IcyMetadata", $"No ICY metadata provided by {url.Host}");
                        break;
                    }
                    failures = 0;
                    if (metadata.Count > 0)
                    {
                        var key = string.Join("|", metadata.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"));
                        if (key != last)
                        {
                            last = key;
                            if (!ct.IsCancellationRequested) onMetadata(metadata);
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // e.g. SHOUTcast v1 "ICY 200 OK" status lines are rejected by HttpClient
                    failures++;
                    AmperfyLog.Info("IcyMetadata", $"Reading ICY metadata failed ({failures}): {ex.Message}");
                    if (failures >= 3) break;
                }
                try
                {
                    await Task.Delay(Interval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            IsRunning = false;
        }, ct);
    }

    public void Dispose()
    {
        if (!_cts.IsCancellationRequested) _cts.Cancel();
        _cts.Dispose();
    }
}
