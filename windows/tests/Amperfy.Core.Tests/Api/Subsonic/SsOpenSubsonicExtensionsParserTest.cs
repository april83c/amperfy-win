using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

public class SsOpenSubsonicExtensionsParserTest : AbstractSsParserTest
{
    public SsOpenSubsonicExtensionsParserTest()
    {
        XmlData = GetTestFileData("OpenSubsonicExtensions_example_1");
    }

    protected override void CreateParserDelegate()
    {
        SsParserDelegate = new SsOpenSubsonicExtensionsParserDelegate();
    }

    protected override void CheckCorrectParsing()
    {
        var extensionsParser = SsParserDelegate as SsOpenSubsonicExtensionsParserDelegate;
        Assert.NotNull(extensionsParser);
        PrefetchIdTester.CheckPrefetchIdCounts();

        var response = extensionsParser.OpenSubsonicExtensionsResponse;

        Assert.Equal("ok", response.Status);
        Assert.Equal("1.16.1", response.Version);
        Assert.Equal("navidrome", response.Type);
        Assert.Equal("0.52.5 (c5560888)", response.ServerVersion);
        Assert.True(response.OpenSubsonic);

        Assert.Equal(3, response.SupportedExtensions.Count);

        Assert.Equal("transcodeOffset", response.SupportedExtensions[0]);
        Assert.Equal("formPost", response.SupportedExtensions[1]);
        Assert.Equal("songLyrics", response.SupportedExtensions[2]);
    }
}
