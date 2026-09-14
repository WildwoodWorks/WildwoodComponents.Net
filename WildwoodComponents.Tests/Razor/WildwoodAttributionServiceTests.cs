using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// The server-side half of the Razor claim proxy: it forwards the browser's payload with the session token and the
/// configured app id, sends nothing it cannot authorize, and never throws.
/// </summary>
public class WildwoodAttributionServiceTests
{
    private static (WildwoodAttributionService Service, FakeHttpMessageHandler Handler) CreateService(
        string? accessToken = "jwt-1",
        string appId = "app-1")
    {
        var handler = new FakeHttpMessageHandler();
        var service = new WildwoodAttributionService(
            handler.CreateClient("https://api.test/api/"),
            new FakeSessionManager(accessToken),
            NullLogger<WildwoodAttributionService>.Instance,
            appId);
        return (service, handler);
    }

    private static AttributionPayloadModel Payload() => new()
    {
        VisitorKey = "visitor-key-0001",
        LastTouch = new AttributionTouchModel { Source = "reddit", Campaign = "spring-launch" }
    };

    [Fact]
    public async Task ClaimAsync_ForwardsThePayloadWithTheSessionTokenAndTheConfiguredAppId()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("attribution/claim", """{"recorded":true,"reason":null}""");

        var result = await service.ClaimAsync(Payload());

        Assert.True(result?.Recorded);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.test/api/attribution/claim?appId=app-1", request.Url);
        Assert.Equal("Bearer jwt-1", request.Authorization);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("app-1", body.RootElement.GetProperty("appId").GetString());
        Assert.Equal("visitor-key-0001", body.RootElement.GetProperty("visitorKey").GetString());
        Assert.Equal("reddit", body.RootElement.GetProperty("lastTouch").GetProperty("source").GetString());
        Assert.False(service.LastResponseUnauthorized);
    }

    [Fact]
    public async Task ClaimAsync_WithoutASignedInSession_SendsNothing()
    {
        var (service, handler) = CreateService(accessToken: null);

        var result = await service.ClaimAsync(Payload());

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ClaimAsync_WithoutAConfiguredAppId_SendsNothing()
    {
        var (service, handler) = CreateService(appId: "");

        var result = await service.ClaimAsync(Payload());

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ClaimAsync_WithoutATouch_SendsNothing()
    {
        var (service, handler) = CreateService();

        Assert.Null(await service.ClaimAsync(null));
        Assert.Null(await service.ClaimAsync(new AttributionPayloadModel { VisitorKey = "visitor-key-0001" }));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ClaimAsync_OnA401_ReportsTheDeadSessionToken()
    {
        var (service, handler) = CreateService();
        handler.DefaultStatus = HttpStatusCode.Unauthorized;

        var result = await service.ClaimAsync(Payload());

        Assert.Null(result);
        Assert.True(service.LastResponseUnauthorized);
    }

    [Fact]
    public async Task ClaimAsync_OnAServerError_ReturnsNull()
    {
        var (service, handler) = CreateService();
        handler.DefaultStatus = HttpStatusCode.InternalServerError;

        var result = await service.ClaimAsync(Payload());

        Assert.Null(result);
        Assert.False(service.LastResponseUnauthorized);
    }

    [Fact]
    public async Task ClaimAsync_WhenTheRequestThrows_ReturnsNullWithoutThrowing()
    {
        var service = new WildwoodAttributionService(
            new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://api.test/api/") },
            new FakeSessionManager("jwt-1"),
            NullLogger<WildwoodAttributionService>.Instance,
            "app-1");

        AttributionClaimResultModel? result = new();
        var exception = await Record.ExceptionAsync(async () => result = await service.ClaimAsync(Payload()));

        Assert.Null(exception);
        Assert.Null(result);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused"));
    }
}
