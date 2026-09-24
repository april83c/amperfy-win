namespace Amperfy.Core.Api.Ampache;

public sealed class RadioParserDelegate : AmpacheXmlLibParser
{
    private Radio? _radioBuffer;
    public List<Radio> ParsedRadios { get; } = [];

    public RadioParserDelegate(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(prefetch, account, library, parseNotifier)
    {
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        if (elementName != "live_stream") return;
        if (!attributes.TryGetValue("id", out var radioId))
        {
            AmperfyLog.Error("Ampache", "Found radio with no id");
            return;
        }
        if (Prefetch.PrefetchedRadioDict.TryGetValue(radioId, out var prefetchedRadio))
        {
            _radioBuffer = prefetchedRadio;
            _radioBuffer.RemoteStatus = RemoteStatus.Available;
        }
        else
        {
            _radioBuffer = Library.CreateRadio(Account);
            _radioBuffer.Id = radioId;
            Prefetch.PrefetchedRadioDict[radioId] = _radioBuffer;
        }
    }

    protected override void DidEndElement(string elementName)
    {
        switch (elementName)
        {
            case "name":
                if (_radioBuffer is not null) _radioBuffer.Title = Buffer;
                break;
            case "url":
                if (_radioBuffer is not null) _radioBuffer.Url = Buffer;
                break;
            case "site_url":
                // Swift: URL(string: buffer) -> nil for an empty/invalid string
                if (_radioBuffer is not null)
                    _radioBuffer.SiteUrl = Uri.TryCreate(Buffer, UriKind.RelativeOrAbsolute, out _) && Buffer.Length > 0 ? Buffer : null;
                break;
            case "live_stream":
                ParsedCount += 1;
                if (_radioBuffer is { } radio) ParsedRadios.Add(radio);
                _radioBuffer = null;
                break;
        }
        base.DidEndElement(elementName);
    }
}
