namespace Amperfy.Core.Api.Ampache;

public sealed class GenreParserDelegate : AmpacheXmlLibParser
{
    private Genre? _genreBuffer;

    public GenreParserDelegate(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(prefetch, account, library, parseNotifier)
    {
    }

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);
        if (elementName != "genre") return;
        if (!attributes.TryGetValue("id", out var genreId))
        {
            AmperfyLog.Error("Ampache", "Found genre with no id");
            return;
        }
        if (Prefetch.PrefetchedGenreDict.TryGetValue(genreId, out var prefetchedGenre))
        {
            _genreBuffer = prefetchedGenre;
        }
        else
        {
            _genreBuffer = Library.CreateGenre(Account);
            _genreBuffer.Id = genreId;
            Prefetch.PrefetchedGenreDict[genreId] = _genreBuffer;
        }
    }

    protected override void DidEndElement(string elementName)
    {
        switch (elementName)
        {
            case "name":
                if (_genreBuffer is not null) _genreBuffer.Name = Buffer;
                break;
            case "genre":
                ParsedCount += 1;
                ParseNotifier?.NotifyParsedObject(ParsedObjectType.Genre);
                _genreBuffer = null;
                break;
        }
        base.DidEndElement(elementName);
    }
}
