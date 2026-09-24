using System.Globalization;
using Amperfy.Core.Api.Ampache;
using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Api.Ampache;

/// Port of Samples/GenreParserTest.swift
public class GenreParserTest : AbstractAmpacheTest
{
    public GenreParserTest()
    {
        XmlData = GetTestFileData("genres");
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, IdParserDelegate.PrefetchIDs);
        ParserDelegate = new GenreParserDelegate(prefetch, Account, Library, parseNotifier: null);
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(genreIdCount: 2);
        Assert.Equal(2, Library.GetGenreCount(Account));

        var genre = Library.GetGenre(Account, "6");
        Assert.NotNull(genre);
        AssertTestAccount1(genre!.Account);
        Assert.Equal("6", genre.Id);
        Assert.Equal("Dance", genre.Name);

        genre = Library.GetGenre(Account, "4");
        Assert.NotNull(genre);
        AssertTestAccount1(genre!.Account);
        Assert.Equal("4", genre.Id);
        Assert.Equal("Dark Ambient", genre.Name);
    }
}
