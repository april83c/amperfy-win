namespace Amperfy.Core.Api.Subsonic;

public sealed class OpenSubsonicExtensionsResponse
{
    public string Status { get; set; } = "";
    public string Version { get; set; } = "";
    public string Type { get; set; } = "";
    public string ServerVersion { get; set; } = "";
    public bool? OpenSubsonic { get; set; }
    public List<string> SupportedExtensions { get; } = [];
}

/// Parses the "getOpenSubsonicExtensions" response.
public class SsOpenSubsonicExtensionsParserDelegate : SsXmlParser
{
    public OpenSubsonicExtensionsResponse OpenSubsonicExtensionsResponse { get; } = new();

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);

        if (elementName == "subsonic-response")
        {
            OpenSubsonicExtensionsResponse.Status = StrAttr(attributes, "status") ?? "";
            OpenSubsonicExtensionsResponse.Version = StrAttr(attributes, "version") ?? "";
            OpenSubsonicExtensionsResponse.Type = StrAttr(attributes, "type") ?? "";
            OpenSubsonicExtensionsResponse.ServerVersion = StrAttr(attributes, "serverVersion") ?? "";
            OpenSubsonicExtensionsResponse.OpenSubsonic = StrAttr(attributes, "openSubsonic") is { } isOpenSubsonic && isOpenSubsonic == "true";
        }
        else if (elementName == "openSubsonicExtensions")
        {
            if (StrAttr(attributes, "name") is { } name) OpenSubsonicExtensionsResponse.SupportedExtensions.Add(name);
        }
    }
}
