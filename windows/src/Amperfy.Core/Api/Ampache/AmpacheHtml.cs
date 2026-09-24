using System.Text.RegularExpressions;

namespace Amperfy.Core.Api.Ampache;

/// Port of Swift's String.html2String as used by the Ampache parsers: the text is converted from
/// HTML twice (Ampache delivers HTML escaped inside CDATA, e.g. "&amp;lt;br/&amp;gt;"), the result is
/// not trimmed ("...&lt;br/&gt;" ends with "\n" like NSAttributedString produces it).
public static partial class AmpacheHtml
{
    [GeneratedRegex(@"<\s*br\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex(@"<\s*/\s*p\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphEndRegex();

    /// Only well formed tags ("&lt;" followed by a letter, '/' or '!'); a lone "&lt;" stays text.
    [GeneratedRegex(@"<[A-Za-z/!][^<>]*>")]
    private static partial Regex TagRegex();

    public static string Html2String(string html) => ConvertOnce(ConvertOnce(html));

    private static string ConvertOnce(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var text = LineBreakRegex().Replace(html, "\n");
        text = ParagraphEndRegex().Replace(text, "\n");
        text = TagRegex().Replace(text, "");
        return System.Net.WebUtility.HtmlDecode(text);
    }
}
