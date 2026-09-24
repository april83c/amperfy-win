using System.Text.Json.Serialization;

namespace Amperfy.Core.Storage;

/// Protects secrets (passwords) at rest. The Windows app plugs in DPAPI.
public interface ISecretProtector
{
    string Protect(string plain);
    string Unprotect(string protectedValue);
}

public sealed class NoSecretProtector : ISecretProtector
{
    public string Protect(string plain) => plain;
    public string Unprotect(string protectedValue) => protectedValue;
}

public static class SecretProtection
{
    public static ISecretProtector Protector { get; set; } = new NoSecretProtector();
}

public sealed class LoginCredentials
{
    public string ServerUrl { get; set; } = "";
    public string Username { get; set; } = "";

    [JsonIgnore]
    public string Password { get; set; } = "";

    /// Serialized, protected form of the password.
    [JsonPropertyName("password")]
    public string ProtectedPassword
    {
        get => string.IsNullOrEmpty(Password) ? "" : SecretProtection.Protector.Protect(Password);
        set
        {
            try { Password = string.IsNullOrEmpty(value) ? "" : SecretProtection.Protector.Unprotect(value); }
            catch { Password = ""; }
        }
    }

    public BackendApiType BackendApi { get; set; } = BackendApiType.NotDetected;
    public string ActiveBackendServerUrl { get; set; } = "";
    public List<string> AlternativeServerURLs { get; set; } = [];

    /// Custom HTTP headers sent with every request to the server (e.g. Cloudflare Access service token).
    public Dictionary<string, string> HttpHeaders { get; set; } = [];

    public LoginCredentials() { }

    public LoginCredentials(string serverUrl, string username, string password, BackendApiType backendApi = BackendApiType.NotDetected)
    {
        ServerUrl = serverUrl;
        Username = username;
        Password = password;
        BackendApi = backendApi;
        ActiveBackendServerUrl = serverUrl;
    }

    [JsonIgnore]
    public string PasswordHash => StringHasher.Sha256(Password);

    [JsonIgnore]
    public string DisplayServerUrl
    {
        get
        {
            if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri)) return "";
            return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        }
    }

    [JsonIgnore]
    public List<string> AvailableServerURLs => [ServerUrl, .. AlternativeServerURLs];

    public LoginCredentials Clone() => new()
    {
        ServerUrl = ServerUrl,
        Username = Username,
        Password = Password,
        BackendApi = BackendApi,
        ActiveBackendServerUrl = string.IsNullOrEmpty(ActiveBackendServerUrl) ? ServerUrl : ActiveBackendServerUrl,
        AlternativeServerURLs = [.. AlternativeServerURLs],
        HttpHeaders = new Dictionary<string, string>(HttpHeaders),
    };
}
