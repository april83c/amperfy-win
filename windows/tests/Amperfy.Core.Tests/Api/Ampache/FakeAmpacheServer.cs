using System.Text;
using System.Globalization;
using System.Net;
using Amperfy.Core.Api.Ampache;

namespace Amperfy.Core.Tests.Api.Ampache;

/// Fake Ampache server: answers the handshake with a token and all other actions from a map.
public sealed class FakeAmpacheServer : HttpMessageHandler
{
    public sealed record RecordedRequest(Uri Url, Dictionary<string, string> Query, Dictionary<string, string> Headers);

    public List<RecordedRequest> Requests { get; } = [];
    public Dictionary<string, Func<RecordedRequest, byte[]>> Actions { get; } = [];
    public string Token { get; set; } = "token-1";
    public string ApiVersion { get; set; } = "500000";
    public TimeSpan SessionDuration { get; set; } = TimeSpan.FromHours(1);
    public int ArtistCount { get; set; } = 3;
    public int AlbumCount { get; set; } = 3;
    /// Overrides the handshake response.
    public byte[]? HandshakeResponse { get; set; }

    public IEnumerable<RecordedRequest> RequestsFor(string action) => Requests.Where(r => r.Query.GetValueOrDefault("action") == action);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var query = (AmpacheUrlComponents.ParseQueryItems(request.RequestUri!.Query) ?? [])
            .GroupBy(i => i.Key).ToDictionary(g => g.Key, g => g.First().Value ?? "");
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
        var recorded = new RecordedRequest(request.RequestUri, query, headers);
        lock (Requests) Requests.Add(recorded);

        byte[] body;
        var action = query.GetValueOrDefault("action");
        if (action == "handshake")
        {
            body = HandshakeResponse ?? Encoding.UTF8.GetBytes(CreateHandshakeXml());
        }
        else if (action is not null && Actions.TryGetValue(action, out var handler))
        {
            body = handler(recorded);
        }
        else
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    public string CreateHandshakeXml()
    {
        var expire = DateTime.UtcNow.Add(SessionDuration).ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <root>
              <auth><![CDATA[{Token}]]></auth>
              <api><![CDATA[{ApiVersion}]]></api>
              <session_expire><![CDATA[{expire}]]></session_expire>
              <update><![CDATA[2021-03-31T17:15:25+10:00]]></update>
              <add><![CDATA[2021-03-31T13:32:27+10:00]]></add>
              <clean><![CDATA[2021-03-31T17:15:18+10:00]]></clean>
              <songs><![CDATA[55]]></songs>
              <albums><![CDATA[{AlbumCount}]]></albums>
              <artists><![CDATA[{ArtistCount}]]></artists>
              <genres><![CDATA[6]]></genres>
              <playlists><![CDATA[19]]></playlists>
              <podcasts><![CDATA[3]]></podcasts>
            </root>
            """;
    }

    public static byte[] ErrorXml(int code, string message) => Encoding.UTF8.GetBytes($"""
        <?xml version="1.0" encoding="UTF-8" ?>
        <root>
            <error errorCode="{code}">
                <errorAction><![CDATA[test]]></errorAction>
                <errorType><![CDATA[system]]></errorType>
                <errorMessage><![CDATA[{message}]]></errorMessage>
            </error>
        </root>
        """);
}
