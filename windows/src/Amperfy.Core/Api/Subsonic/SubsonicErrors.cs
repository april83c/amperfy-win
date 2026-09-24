namespace Amperfy.Core.Api.Subsonic;

/// Subsonic API error codes (SubsonicServerApi.SubsonicError in Swift).
public enum SubsonicError
{
    /// A generic error.
    Generic = 0,
    /// Required parameter is missing.
    RequiredParameterMissing = 10,
    /// Incompatible Subsonic REST protocol version. Client must upgrade.
    ClientVersionTooLow = 20,
    /// Incompatible Subsonic REST protocol version. Server must upgrade.
    ServerVersionTooLow = 30,
    /// Wrong username or password.
    WrongUsernameOrPassword = 40,
    /// Token authentication not supported for LDAP users.
    TokenAuthenticationNotSupported = 41,
    /// User is not authorized for the given operation.
    UserIsNotAuthorized = 50,
    /// The trial period for the Subsonic server is over.
    TrialPeriodForServerIsOver = 60,
    /// The requested data was not found.
    RequestedDataNotFound = 70,
}

public static class SubsonicErrorExtensions
{
    public static bool ShouldErrorBeDisplayedToUser(this SubsonicError e) => e != SubsonicError.RequestedDataNotFound;

    public static bool IsRemoteAvailable(this SubsonicError e) => e != SubsonicError.RequestedDataNotFound;

    /// Maps a raw status code to a known Subsonic error (null if the code is unknown).
    public static SubsonicError? FromStatusCode(int statusCode) =>
        Enum.IsDefined(typeof(SubsonicError), statusCode) ? (SubsonicError)statusCode : null;

    /// Swift ResponseError.asSubsonicError
    public static SubsonicError? AsSubsonicError(this ResponseError error) => FromStatusCode(error.StatusCode);

    /// Swift ResponseError.createFromSubsonicError
    public static ResponseError CreateResponseError(this SubsonicResponseError error, CleansedUrl? cleansedUrl, byte[]? data) =>
        new(ResponseErrorType.Api, error.StatusCode, error.Message, cleansedUrl, data);
}

/// Error element of a Subsonic response (&lt;error code="40" message="..."/&gt;).
public sealed class SubsonicResponseError
{
    public int StatusCode { get; }
    public string Message { get; }

    public SubsonicResponseError(int statusCode, string message)
    {
        StatusCode = statusCode;
        Message = message;
    }

    public SubsonicError? SubsonicError => SubsonicErrorExtensions.FromStatusCode(StatusCode);

    public override string ToString() => $"Subsonic error {StatusCode}: {Message}";
}

public enum SubsonicApiAuthType
{
    AutoDetect = 0,
    Legacy = 1,
}

public enum OpenSubsonicExtension
{
    SongLyrics,
}

public static class OpenSubsonicExtensionExtensions
{
    /// Name of the extension as reported by the server.
    public static string RawValue(this OpenSubsonicExtension e) => e switch
    {
        OpenSubsonicExtension.SongLyrics => "songLyrics",
        _ => e.ToString(),
    };
}
