namespace Amperfy.Core.Api.Subsonic;

/// Subsonic REST API version "major.minor.patch" (port of SubsonicVersion.swift).
/// Note: the Swift '>' compared every component independently (1.1.1 > 1.9.0 was true); here the
/// comparison is lexicographic (major, minor, patch). All Swift test cases behave identically.
public sealed class SubsonicVersion : IEquatable<SubsonicVersion>
{
    public static readonly SubsonicVersion AuthenticationTokenRequiredServerApi = new(1, 13, 0);

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    public string Description => $"{Major}.{Minor}.{Patch}";

    public SubsonicVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    /// Parses "major.minor.patch" (non negative integers). Returns null if the string is invalid.
    public static SubsonicVersion? Create(string versionString)
    {
        var parts = versionString.Split('.');
        if (parts.Length != 3) return null;
        if (!TryParseComponent(parts[0], out var major) ||
            !TryParseComponent(parts[1], out var minor) ||
            !TryParseComponent(parts[2], out var patch)) return null;
        if (major < 0 || minor < 0 || patch < 0) return null;
        return new SubsonicVersion(major, minor, patch);
    }

    private static bool TryParseComponent(string s, out int value) =>
        int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    public override string ToString() => Description;

    public bool Equals(SubsonicVersion? other) =>
        other is not null && Major == other.Major && Minor == other.Minor && Patch == other.Patch;

    public override bool Equals(object? obj) => Equals(obj as SubsonicVersion);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch);

    public static bool operator ==(SubsonicVersion? lhs, SubsonicVersion? rhs) => lhs is null ? rhs is null : lhs.Equals(rhs);

    public static bool operator !=(SubsonicVersion? lhs, SubsonicVersion? rhs) => !(lhs == rhs);

    public static bool operator >(SubsonicVersion lhs, SubsonicVersion rhs)
    {
        if (lhs.Major != rhs.Major) return lhs.Major > rhs.Major;
        if (lhs.Minor != rhs.Minor) return lhs.Minor > rhs.Minor;
        return lhs.Patch > rhs.Patch;
    }

    public static bool operator >=(SubsonicVersion lhs, SubsonicVersion rhs)
    {
        if (lhs == rhs) return true;
        return lhs > rhs;
    }

    public static bool operator <(SubsonicVersion lhs, SubsonicVersion rhs)
    {
        if (lhs == rhs) return false;
        return !(lhs > rhs);
    }

    public static bool operator <=(SubsonicVersion lhs, SubsonicVersion rhs)
    {
        if (lhs == rhs) return true;
        return !(lhs > rhs);
    }
}
