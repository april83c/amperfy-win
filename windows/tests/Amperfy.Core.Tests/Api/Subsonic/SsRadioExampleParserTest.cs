using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsRadioExampleParserTest : AbstractSsParserTest
{
    public SsRadioExampleParserTest()
    {
        XmlData = GetTestFileData("internetRadioStations_example_1");
    }

    protected override void CreateParserDelegate()
    {
        var prefetch = Library.GetElements(Account, SsIdParserDelegate.PrefetchIDs);
        SsParserDelegate = new SsRadioParserDelegate(prefetch, Account, Library, parseNotifier: null);
    }

    private static void CheckRadio(Radio radio, string id, string title, string url, string siteUrl)
    {
        AssertIsAccount1(radio.Account);
        Assert.Equal(id, radio.Id);
        Assert.Equal(title, radio.Title);
        Assert.Equal(0, radio.Rating);
        Assert.Equal(url, radio.Url);
        Assert.Equal(siteUrl, radio.SiteUrl);
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

    protected override void CheckCorrectParsing()
    {
        PrefetchIdTester.CheckPrefetchIdCounts(radioCount: 2);

        var radios = Library.GetRadios(Account).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        Assert.Equal(2, radios.Count);

        CheckRadio(radios[0], "0", "NRK P1", "http://lyd.nrk.no/nrk_radio_p1_ostlandssendingen_mp3_m", "http://www.nrk.no/p1");
        CheckRadio(radios[1], "1", "NRK P2", "http://lyd.nrk.no/nrk_radio_p2_mp3_m", "http://p3.no");
    }
}
