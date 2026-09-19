using System.Net;
using System.Text;

namespace WildwoodComponents.Tests.TestHelpers;

/// <summary>
/// <see cref="FakeHttpMessageHandler"/>'s sibling for flows where the SAME route has to answer
/// differently on a second call — a login that fails and then succeeds, a quote asked for twice.
/// Routes are matched by URL substring in registration order, and each one is told how many times
/// it has already answered.
/// </summary>
public class ScriptedHttpMessageHandler : HttpMessageHandler
{
    public record RecordedRequest(HttpMethod Method, string Url, string? Body);

    private readonly List<(string Match, Func<int, (HttpStatusCode Status, string Json)> Reply)> _routes = new();
    private readonly Dictionary<string, int> _hits = new(StringComparer.Ordinal);

    /// <summary>Every request, in the order it was made.</summary>
    public List<RecordedRequest> Requests { get; } = new();

    public HttpStatusCode DefaultStatus { get; set; } = HttpStatusCode.OK;

    public string DefaultJson { get; set; } = "{}";

    /// <summary>Answers <paramref name="match"/> by call number, starting at 0.</summary>
    public ScriptedHttpMessageHandler On(string match, Func<int, (HttpStatusCode Status, string Json)> reply)
    {
        _routes.Add((match, reply));
        return this;
    }

    /// <summary>Answers <paramref name="match"/> with the same 200 every time.</summary>
    public ScriptedHttpMessageHandler On(string match, string json)
    {
        return On(match, _ => (HttpStatusCode.OK, json));
    }

    /// <summary>Answers <paramref name="match"/> with one status and body every time.</summary>
    public ScriptedHttpMessageHandler On(string match, HttpStatusCode status, string json)
    {
        return On(match, _ => (status, json));
    }

    /// <summary>How many times a route was asked.</summary>
    public int Count(string match)
    {
        var hits = 0;
        foreach (var request in Requests)
        {
            if (request.Url.Contains(match, StringComparison.OrdinalIgnoreCase)) hits++;
        }

        return hits;
    }

    /// <summary>The URLs, in order, of every request whose URL contains <paramref name="match"/>.</summary>
    public List<RecordedRequest> All(string match)
    {
        var matched = new List<RecordedRequest>();
        foreach (var request in Requests)
        {
            if (request.Url.Contains(match, StringComparison.OrdinalIgnoreCase)) matched.Add(request);
        }

        return matched;
    }

    /// <summary>The single request whose URL contains <paramref name="match"/>.</summary>
    public RecordedRequest Single(string match)
    {
        var matched = All(match);
        if (matched.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one request matching '{match}', found {matched.Count}.");
        }

        return matched[0];
    }

    /// <summary>The position of the first request matching <paramref name="match"/>, or -1.</summary>
    public int IndexOf(string match)
    {
        for (var i = 0; i < Requests.Count; i++)
        {
            if (Requests[i].Url.Contains(match, StringComparison.OrdinalIgnoreCase)) return i;
        }

        return -1;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        var url = request.RequestUri?.ToString() ?? string.Empty;
        Requests.Add(new RecordedRequest(request.Method, url, body));

        foreach (var (match, reply) in _routes)
        {
            if (!url.Contains(match, StringComparison.OrdinalIgnoreCase)) continue;

            int seen;
            _hits.TryGetValue(match, out seen);
            _hits[match] = seen + 1;

            var (status, json) = reply(seen);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        return new HttpResponseMessage(DefaultStatus)
        {
            Content = new StringContent(DefaultJson, Encoding.UTF8, "application/json")
        };
    }

    public HttpClient CreateClient(string baseAddress = "https://api.test/")
    {
        return new HttpClient(this) { BaseAddress = new Uri(baseAddress) };
    }
}
