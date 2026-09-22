using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// Campaign Attribution on the Blazor authentication service, ported case for case from
/// <c>@wildwood/core</c>'s <c>authService.test.ts</c> ("AuthService campaign attribution"): every
/// registration path attaches the captured payload when the caller supplies none and clears the touches
/// once the signup is recorded, and a provider sign-in queues a claim that goes out with the first
/// session — at most once, and never later than the fifteen-minute window.
/// </summary>
public class AuthenticationServiceAttributionTests
{
    private const string ApiRoot = "https://api.test/";

    private static AttributionPayloadModel Payload() => new()
    {
        VisitorKey = "visitor-key-0001",
        LastTouch = new AttributionTouchModel
        {
            Source = "reddit",
            Medium = "paid",
            Campaign = "govcon-test-sep26",
            Content = "ad1",
            OccurredAt = "2026-09-13T12:00:00.000Z"
        }
    };

    private static RegistrationRequest Registration() => new()
    {
        Email = "new@example.com",
        FirstName = "Jane",
        LastName = "Doe",
        Password = "Passw0rd!",
        AppId = "app-1"
    };

    /// <summary>
    /// The single request to an endpoint. RegisterAsync reads the password policy first, so the
    /// registration POST is never simply the first request recorded.
    /// </summary>
    private static FakeHttpMessageHandler.RecordedRequest Request(FakeHttpMessageHandler handler, string urlContains)
    {
        FakeHttpMessageHandler.RecordedRequest? match = null;
        foreach (var request in handler.Requests)
        {
            if (!request.Url.Contains(urlContains, StringComparison.OrdinalIgnoreCase)) continue;
            Assert.Null(match);
            match = request;
        }

        Assert.NotNull(match);
        return match!;
    }

    private static (AuthenticationService Service, FakeHttpMessageHandler Handler, RecordingAttributionService Attribution)
        CreateService(AttributionPayloadModel? captured)
    {
        var handler = new FakeHttpMessageHandler();
        var attribution = new RecordingAttributionService(captured);
        var service = new AuthenticationService(
            handler.CreateClient(ApiRoot),
            new FakeLocalStorageService(),
            NullLogger<AuthenticationService>.Instance,
            attribution);
        return (service, handler, attribution);
    }

    // ── register: attach, explicit wins, clear only on success ──────────────────

    [Fact]
    public async Task RegisterAsync_AttachesTheCapturedPayloadAndClearsItAfterwards()
    {
        var (service, handler, attribution) = CreateService(Payload());
        handler.WhenOk("auth/register", """{"jwtToken":"jwt-1","refreshToken":"r-1","email":"new@example.com"}""");

        await service.RegisterAsync(Registration());

        using var body = JsonDocument.Parse(Request(handler, "auth/register").Body!);
        var sent = body.RootElement.GetProperty("attribution");
        Assert.Equal("visitor-key-0001", sent.GetProperty("visitorKey").GetString());
        Assert.Equal("reddit", sent.GetProperty("lastTouch").GetProperty("source").GetString());
        Assert.Equal(1, attribution.ClearCalls);
    }

    [Fact]
    public async Task RegisterAsync_WithAnExplicitPayload_DoesNotAskTheEngine()
    {
        // The component path and the service path have to be idempotent: TokenRegistrationComponent and
        // SignupWithSubscriptionComponent attach the payload themselves, and theirs must win.
        var (service, handler, attribution) = CreateService(Payload());
        handler.WhenOk("auth/register", """{"jwtToken":"jwt-1"}""");
        var request = Registration();
        request.Attribution = new AttributionPayloadModel { VisitorKey = "explicit-key-0001" };

        await service.RegisterAsync(request);

        using var body = JsonDocument.Parse(Request(handler, "auth/register").Body!);
        Assert.Equal("explicit-key-0001", body.RootElement.GetProperty("attribution").GetProperty("visitorKey").GetString());
        Assert.Equal(0, attribution.GetForRegistrationCalls);
    }

    [Fact]
    public async Task RegisterAsync_WithNothingCaptured_SendsNoAttribution()
    {
        var (service, handler, _) = CreateService(captured: null);
        handler.WhenOk("auth/register", """{"jwtToken":"jwt-1"}""");

        await service.RegisterAsync(Registration());

        using var body = JsonDocument.Parse(Request(handler, "auth/register").Body!);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("attribution").ValueKind);
    }

    [Fact]
    public async Task RegisterAsync_WhenTheRequestFails_KeepsTheTouches()
    {
        var (service, handler, attribution) = CreateService(Payload());
        handler.When("auth/register", System.Net.HttpStatusCode.BadRequest, """{"message":"Email already registered"}""");

        await Assert.ThrowsAsync<AuthenticationException>(() => service.RegisterAsync(Registration()));

        Assert.Equal(0, attribution.ClearCalls);
    }

    // ── registerWithToken: the same contract on the token route ─────────────────

    [Fact]
    public async Task RegisterWithTokenAsync_AttachesTheCapturedPayloadAndClearsItOnSuccess()
    {
        var (service, handler, attribution) = CreateService(Payload());
        handler.WhenOk("userregistration/register-with-token", """{"success":true,"message":"ok","userId":"user-9"}""");
        var request = Registration();
        request.RegistrationToken = "token-1";

        await service.RegisterWithTokenAsync(request);

        // The DTO's members are declared PascalCase like the JS SDK's, but PostAsJsonAsync serializes
        // with the web defaults, so the wire name is camelCase. WildwoodAPI binds either.
        using var body = JsonDocument.Parse(Request(handler, "register-with-token").Body!);
        Assert.Equal("visitor-key-0001", body.RootElement.GetProperty("attribution").GetProperty("visitorKey").GetString());
        Assert.Equal(1, attribution.ClearCalls);
    }

    [Fact]
    public async Task RegisterWithTokenAsync_OnATokenLessFailure_KeepsTheTouches()
    {
        var (service, handler, attribution) = CreateService(Payload());
        handler.WhenOk("userregistration/register-with-token", """{"success":false,"message":"Invalid token"}""");
        var request = Registration();
        request.RegistrationToken = "token-1";

        await Assert.ThrowsAsync<AuthenticationException>(() => service.RegisterWithTokenAsync(request));

        Assert.Equal(0, attribution.ClearCalls);
    }

    // ── the provider-sign-in claim ──────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_WithAProviderToken_ClaimsTheAttributionForTheNewAccount()
    {
        var (service, handler, attribution) = CreateService(Payload());
        handler.WhenOk("auth/login", """{"jwtToken":"jwt-1","refreshToken":"r-1"}""");
        handler.WhenOk("attribution/claim", """{"recorded":true,"reason":null}""");

        await service.LoginAsync(new LoginRequest
        {
            Username = string.Empty,
            ProviderName = "Google",
            ProviderToken = "provider-token",
            AppId = "app-1"
        });

        Assert.Equal(2, handler.Requests.Count);
        var claim = Request(handler, "attribution/claim");
        Assert.Equal("https://api.test/api/attribution/claim?appId=app-1", claim.Url);
        Assert.Equal("Bearer jwt-1", claim.Authorization);
        Assert.Equal(1, attribution.ClearCalls);
    }

    [Fact]
    public async Task LoginAsync_WithAPassword_DoesNotClaim()
    {
        var (service, handler, attribution) = CreateService(Payload());
        handler.WhenOk("auth/login", """{"jwtToken":"jwt-1","refreshToken":"r-1"}""");

        await service.LoginAsync(new LoginRequest { Username = "john", Password = "pass", AppId = "app-1" });

        Assert.Single(handler.Requests);
        Assert.Equal(0, attribution.GetForRegistrationCalls);
    }

    [Fact]
    public async Task AProviderSignInDeferredByTwoFactor_ClaimsOnceVerificationSignsIn()
    {
        var (service, handler, attribution) = CreateService(Payload());
        handler.WhenOk("auth/login", """{"requiresTwoFactor":true,"twoFactorSessionId":"s-1","jwtToken":""}""");
        handler.WhenOk("twofactor/verify", """{"success":true,"authResponse":{"jwtToken":"jwt-1","refreshToken":"r-1"}}""");
        handler.WhenOk("attribution/claim", """{"recorded":true,"reason":null}""");

        await service.LoginAsync(new LoginRequest
        {
            Username = string.Empty,
            ProviderName = "Google",
            ProviderToken = "provider-token",
            AppId = "app-1"
        });

        // The session does not exist yet, so nothing has been claimed.
        Assert.Single(handler.Requests);
        Assert.Equal(0, attribution.GetForRegistrationCalls);

        await service.VerifyTwoFactorCodeAsync(new TwoFactorVerifyRequest { SessionId = "s-1", Code = "123456" });

        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("attribution/claim", handler.Requests[2].Url, StringComparison.Ordinal);
        Assert.Equal(1, attribution.ClearCalls);
    }

    [Fact]
    public async Task AQueuedClaim_IsSentAtMostOnce()
    {
        var (service, handler, attribution) = CreateService(Payload());
        handler.WhenOk("auth/login", """{"jwtToken":"jwt-1","refreshToken":"r-1"}""");
        handler.WhenOk("attribution/claim", """{"recorded":true,"reason":null}""");

        service.QueueAttributionClaim("app-1");
        await service.LoginAsync(new LoginRequest { Username = "john", Password = "pass", AppId = "app-1" });
        await service.LoginAsync(new LoginRequest { Username = "john", Password = "pass", AppId = "app-1" });

        Assert.Equal(1, attribution.GetForRegistrationCalls);
        Assert.Equal(1, attribution.ClearCalls);
    }

    [Fact]
    public async Task AQueuedClaim_IsDroppedBySignOut()
    {
        var (service, handler, attribution) = CreateService(Payload());
        handler.WhenOk("auth/login", """{"jwtToken":"jwt-1","refreshToken":"r-1"}""");

        service.QueueAttributionClaim("app-1");
        await service.LogoutAsync();
        await service.LoginAsync(new LoginRequest { Username = "john", Password = "pass", AppId = "app-1" });

        Assert.Equal(0, attribution.GetForRegistrationCalls);
    }

    [Fact]
    public void QueueAttributionClaim_WithoutAnAppId_QueuesNothing()
    {
        var (service, _, _) = CreateService(Payload());

        var exception = Record.Exception(() => service.QueueAttributionClaim("  "));

        Assert.Null(exception);
    }

    [Fact]
    public async Task AQueuedClaim_LapsesAfterTheFifteenMinuteWindow()
    {
        // The window matches @wildwood/core's ATTRIBUTION_CLAIM_WINDOW_MS and the server's own claim
        // window: an hour-old provider sign-in is no longer evidence for these touches.
        var (service, handler, attribution) = CreateService(Payload());
        handler.WhenOk("auth/login", """{"jwtToken":"jwt-1","refreshToken":"r-1"}""");

        service.QueueAttributionClaim("app-1");
        Backdate(service, TimeSpan.FromMinutes(16));
        await service.LoginAsync(new LoginRequest { Username = "john", Password = "pass", AppId = "app-1" });

        Assert.Single(handler.Requests);
        Assert.Equal(0, attribution.GetForRegistrationCalls);
    }

    [Fact]
    public async Task AFailedClaim_DoesNotBreakTheSignIn()
    {
        var (service, handler, attribution) = CreateService(Payload());
        handler.WhenOk("auth/login", """{"jwtToken":"jwt-1","refreshToken":"r-1"}""");
        handler.When("attribution/claim", System.Net.HttpStatusCode.InternalServerError, "{}");

        var response = await service.LoginAsync(new LoginRequest
        {
            Username = string.Empty,
            ProviderName = "Google",
            ProviderToken = "provider-token",
            AppId = "app-1"
        });

        Assert.Equal("jwt-1", response.JwtToken);
        Assert.Equal(0, attribution.ClearCalls);
    }

    /// <summary>
    /// Moves the queued claim back in time. The field is private because nothing outside the service has
    /// any business rewriting it; a test clock would be a larger change to a service that reads
    /// <c>DateTimeOffset.UtcNow</c> in exactly this one place.
    /// </summary>
    private static void Backdate(AuthenticationService service, TimeSpan by)
    {
        var field = typeof(AuthenticationService).GetField(
            "_queuedAttributionClaim",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);

        var queued = ((string AppId, DateTimeOffset QueuedAt)?)field!.GetValue(service);
        Assert.NotNull(queued);
        field.SetValue(service, (queued!.Value.AppId, queued.Value.QueuedAt - by));
    }

    private sealed class RecordingAttributionService : IAttributionService
    {
        private readonly AttributionPayloadModel? _payload;

        public RecordingAttributionService(AttributionPayloadModel? payload) => _payload = payload;

        public int GetForRegistrationCalls { get; private set; }

        public int ClearCalls { get; private set; }

        public Task<AttributionStateModel?> InitializeAsync(string appId, string? baseUrlOverride = null)
            => Task.FromResult<AttributionStateModel?>(null);

        public Task<AttributionTouchModel?> CaptureUrlAsync(string url, string? referrer = null)
            => Task.FromResult<AttributionTouchModel?>(null);

        public Task<AttributionPayloadModel?> GetForRegistrationAsync()
        {
            GetForRegistrationCalls++;
            return Task.FromResult(_payload);
        }

        public Task ClearAsync()
        {
            ClearCalls++;
            return Task.CompletedTask;
        }

        public Task<AttributionStateModel?> GetStateAsync() => Task.FromResult<AttributionStateModel?>(null);
    }
}
