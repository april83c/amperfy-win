using System.Text;
using System.Net;
using Amperfy.Core.Tests.Helper;

namespace Amperfy.Core.Tests.Api.Subsonic;

/// Fake Subsonic server: answers requests by action name ("getAlbum" for ".../rest/getAlbum.view")
/// and records all requests (url + headers).
public sealed class FakeSubsonicHttpHandler : HttpMessageHandler
{
    public sealed record RecordedRequest(Uri Url, Dictionary<string, string> Headers)
    {
        public string Action => Url.AbsolutePath.Split('/').Last().Replace(".view", "");

        public Dictionary<string, string?> Query =>
            Url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .GroupBy(p => Uri.UnescapeDataString(p[0]))
                .ToDictionary(g => g.Key, g => g.Last().Length > 1 ? Uri.UnescapeDataString(g.Last()[1]) : null);

        public List<string> QueryValues(string name) =>
            Url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(p => Uri.UnescapeDataString(p[0]) == name)
                .Select(p => Uri.UnescapeDataString(p[1]))
                .ToList();
    }

    public const string PingXml =
        """<?xml version="1.0" encoding="UTF-8"?><subsonic-response xmlns="http://subsonic.org/restapi" status="ok" version="1.16.1"></subsonic-response>""";

    private readonly object _lock = new();
    private readonly List<RecordedRequest> _requests = [];
    public Dictionary<string, byte[]> Responses { get; } = new() { ["ping"] = Encoding.UTF8.GetBytes(PingXml) };

    public IReadOnlyList<RecordedRequest> Requests
    {
        get { lock (_lock) return _requests.ToList(); }
    }

    public RecordedRequest LastRequest(string action) => Requests.Last(r => r.Action == action);

    public void Respond(string action, string xml) => Responses[action] = Encoding.UTF8.GetBytes(xml);

    public void RespondWithSample(string action, string sampleName) => Responses[action] = TestFiles.GetTestFileData("Subsonic", sampleName);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
        var recorded = new RecordedRequest(request.RequestUri!, headers);
        lock (_lock) _requests.Add(recorded);
        var response = Responses.TryGetValue(recorded.Action, out var data)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not found") };
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
