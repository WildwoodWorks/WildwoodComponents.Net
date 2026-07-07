using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

public class WildwoodAppTierServiceTests
{
    private static (WildwoodAppTierService Service, FakeHttpMessageHandler Handler, FakeSessionManager Session) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var session = new FakeSessionManager();
        var service = new WildwoodAppTierService(
            handler.CreateClient("https://api.test/api/"),
            session,
            NullLogger<WildwoodAppTierService>.Instance);
        return (service, handler, session);
    }

    [Fact]
    public async Task GetTierAsync_CallsTierByIdEndpoint_WithAuthHeader()
    {
        var (service, handler, session) = CreateService();
        handler.WhenOk("app-tiers/tier/", """{"id":"tier-9","name":"Enterprise"}""");

        var tier = await service.GetTierAsync("tier-9");

        Assert.NotNull(tier);
        Assert.Equal("Enterprise", tier.Name);
        Assert.True(session.ApplyAuthorizationHeaderCalls > 0);
        Assert.Contains("app-tiers/tier/tier-9", handler.Requests[0].Url);
    }

    [Fact]
    public async Task GetCompanyFeaturesAsync_UsesAppScopedAdminEndpoint()
    {
        // Regression: companies/{id}/features is platform-only and ignores appId
        var (service, handler, _) = CreateService();
        handler.WhenOk("admin/company-features", """{"FEATURE_X":true}""");

        var features = await service.GetCompanyFeaturesAsync("app-1", "company-3");

        Assert.True(features["FEATURE_X"]);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("app-tiers/app-1/admin/company-features/company-3", request.Url);
        Assert.DoesNotContain("companies/", request.Url);
    }

    [Fact]
    public async Task GetTierAsync_ReturnsNull_OnError()
    {
        var (service, handler, _) = CreateService();
        handler.When("app-tiers/tier/", System.Net.HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        var tier = await service.GetTierAsync("tier-9");

        Assert.Null(tier);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_SurfacesScheduledCancelPayload()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("my-subscription/cancel",
            """{"success":true,"isScheduled":true,"effectiveDate":"2026-08-01T00:00:00Z"}""");

        var result = await service.CancelSubscriptionAsync("app-1");

        Assert.True(result.Success);
        Assert.True(result.IsScheduled);
        Assert.Equal(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), result.EffectiveDate!.Value.ToUniversalTime());
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("app-tiers/app-1/my-subscription/cancel", request.Url);
    }

    [Fact]
    public async Task CancelCompanySubscriptionAsync_ReportsFailure_WithoutThrowing()
    {
        var (service, handler, _) = CreateService();
        handler.When("cancel/company/", System.Net.HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        var result = await service.CancelCompanySubscriptionAsync("app-1", "company-3");

        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
    }

    [Fact]
    public async Task GetMySubscriptionAsync_ReturnsNull_OnNoContent_ButThrowsOnFailure()
    {
        // 204/no-content is the API's "no subscription" answer; a failed lookup must stay
        // distinguishable from it — subscribed users were shown "no plan" on transient errors.
        var (service, handler, _) = CreateService();
        handler.When("my-subscription", System.Net.HttpStatusCode.NoContent, "");
        Assert.Null(await service.GetMySubscriptionAsync("app-1"));

        // 404 = no subscription (pre-July-2026 backend behavior) — NOT an error.
        var (notFoundService, notFoundHandler, _) = CreateService();
        notFoundHandler.When("my-subscription", System.Net.HttpStatusCode.NotFound, """{"error":"not found"}""");
        Assert.Null(await notFoundService.GetMySubscriptionAsync("app-1"));

        var (failingService, failingHandler, _) = CreateService();
        failingHandler.When("my-subscription", System.Net.HttpStatusCode.InternalServerError, """{"error":"boom"}""");
        await Assert.ThrowsAsync<HttpRequestException>(() => failingService.GetMySubscriptionAsync("app-1"));
    }

    [Fact]
    public async Task GetUserFeaturesAsync_Throws_OnFailure()
    {
        // Failures must not masquerade as an empty (= no access) map — feature gates would
        // lock entitled users out during transient errors.
        var (service, handler, _) = CreateService();
        handler.When("user-features", System.Net.HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetUserFeaturesAsync("app-1"));
    }

    [Fact]
    public async Task HasFeatureAsync_IsCaseInsensitive_AndCachesPerRequest()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("user-features", """{"ai_chat":true,"PAYMENTS":false}""");

        Assert.True(await service.HasFeatureAsync("app-1", "AI_CHAT"));
        Assert.False(await service.HasFeatureAsync("app-1", "payments"));
        Assert.False(await service.HasFeatureAsync("app-1", "MISSING_FEATURE"));

        // The scoped service fetches the map once per appId per request.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task HasFeatureAsync_FailsOpen_WhenEntitlementsUnavailable()
    {
        // Gating markup is UX only — the server enforces the real entitlement, so an
        // unavailable map must not hide paid features.
        var (service, handler, _) = CreateService();
        handler.When("user-features", System.Net.HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        Assert.True(await service.HasFeatureAsync("app-1", "AI_CHAT"));
    }
}
