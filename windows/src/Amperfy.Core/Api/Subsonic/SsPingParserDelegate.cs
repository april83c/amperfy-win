namespace Amperfy.Core.Api.Subsonic;

/// Parses a "ping" response (authentication check + server API version). Also used to check
/// responses without payload for errors.
public class SsPingParserDelegate : SsXmlParser
{
    public bool IsAuthValid { get; private set; }
    public string? ServerApiVersion { get; private set; }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        if (elementName == "subsonic-response" && StrAttr(attributes, "version") is { } version)
        {
            ServerApiVersion = version;
        }
    }

    protected override void DidEndElement(string elementName)
    {
        if (elementName == "subsonic-response" && Error is null) IsAuthValid = true;
        base.DidEndElement(elementName);
    }
}
