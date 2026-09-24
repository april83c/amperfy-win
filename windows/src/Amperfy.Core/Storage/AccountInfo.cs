using System.Security.Cryptography;

namespace Amperfy.Core.Storage;

/// Identifies an account by hashes of the server url and the user name.
/// The api type is intentionally not part of the identity.
public sealed class AccountInfo : IEquatable<AccountInfo>
{
    public string ServerHash { get; }
    public string UserHash { get; }
    public BackendApiType ApiType { get; }

    public AccountInfo(string serverHash, string userHash, BackendApiType apiType = BackendApiType.NotDetected)
    {
        ServerHash = serverHash;
        UserHash = userHash;
        ApiType = apiType;
    }

    public string Ident => $"{ServerHash}-{UserHash}";

    public static AccountInfo? Create(string ident)
    {
        var idx = ident.IndexOf('-');
        if (idx < 0) return null;
        return new AccountInfo(ident[..idx], ident[(idx + 1)..]);
    }

    public static AccountInfo Create(string serverUrl, string userName, BackendApiType apiType) =>
        new(Hash16(serverUrl), Hash16(userName), apiType);

    public static AccountInfo Create(LoginCredentials credentials) =>
        Create(credentials.ServerUrl, credentials.Username, credentials.BackendApi);

    /// SHA256, first 8 bytes as hex (matches the iOS implementation).
    private static string Hash16(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    public bool Equals(AccountInfo? other) => other is not null && ServerHash == other.ServerHash && UserHash == other.UserHash;
    public override bool Equals(object? obj) => Equals(obj as AccountInfo);
    public override int GetHashCode() => HashCode.Combine(ServerHash, UserHash);
    public static bool operator ==(AccountInfo? a, AccountInfo? b) => a is null ? b is null : a.Equals(b);
    public static bool operator !=(AccountInfo? a, AccountInfo? b) => !(a == b);
    public override string ToString() => Ident;
}
