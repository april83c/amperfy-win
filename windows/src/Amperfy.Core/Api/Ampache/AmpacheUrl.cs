namespace Amperfy.Core.Api.Ampache;

/// Minimal URLComponents replacement: base url (scheme, host, port, path) plus ordered query items.
public sealed class AmpacheUrlComponents
{
    public string Scheme { get; set; }
    public string Host { get; set; }
    public int? Port { get; set; }
    public string UserInfo { get; set; }
    /// Unescaped path (e.g. "/ampache/server/xml.server.php")
    public string Path { get; set; }
    public List<KeyValuePair<string, string?>>? QueryItems { get; set; }
    public string Fragment { get; set; }

    private AmpacheUrlComponents(string scheme, string host, int? port, string userInfo, string path, List<KeyValuePair<string, string?>>? queryItems, string fragment)
    {
        Scheme = scheme;
        Host = host;
        Port = port;
        UserInfo = userInfo;
        Path = path;
        QueryItems = queryItems;
        Fragment = fragment;
    }

    /// Parses an absolute URL string. Returns null if it is not a valid absolute URL.
    public static AmpacheUrlComponents? Create(string? urlString)
    {
        if (string.IsNullOrWhiteSpace(urlString)) return null;
        if (!Uri.TryCreate(urlString.Trim(), UriKind.Absolute, out var uri)) return null;
        return Create(uri);
    }

    public static AmpacheUrlComponents? Create(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri) return null;
        var port = uri.IsDefaultPort ? (int?)null : uri.Port;
        var fragment = uri.Fragment.StartsWith('#') ? uri.Fragment[1..] : uri.Fragment;
        return new AmpacheUrlComponents(uri.Scheme, uri.Host, port, uri.UserInfo, Uri.UnescapeDataString(uri.AbsolutePath),
            ParseQueryItems(uri.Query), fragment);
    }

    /// Parses "?a=1&amp;b=2" into (name, value) pairs; null if there is no query.
    public static List<KeyValuePair<string, string?>>? ParseQueryItems(string? query)
    {
        if (string.IsNullOrEmpty(query)) return null;
        var q = query.StartsWith('?') ? query[1..] : query;
        var items = new List<KeyValuePair<string, string?>>();
        if (q.Length == 0) return items;
        foreach (var part in q.Split('&'))
        {
            var idx = part.IndexOf('=');
            if (idx < 0) items.Add(new(Uri.UnescapeDataString(part), null));
            else items.Add(new(Uri.UnescapeDataString(part[..idx]), Uri.UnescapeDataString(part[(idx + 1)..])));
        }
        return items;
    }

    /// Like URL.appendPathComponent: adds "/component" (no double slashes).
    public void AppendPathComponent(string component)
    {
        Path = Path.TrimEnd('/') + "/" + component;
    }

    public void AddQueryItem(string name, string value)
    {
        QueryItems ??= [];
        QueryItems.Add(new(name, value));
    }

    public void AddQueryItem(string name, int value) => AddQueryItem(name, value.ToString(CultureInfo.InvariantCulture));

    public void AddQueryItem(string name, long value) => AddQueryItem(name, value.ToString(CultureInfo.InvariantCulture));

    public string? GetQueryValue(string name) => QueryItems?.FirstOrDefault(i => i.Key == name).Value;

    public static string EscapeQueryValue(string value) => Uri.EscapeDataString(value);

    public string Query => QueryItems is null
        ? ""
        : string.Join("&", QueryItems.Select(i => i.Value is null ? EscapeQueryValue(i.Key) : $"{EscapeQueryValue(i.Key)}={EscapeQueryValue(i.Value)}"));

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(Scheme).Append("://");
        if (!string.IsNullOrEmpty(UserInfo)) sb.Append(UserInfo).Append('@');
        sb.Append(Host.Contains(':') && !Host.StartsWith('[') ? $"[{Host}]" : Host);
        if (Port is { } port) sb.Append(':').Append(port.ToString(CultureInfo.InvariantCulture));
        var path = string.Join("/", Path.Split('/').Select(Uri.EscapeDataString));
        if (path.Length > 0 && !path.StartsWith('/')) sb.Append('/');
        sb.Append(path);
        if (QueryItems is not null) sb.Append('?').Append(Query);
        if (!string.IsNullOrEmpty(Fragment)) sb.Append('#').Append(Fragment);
        return sb.ToString();
    }

    /// Builds the url (throws BackendError.InvalidUrl if it is not valid).
    public Uri ToUri()
    {
        if (Uri.TryCreate(ToString(), UriKind.Absolute, out var uri)) return uri;
        throw BackendError.InvalidUrl;
    }
}
