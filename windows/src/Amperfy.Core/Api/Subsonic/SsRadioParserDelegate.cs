namespace Amperfy.Core.Api.Subsonic;

public class SsRadioParserDelegate : SsXmlLibParser
{
    private Radio? _radioBuffer;
    public List<Radio> ParsedRadios { get; } = [];

    public SsRadioParserDelegate(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(prefetch, account, library, parseNotifier)
    {
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        if (elementName == "internetRadioStation")
        {
            if (StrAttr(attributes, "id") is not { } radioId)
            {
                AmperfyLog.Error("Parser", "Found radio with no id");
                return;
            }
            if (Prefetch.PrefetchedRadioDict.TryGetValue(radioId, out var prefetchedRadio))
            {
                _radioBuffer = prefetchedRadio;
                prefetchedRadio.RemoteStatus = RemoteStatus.Available;
            }
            else
            {
                _radioBuffer = Library.CreateRadio(Account);
                Prefetch.PrefetchedRadioDict[radioId] = _radioBuffer;
                _radioBuffer.Id = radioId;
            }

            if (StrAttr(attributes, "name") is { } attributeTitle) _radioBuffer.Title = attributeTitle;
            if (StrAttr(attributes, "streamUrl") is { } streamUrl) _radioBuffer.Url = streamUrl;
            if (StrAttr(attributes, "homePageUrl") is { } siteUrl)
            {
                // Swift: URL(string:) -> nil for strings which are no valid URL
                _radioBuffer.SiteUrl = Uri.TryCreate(siteUrl, UriKind.RelativeOrAbsolute, out _) ? siteUrl : null;
            }
        }
        base.DidStartElement(elementName, attributes);
    }

    protected override void DidEndElement(string elementName)
    {
        base.DidEndElement(elementName);
        if (elementName == "internetRadioStation" && _radioBuffer is { } radio)
        {
            ParsedCount += 1;
            ParsedRadios.Add(radio);
            _radioBuffer = null;
        }
    }
}
