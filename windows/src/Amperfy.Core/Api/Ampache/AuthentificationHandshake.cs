namespace Amperfy.Core.Api.Ampache;

/// Dates of the last library changes reported by the Ampache handshake.
public readonly record struct LibraryChangeDates(DateTime DateOfLastUpdate, DateTime DateOfLastAdd, DateTime DateOfLastClean)
{
    public LibraryChangeDates() : this(DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow) { }

    /// Like Swift: only the date of the last add is compared ("orderedSame" counts as less).
    public static bool operator <(LibraryChangeDates lhs, LibraryChangeDates rhs) => lhs.DateOfLastAdd <= rhs.DateOfLastAdd;
    public static bool operator >(LibraryChangeDates lhs, LibraryChangeDates rhs) => rhs < lhs;
}

/// Result of the Ampache "handshake" action (auth token + library meta data).
public sealed record AuthentificationHandshake
{
    public string Token { get; set; } = "";
    public DateTime SessionExpire { get; set; } = DateTime.UtcNow;
    /// Point in time after which a new handshake is required (session expire minus a safety offset).
    public DateTime ReauthenticateTime { get; set; } = DateTime.UtcNow;
    public LibraryChangeDates LibraryChangeDates { get; set; } = new();
    public int SongCount { get; set; }
    public int ArtistCount { get; set; }
    public int AlbumCount { get; set; }
    public int GenreCount { get; set; }
    public int PlaylistCount { get; set; }
    public int PodcastCount { get; set; }
    public int VideoCount { get; set; }
}
