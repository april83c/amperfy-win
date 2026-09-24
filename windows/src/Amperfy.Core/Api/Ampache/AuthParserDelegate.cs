namespace Amperfy.Core.Api.Ampache;

/// Parses the response of the "handshake" action.
public sealed class AuthParserDelegate : AmpacheXmlParser
{
    private static readonly TimeSpan SafetyOffsetTimeBeforeSessionExpire = TimeSpan.FromMinutes(-5);

    public AuthentificationHandshake? AuthHandshake { get; private set; }
    public string? ServerApiVersion { get; private set; }
    private readonly AuthentificationHandshake _authBuffer = new();

    protected override void DidEndElement(string elementName)
    {
        switch (elementName)
        {
            case "auth":
                _authBuffer.Token = Buffer;
                break;
            case "api":
                ServerApiVersion = Buffer;
                break;
            case "session_expire":
                _authBuffer.SessionExpire = Buffer.AsIso8601Date() ?? DateTime.UtcNow;
                _authBuffer.ReauthenticateTime = _authBuffer.SessionExpire + SafetyOffsetTimeBeforeSessionExpire;
                break;
            case "update":
                _authBuffer.LibraryChangeDates = _authBuffer.LibraryChangeDates with { DateOfLastUpdate = Buffer.AsIso8601Date() ?? DateTime.UtcNow };
                break;
            case "add":
                _authBuffer.LibraryChangeDates = _authBuffer.LibraryChangeDates with { DateOfLastAdd = Buffer.AsIso8601Date() ?? DateTime.UtcNow };
                break;
            case "clean":
                _authBuffer.LibraryChangeDates = _authBuffer.LibraryChangeDates with { DateOfLastClean = Buffer.AsIso8601Date() ?? DateTime.UtcNow };
                break;
            case "songs":
                _authBuffer.SongCount = BufferIntOrZero;
                break;
            case "artists":
                _authBuffer.ArtistCount = BufferIntOrZero;
                break;
            case "albums":
                _authBuffer.AlbumCount = BufferIntOrZero;
                break;
            case "genres":
                _authBuffer.GenreCount = BufferIntOrZero;
                break;
            case "playlists":
                _authBuffer.PlaylistCount = BufferIntOrZero;
                break;
            case "podcasts":
                _authBuffer.PodcastCount = BufferIntOrZero;
                break;
            case "videos":
                _authBuffer.VideoCount = BufferIntOrZero;
                break;
            case "root":
                AuthHandshake = _authBuffer.Token.Length > 0 ? _authBuffer with { } : null;
                break;
        }
        base.DidEndElement(elementName);
    }
}
