namespace Amperfy.Core.Api.Subsonic;

/// Parses the OpenSubsonic "getLyricsBySongId" response.
public class SsLyricsParserDelegate : SsXmlParser
{
    public LyricsList? LyricsList { get; private set; }
    private StructuredLyrics? _structuredLyrics;
    private List<LyricsLine>? _lines;
    private LyricsLine? _currentLine;

    protected override void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes)
    {
        base.DidStartElement(elementName, attributes);

        if (elementName == "lyricsList")
        {
            LyricsList = new LyricsList();
            _structuredLyrics = null;
            _lines = null;
            _currentLine = null;
        }
        else if (elementName == "structuredLyrics")
        {
            _structuredLyrics = new StructuredLyrics
            {
                DisplayArtist = StrAttr(attributes, "displayArtist"),
                DisplayTitle = StrAttr(attributes, "displayTitle"),
                Lang = StrAttr(attributes, "lang") ?? "",
                Offset = SwiftInt(StrAttr(attributes, "offset")) ?? 0,
                Synced = StrAttr(attributes, "synced") is { } isSynced && isSynced == "true",
            };
            _lines = [];
            _currentLine = null;
        }
        else if (elementName == "line")
        {
            _currentLine = new LyricsLine();
            if (SwiftInt(StrAttr(attributes, "start")) is { } start) _currentLine.Start = start;
        }
    }

    protected override void DidEndElement(string elementName)
    {
        if (elementName is "line" or "value" && _currentLine is { } curLine)
        {
            curLine.Value = Buffer;
            _lines?.Add(curLine);
            _currentLine = null;
        }
        else if (elementName == "structuredLyrics" && _structuredLyrics is { } curStructuredLyrics)
        {
            if (_lines is { } curLines) curStructuredLyrics.Line = curLines;
            LyricsList?.Lyrics.Add(curStructuredLyrics);
            _structuredLyrics = null;
            _lines = null;
        }
        base.DidEndElement(elementName);
    }
}
