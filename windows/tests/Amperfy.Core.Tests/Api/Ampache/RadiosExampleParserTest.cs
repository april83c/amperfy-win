using System.Globalization;
using Amperfy.Core.Api.Ampache;
using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Api.Ampache;

/// Port of RadiosExampleParserTest.swift
public class RadiosExampleParserTest : AbstractAmpacheTest
{
    public RadiosExampleParserTest()
    {
        XmlData = GetTestFileData("live_streams");
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, IdParserDelegate.PrefetchIDs);
        ParserDelegate = new RadioParserDelegate(prefetch, Account, Library);
    }

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(radioCount: 3);
        var radios = Library.GetRadios(Account).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        Assert.Equal(3, radios.Count);

        (string Id, string Title, string Url, string SiteUrl)[] expected =
        [
            ("1", "HBR1.com - Dream Factory", "http://ubuntu.hbr1.com:19800/ambient.aac", "http://www.hbr1.com/"),
            ("2", "HBR1.com - I.D.M. Tranceponder", "http://ubuntu.hbr1.com:19800/trance.ogg", "http://www.hbr1.com/"),
            ("3", "4ZZZ Community Radio", "https://stream.4zzz.org.au:9200/4zzz", "https://4zzzfm.org.au"),
        ];
        for (var i = 0; i < expected.Length; i++)
        {
            var radio = radios[i];
            AssertTestAccount1(radio.Account);
            Assert.Equal(expected[i].Id, radio.Id);
            Assert.Equal(expected[i].Title, radio.Title);
            Assert.Equal(0, radio.Rating);
            Assert.Equal(expected[i].Url, radio.Url);
            Assert.Equal(expected[i].SiteUrl, radio.SiteUrl);
            Assert.Null(radio.Disk);
            Assert.Equal(0, radio.Duration);
            Assert.Equal(RemoteStatus.Available, radio.RemoteStatus);
            Assert.Equal(0, radio.RemoteDuration);
            Assert.Equal(0, radio.Year);
            Assert.Equal(0, radio.Bitrate);
            Assert.False(radio.IsFavorite);
            Assert.Null(radio.StarredDate);
            Assert.Null(radio.ContentType);
            Assert.Equal(0, radio.Size);
            Assert.Null(radio.Artwork);
        }
    }
}
