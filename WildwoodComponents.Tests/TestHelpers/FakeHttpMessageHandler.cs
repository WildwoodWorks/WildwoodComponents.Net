using System.Net;
using System.Text;

namespace WildwoodComponents.Tests.TestHelpers;

/// <summary>
/// Records every request (method, URL, body) and returns canned responses.
/// Responses are matched by URL substring, falling back to a default response.
/// </summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    public record RecordedRequest(HttpMethod Method, string Url, string? Body, string? Authorization = null);

    private readonly List<(string UrlContains, HttpStatusCode Status, string Json)> _responses = new();
    public List<RecordedRequest> Requests { get; } = new();

    public HttpStatusCode DefaultStatus { get; set; } = HttpStatusCode.OK;
    public string DefaultJson { get; set; } = "{}";

    public FakeHttpMessageHandler When(string urlContains, HttpStatusCode status, string json)
    {
        _responses.Add((urlContains, status, json));
        return this;
    }

    public FakeHttpMessageHandler WhenOk(string urlContains, string json)
        => When(urlContains, HttpStatusCode.OK, json);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = null;
        if (request.Content != null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        var url = request.RequestUri?.ToString() ?? string.Empty;
        Requests.Add(new RecordedRequest(request.Method, url, body, request.Headers.Authorization?.ToString()));

        foreach (var (urlContains, status, json) in _responses)
        {
            if (url.Contains(urlContains, StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(status)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
        }

        return new HttpResponseMessage(DefaultStatus)
        {
            Content = new StringContent(DefaultJson, Encoding.UTF8, "application/json")
        };
    }

    public HttpClient CreateClient(string? baseAddress = null)
    {
        var client = new HttpClient(this);
        if (baseAddress != null)
        {
            client.BaseAddress = new Uri(baseAddress);
        }
        return client;
    }
}
