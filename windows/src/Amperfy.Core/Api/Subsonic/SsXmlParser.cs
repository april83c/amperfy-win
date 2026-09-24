namespace Amperfy.Core.Api.Subsonic;

/// Base of all Subsonic parsers: detects the &lt;error&gt; element of a response.
public class SsXmlParser : GenericXmlParser
{
    public SubsonicResponseError? Error { get; private set; }

    /// Number of parsed elements (songs, albums, ...), public for the syncer/tests.
    public new int ParsedCount
    {
        get => base.ParsedCount;
        protected set => base.ParsedCount = value;
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        if (elementName == "error")
        {
            var statusCode = int.TryParse(StrAttr(attributes, "code") ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) ? code : 0;
            var message = StrAttr(attributes, "message") ?? "";
            Error = new SubsonicResponseError(statusCode, message);
        }
    }

    // --- Swift-like attribute conversions -------------------------------------------------------

    /// Swift Int(String): optional sign and digits only.
    protected static int? SwiftInt(string? s) =>
        s is not null && int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i) ? i : null;

    protected static long? SwiftLong(string? s) =>
        s is not null && long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i) ? i : null;

    /// Swift Float(String)
    protected static float? SwiftFloat(string? s) =>
        s is not null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;

    /// Swift Bool(String): only "true" / "false" are accepted.
    protected static bool? SwiftBool(string? s) => s switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };
}

/// Parser which can report progress (Swift SsNotifiableXmlParser).
public class SsNotifiableXmlParser : SsXmlParser
{
    public IParsedObjectNotifiable? ParseNotifier { get; set; }

    public SsNotifiableXmlParser(IParsedObjectNotifiable? parseNotifier = null)
    {
        ParseNotifier = parseNotifier;
    }
}

/// Parser that writes into the library (Swift SsXmlLibParser).
public class SsXmlLibParser : SsNotifiableXmlParser
{
    public PrefetchElementContainer Prefetch { get; set; }
    public Account Account { get; }
    public LibraryStorage Library { get; }

    public SsXmlLibParser(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(parseNotifier)
    {
        Prefetch = prefetch;
        Account = account;
        Library = library;
    }
}

/// Parser with artwork support (Swift SsXmlLibWithArtworkParser).
public class SsXmlLibWithArtworkParser : SsXmlLibParser
{
    public SsXmlLibWithArtworkParser(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(prefetch, account, library, parseNotifier)
    {
    }

    protected Artwork ParseArtwork(string id)
    {
        var remoteInfo = new ArtworkRemoteInfo(id, "");
        if (Prefetch.PrefetchedArtworkDict.TryGetValue(remoteInfo, out var prefetchedArtwork)) return prefetchedArtwork;
        var createdArtwork = Library.CreateArtwork(Account);
        Prefetch.PrefetchedArtworkDict[remoteInfo] = createdArtwork;
        createdArtwork.RemoteInfo = remoteInfo;
        createdArtwork.Status = ImageStatus.NotChecked;
        return createdArtwork;
    }
}

/// Date helpers matching the Swift formatters used by the Subsonic parsers.
public static class SubsonicDateParser
{
    private static readonly System.Text.RegularExpressions.Regex InternetDateTimeWithFractionalSeconds = new(
        @"^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})\.(\d+)(Z|[+-]\d{2}:?\d{2})$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// Port of ISO8601DateFormatter with [.withInternetDateTime, .withFractionalSeconds]:
    /// fractional seconds and a time zone are required, otherwise null is returned.
    public static DateTime? ParseIso8601WithFractionalSeconds(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var m = InternetDateTimeWithFractionalSeconds.Match(s);
        if (!m.Success) return null;
        try
        {
            int P(int g) => int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
            var fraction = m.Groups[7].Value;
            var ticksFraction = fraction.Length >= 7 ? fraction[..7] : fraction.PadRight(7, '0');
            var date = new DateTime(P(1), P(2), P(3), P(4), P(5), P(6), DateTimeKind.Utc)
                .AddTicks(long.Parse(ticksFraction, CultureInfo.InvariantCulture));
            var zone = m.Groups[8].Value;
            if (zone != "Z")
            {
                var sign = zone[0] == '-' ? -1 : 1;
                var digits = zone[1..].Replace(":", "");
                var offset = new TimeSpan(int.Parse(digits[..2], CultureInfo.InvariantCulture), int.Parse(digits[2..], CultureInfo.InvariantCulture), 0);
                date -= sign * offset;
            }
            return date;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// Podcast episode publish date: "yyyy-MM-dd'T'HH:mm:ss" (UTC); only the first 19 characters are used.
    /// Returns null if the string is shorter than 19 characters, the Unix epoch if it can't be parsed.
    public static DateTime? ParsePublishDate(string? publishDate)
    {
        if (publishDate is null || publishDate.Length < 19) return null;
        var withoutTimeZone = publishDate[..19];
        return DateTime.TryParseExact(withoutTimeZone, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
            : DateTime.UnixEpoch;
    }
}
