using System.Net;
using System.Text.Json;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// The sign-in provider claim (AttributionServiceExtensions.ClaimAsync) mirrors @wildwood/core claimAttribution: one
/// authenticated POST, the touches cleared on any 2xx, and a failure path that keeps them and never breaks a sign-in.
/// </summary>
public class AttributionClaimTests
{
    private const string ApiRoot = "https://api.example.com/";

    private static AttributionPayloadModel Payload() => new()
    {
        VisitorKey = "visitor-key-0001",
        FirstTouch = new AttributionTouchModel { Source = "reddit", Campaign = "spring-launch", Content = "ad1" },
        LastTouch = new AttributionTouchModel { Source = "reddit", Campaign = "spring-launch", Content = "ad2" }
    };

    [Fact]
    public async Task ClaimAsync_PostsThePayloadWithTheBearerTokenAndClearsTheTouches()
    {
        var attribution = new FakeAttributionService(Payload());
        var handler = new FakeHttpMessageHandler().WhenOk("api/attribution/claim", """{"recorded":true,"reason":null}""");

        var result = await attribution.ClaimAsync(handler.CreateClient(ApiRoot), "app-1", "jwt-1");

        Assert.True(result?.Recorded);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.example.com/api/attribution/claim?appId=app-1", request.Url);
        Assert.Equal("Bearer jwt-1", request.Authorization);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("app-1", body.RootElement.GetProperty("appId").GetString());
        Assert.Equal("visitor-key-0001", body.RootElement.GetProperty("visitorKey").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("dotnet", body.RootElement.GetProperty("sdk").GetString());
        Assert.Equal("ad1", body.RootElement.GetProperty("firstTouch").GetProperty("content").GetString());
        Assert.Equal("ad2", body.RootElement.GetProperty("lastTouch").GetProperty("content").GetString());

        Assert.Equal(1, attribution.ClearCalls);
    }

    [Fact]
    public async Task ClaimAsync_ClearsTheTouchesEvenWhenTheServerDoesNotRecordThem()
    {
        // recorded:false (AlreadyRecorded, WindowExpired, ...) is still the server's final answer for these touches.
        var attribution = new FakeAttributionService(Payload());
        var handler = new FakeHttpMessageHandler().WhenOk("api/attribution/claim", """{"recorded":false,"reason":"AlreadyRecorded"}""");

        var result = await attribution.ClaimAsync(handler.CreateClient(ApiRoot), "app-1", "jwt-1");

        Assert.NotNull(result);
        Assert.False(result!.Recorded);
        Assert.Equal("AlreadyRecorded", result.Reason);
        Assert.Equal(1, attribution.ClearCalls);
    }

    [Theory]
    [InlineData(null, "jwt-1")]
    [InlineData("  ", "jwt-1")]
    [InlineData("app-1", null)]
    [InlineData("app-1", "")]
    public async Task ClaimAsync_WithoutAnAppIdOrToken_SendsNothing(string? appId, string? jwtToken)
    {
        var attribution = new FakeAttributionService(Payload());
        var handler = new FakeHttpMessageHandler();

        var result = await attribution.ClaimAsync(handler.CreateClient(ApiRoot), appId, jwtToken);

        Assert.Null(result);
        Assert.Empty(handler.Requests);
        Assert.Equal(0, attribution.ClearCalls);
    }

    [Fact]
    public async Task ClaimAsync_WithNothingCaptured_SendsNothing()
    {
        var attribution = new FakeAttributionService(payload: null);
        var handler = new FakeHttpMessageHandler();

        var result = await attribution.ClaimAsync(handler.CreateClient(ApiRoot), "app-1", "jwt-1");

        Assert.Null(result);
        Assert.Empty(handler.Requests);
        Assert.Equal(0, attribution.ClearCalls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ClaimAsync_OnAnErrorStatus_KeepsTheTouches(HttpStatusCode status)
    {
        var attribution = new FakeAttributionService(Payload());
        var handler = new FakeHttpMessageHandler { DefaultStatus = status };

        var result = await attribution.ClaimAsync(handler.CreateClient(ApiRoot), "app-1", "jwt-1");

        Assert.Null(result);
        Assert.Single(handler.Requests);
        Assert.Equal(0, attribution.ClearCalls);
    }

    [Fact]
    public async Task ClaimAsync_WhenTheRequestThrows_ReturnsNullWithoutThrowing()
    {
        var attribution = new FakeAttributionService(Payload());
        using var http = new HttpClient(new ThrowingHandler(new HttpRequestException("connection refused")))
        {
            BaseAddress = new Uri(ApiRoot)
        };

        AttributionClaimResultModel? result = new();
        var exception = await Record.ExceptionAsync(async () => result = await attribution.ClaimAsync(http, "app-1", "jwt-1"));

        Assert.Null(exception);
        Assert.Null(result);
        Assert.Equal(0, attribution.ClearCalls);
    }

    [Fact]
    public async Task ClaimAsync_WhenCancelled_ReturnsNullWithoutThrowing()
    {
        // The same path the built-in ClaimTimeout takes: a claim that does not answer in time is abandoned.
        var attribution = new FakeAttributionService(Payload());
        using var http = new HttpClient(new HangingHandler()) { BaseAddress = new Uri(ApiRoot) };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        AttributionClaimResultModel? result = new();
        var exception = await Record.ExceptionAsync(async () =>
            result = await attribution.ClaimAsync(http, "app-1", "jwt-1", cancellation.Token));

        Assert.Null(exception);
        Assert.Null(result);
        Assert.Equal(0, attribution.ClearCalls);
    }

    private sealed class FakeAttributionService : IAttributionService
    {
        private readonly AttributionPayloadModel? _payload;

        public FakeAttributionService(AttributionPayloadModel? payload) => _payload = payload;

        public int ClearCalls { get; private set; }

        public Task<AttributionStateModel?> InitializeAsync(string appId, string? baseUrlOverride = null)
            => Task.FromResult<AttributionStateModel?>(null);

        public Task<AttributionTouchModel?> CaptureUrlAsync(string url, string? referrer = null)
            => Task.FromResult<AttributionTouchModel?>(null);

        public Task<AttributionPayloadModel?> GetForRegistrationAsync() => Task.FromResult(_payload);

        public Task ClearAsync()
        {
            ClearCalls++;
            return Task.CompletedTask;
        }

        public Task<AttributionStateModel?> GetStateAsync() => Task.FromResult<AttributionStateModel?>(null);
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
