using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsGenreParserTest : AbstractSsParserTest
{
    public SsGenreParserTest()
    {
        XmlData = GetTestFileData("genres_example_1");
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, SsIdParserDelegate.PrefetchIDs);
        SsParserDelegate = new SsGenreParserDelegate(prefetch, Account, Library, parseNotifier: null);
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(genreNameCount: 7);
        Assert.Equal(7, Library.GetGenreCount(Account));

        foreach (var name in new[] { "Electronic", "Hard Rock", "R&B", "Blues", "Podcast", "Brit Pop", "Live" })
        {
            var genre = Library.GetGenreByName(Account, name);
            Assert.NotNull(genre);
            Assert.Equal(name, genre.Name);
            AssertIsAccount1(genre.Account);
        }
    }
}
