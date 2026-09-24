namespace Amperfy.Core.Api.Subsonic;

public class SsGenreParserDelegate : SsXmlLibParser
{
    public SsGenreParserDelegate(PrefetchElementContainer prefetch, Account account, LibraryStorage library, IParsedObjectNotifiable? parseNotifier = null)
        : base(prefetch, account, library, parseNotifier)
    {
    }

    protected override void DidEndElement(string elementName)
    {
        if (elementName == "genre")
        {
            var genreName = Buffer;
            if (!Prefetch.PrefetchedGenreDict.ContainsKey(genreName))
            {
                var genre = Library.CreateGenre(Account);
                Prefetch.PrefetchedGenreDict[genreName] = genre;
                genre.Name = genreName;
            }
            // else: info already synced -> skip
            ParsedCount += 1;
        }
        base.DidEndElement(elementName);
    }
}
