using Amperfy.Core.Tests.Helper;
using Amperfy.Core.Api.Subsonic;

namespace Amperfy.Core.Tests.Api.Subsonic;

/// Shared checks of SsLyricsBySongId1ParserTest / SsLyricsBySongId2ParserTest (both samples
/// contain the same lyrics in a different xml layout).
public abstract class AbstractSsLyricsBySongIdParserTest : AbstractSsParserTest
{
    protected AbstractSsLyricsBySongIdParserTest(string sampleName)
    {
        XmlData = GetTestFileData(sampleName);
        SsParserDelegate = new SsLyricsParserDelegate();
    }

    protected override void CreateParserDelegate()
    {
        SsParserDelegate = new SsLyricsParserDelegate();
    }

    protected override void CheckCorrectParsing()
    {
        var lyricsParser = SsParserDelegate as SsLyricsParserDelegate;
        Assert.NotNull(lyricsParser);
        var lyricsList = lyricsParser.LyricsList;
        Assert.NotNull(lyricsList);
        PrefetchIdTester.CheckPrefetchIdCounts();

        Assert.Equal(2, lyricsList.Lyrics.Count);

        var structuredLyrics = lyricsList.Lyrics[0];
        Assert.Equal("Muse", structuredLyrics.DisplayArtist);
        Assert.Equal("Hysteria", structuredLyrics.DisplayTitle);
        Assert.Equal("en", structuredLyrics.Lang);
        Assert.Equal(-100, structuredLyrics.Offset);
        Assert.True(structuredLyrics.Synced);
        Assert.Equal(3, structuredLyrics.Line.Count);
        Assert.Equal(0, structuredLyrics.Line[0].Start);
        Assert.Equal("It's bugging me", structuredLyrics.Line[0].Value);
        Assert.Equal(2000, structuredLyrics.Line[1].Start);
        Assert.Equal("Grating me", structuredLyrics.Line[1].Value);
        Assert.Equal(3001, structuredLyrics.Line[2].Start);
        Assert.Equal("And twisting me around...", structuredLyrics.Line[2].Value);

        structuredLyrics = lyricsList.Lyrics[1];
        Assert.Equal("Mu2se", structuredLyrics.DisplayArtist);
        Assert.Equal("Hy2steria", structuredLyrics.DisplayTitle);
        Assert.Equal("de", structuredLyrics.Lang);
        Assert.Equal(100, structuredLyrics.Offset);
        Assert.False(structuredLyrics.Synced);
        Assert.Equal(3, structuredLyrics.Line.Count);
        Assert.Null(structuredLyrics.Line[0].Start);
        Assert.Equal("It's bugging2 me", structuredLyrics.Line[0].Value);
        Assert.Null(structuredLyrics.Line[1].Start);
        Assert.Equal("Grating2 me", structuredLyrics.Line[1].Value);
        Assert.Null(structuredLyrics.Line[2].Start);
        Assert.Equal("And twisting2 me around...", structuredLyrics.Line[2].Value);
    }
}
