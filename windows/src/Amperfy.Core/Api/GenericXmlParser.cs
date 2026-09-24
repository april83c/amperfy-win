using System.Xml;

namespace Amperfy.Core.Api;

/// SAX style XML parser base (port of GenericXmlParser / XMLParserDelegate). Subclasses override
/// DidStartElement / DidEndElement; the character content of the current element is in Buffer.
public abstract class GenericXmlParser
{
    /// Text content collected since the last start/end element.
    protected string Buffer { get; set; } = "";
    protected int ParsedCount { get; set; }

    private readonly StringBuilder _buffer = new();

    /// Parses the data. Returns false (and sets ParserError) if the XML is malformed.
    public bool Parse(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = false,
                XmlResolver = null,
            });
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        {
                            var name = reader.LocalName;
                            var attributes = new Dictionary<string, string>();
                            if (reader.HasAttributes)
                            {
                                while (reader.MoveToNextAttribute())
                                {
                                    if (reader.Prefix == "xmlns" || reader.Name == "xmlns") continue;
                                    attributes[reader.LocalName] = reader.Value;
                                }
                                reader.MoveToElement();
                            }
                            var isEmpty = reader.IsEmptyElement;
                            _buffer.Clear();
                            Buffer = "";
                            DidStartElement(name, attributes);
                            if (isEmpty)
                            {
                                Buffer = "";
                                EndElement(name);
                            }
                            break;
                        }
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        _buffer.Append(reader.Value);
                        break;
                    case XmlNodeType.EndElement:
                        Buffer = _buffer.ToString();
                        EndElement(reader.LocalName);
                        break;
                }
            }
            return true;
        }
        catch (XmlException ex)
        {
            ParserError = ex;
            AmperfyLog.Error("Parser", $"XML error: {ex.Message}");
            return false;
        }
    }

    public Exception? ParserError { get; private set; }

    private void EndElement(string name)
    {
        DidEndElement(name);
        _buffer.Clear();
        Buffer = "";
    }

    protected virtual void DidStartElement(string elementName, IReadOnlyDictionary<string, string> attributes) { }

    protected virtual void DidEndElement(string elementName) { }

    /// Some operations are not allowed during parsing; call after Parse() (override if required).
    public virtual void PerformPostParseOperations() { }

    // --- helpers for subclasses ---

    protected static int? IntAttr(IReadOnlyDictionary<string, string> a, string key) =>
        a.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    protected static long? LongAttr(IReadOnlyDictionary<string, string> a, string key) =>
        a.TryGetValue(key, out var v) && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    protected static float? FloatAttr(IReadOnlyDictionary<string, string> a, string key) =>
        a.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;

    protected static bool? BoolAttr(IReadOnlyDictionary<string, string> a, string key) =>
        a.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : null;

    protected static string? StrAttr(IReadOnlyDictionary<string, string> a, string key) =>
        a.TryGetValue(key, out var v) ? v : null;

    protected int? BufferAsInt => int.TryParse(Buffer.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
    protected long? BufferAsLong => long.TryParse(Buffer.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
    protected float? BufferAsFloat => float.TryParse(Buffer.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;

    /// Parses ISO 8601 date strings as used by Subsonic/Ampache.
    protected static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;
    }
}
