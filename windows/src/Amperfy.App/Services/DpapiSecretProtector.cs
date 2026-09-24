using System.Security.Cryptography;
using System.Text;
using Amperfy.Core.Storage;

namespace Amperfy.App.Services;

/// Protects stored passwords with the Windows Data Protection API (per user).
public sealed class DpapiSecretProtector : ISecretProtector
{
    private const string Prefix = "dpapi:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Amperfy.Windows.Credentials");

    public string Protect(string plain)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(bytes);
    }

    public string Unprotect(string protectedValue)
    {
        if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal)) return protectedValue;
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedValue[Prefix.Length..]), Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
