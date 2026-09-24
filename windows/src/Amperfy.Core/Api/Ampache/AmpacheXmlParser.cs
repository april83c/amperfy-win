namespace Amperfy.Core.Api.Ampache;

/// Ampache API error codes.
public enum AmpacheError
{
    Empty = 0,
    /// The API is disabled. Enable 'access_control' in your config
    AccessControlNotEnabled = 4700,
    /// This is a temporary error, this means no valid session was passed or the handshake failed
    ReceivedInvalidHandshake = 4701,
    /// The requested method is not available. You can check the error message for details about which feature is disabled
    AccessDenied = 4703,
    /// The API could not find the requested object
    NotFound = 4704,
    /// This is a fatal error, the service requested a method that the API does not implement
    Missing = 4705,
    /// This is a fatal error, the method requested is no longer available
    Depreciated = 4706,
    /// Used when you have specified a valid method but something about the input is incorrect, invalid or missing.
    /// You can check the error message for details, but do not re-attempt the exact same request
    BadRequest = 4710,
    /// Access denied to the requested object or function for this user
    FailedAccessCheck = 4742,
}

public static class AmpacheErrorExtensions
{
    public static bool ShouldErrorBeDisplayedToUser(this AmpacheError e) => e != AmpacheError.Empty && e != AmpacheError.NotFound;

    public static bool IsRemoteAvailable(this AmpacheError e) => e != AmpacheError.NotFound;

    /// Maps a status code to a known Ampache error (null for unknown codes).
    public static AmpacheError? FromStatusCode(int statusCode) =>
        Enum.IsDefined(typeof(AmpacheError), statusCode) ? (AmpacheError)statusCode : null;

    public static AmpacheError? AsAmpacheError(this ResponseError error) => FromStatusCode(error.StatusCode);
}

/// Error document returned by the Ampache API (&lt;error errorCode="..."&gt;).
public sealed class AmpacheResponseError : Exception
{
    public int StatusCode { get; }
    public string ErrorMessage { get; }

    public AmpacheResponseError(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
        ErrorMessage = message;
    }

    public AmpacheError? AmpacheError => AmpacheErrorExtensions.FromStatusCode(StatusCode);

    public ResponseError ToResponseError(CleansedUrl? cleansedUrl, byte[]? data) =>
        new(ResponseErrorType.Api, StatusCode, ErrorMessage, cleansedUrl, data);
}

/// Base of all Ampache parsers: detects the Ampache error document.
public class AmpacheXmlParser : GenericXmlParser
{
    public AmpacheResponseError? Error { get; private set; }
    private int _statusCode;
    private string _message = "";

    /// Number of main elements parsed (songs, albums, ...).
    public int ParsedElementCount => ParsedCount;

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        if (elementName == "error")
        {
            _statusCode = attributes.TryGetValue("errorCode", out var code) && int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : 0;
        }
    }

    protected override void DidEndElement(string elementName)
    {
        switch (elementName)
        {
            case "errorMessage":
                _message = Buffer;
                break;
            case "error":
                Error = new AmpacheResponseError(_statusCode, _message);
                break;
        }
        base.DidEndElement(elementName);
    }

    /// Swift: Int(buffer) ?? 0
    protected int BufferIntOrZero => BufferAsInt ?? 0;
}

public class AmpacheNotifiableXmlParser : AmpacheXmlParser
{
    public IParsedObjectNotifiable? ParseNotifier { get; set; }

    public AmpacheNotifiableXmlParser(IParsedObjectNotifiable? parseNotifier = null)
    {
        ParseNotifier = parseNotifier;
    }
}

/// Parser that creates/updates library entities.
public class AmpacheXmlLibParser : AmpacheNotifiableXmlParser
{
    public PrefetchElementContainer Prefetch { get; }
    public Account Account { get; }
    public LibraryStorage Library { get; }

    public AmpacheXmlLibParser(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(parseNotifier)
    {
        Prefetch = prefetch;
        Account = account;
        Library = library;
    }

    public Artwork? ParseArtwork(string urlString)
    {
        if (AmpacheXmlServerApi.ExtractArtworkInfoFromUrl(urlString) is not { } artworkRemoteInfo) return null;
        if (Prefetch.PrefetchedArtworkDict.TryGetValue(artworkRemoteInfo, out var prefetchedArtwork)) return prefetchedArtwork;
        var createdArtwork = Library.CreateArtwork(Account);
        Prefetch.PrefetchedArtworkDict[artworkRemoteInfo] = createdArtwork;
        createdArtwork.RemoteInfo = artworkRemoteInfo;
        createdArtwork.Status = ImageStatus.NotChecked;
        return createdArtwork;
    }
}
