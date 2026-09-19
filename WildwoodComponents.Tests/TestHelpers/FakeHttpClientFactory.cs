namespace WildwoodComponents.Tests.TestHelpers;

/// <summary>
/// An <see cref="IHttpClientFactory"/> that hands out one client, so a service built the way the
/// library builds it (from the named "WildwoodAPI" client) can be driven by
/// <see cref="FakeHttpMessageHandler"/>.
/// </summary>
public class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _client;

    public FakeHttpClientFactory(HttpClient client)
    {
        _client = client;
    }

    /// <summary>Every client name the caller asked for, newest last.</summary>
    public List<string> RequestedNames { get; } = new();

    public HttpClient CreateClient(string name)
    {
        RequestedNames.Add(name);
        return _client;
    }
}
