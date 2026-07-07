using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

public class AppTierComponentServiceTests
{
    private static (AppTierComponentService Service, FakeHttpMessageHandler Handler) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var service = new AppTierComponentService(
            handler.CreateClient("https://api.test/"),
            NullLogger<AppTierComponentService>.Instance);
        return (service, handler);
    }

    [Fact]
    public async Task GetTierAsync_CallsTierByIdEndpoint_AndParsesTier()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("app-tiers/tier/tier-1", """{"id":"tier-1","name":"Pro","description":"Pro tier"}""");

        var tier = await service.GetTierAsync("tier-1");

        Assert.NotNull(tier);
        Assert.Equal("tier-1", tier.Id);
        Assert.Equal("Pro", tier.Name);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("/api/app-tiers/tier/tier-1", request.Url);
    }

    [Fact]
    public async Task GetTierAsync_ReturnsNull_WhenNotFound()
    {
        var (service, handler) = CreateService();
        handler.When("app-tiers/tier/", System.Net.HttpStatusCode.NotFound, """{"error":"not found"}""");

        var tier = await service.GetTierAsync("missing");

        Assert.Null(tier);
    }

    [Fact]
    public async Task GetCompanyFeaturesAsync_UsesAppScopedAdminEndpoint()
    {
        // Regression: companies/{id}/features is platform-only and ignores appId
        var (service, handler) = CreateService();
        handler.WhenOk("admin/company-features", """{"FEATURE_A":true,"FEATURE_B":false}""");

        var features = await service.GetCompanyFeaturesAsync("app-1", "company-9");

        Assert.True(features["FEATURE_A"]);
        Assert.False(features["FEATURE_B"]);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("/api/app-tiers/app-1/admin/company-features/company-9", request.Url);
        Assert.DoesNotContain("companies/", request.Url);
    }

    [Fact]
    public async Task GetAvailableTiersAsync_CallsAppTiersList()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("app-tiers/app-1", """[{"id":"t1","name":"Free"},{"id":"t2","name":"Pro"}]""");

        var tiers = await service.GetAvailableTiersAsync("app-1");

        Assert.Equal(2, tiers.Count);
        Assert.Equal("Free", tiers[0].Name);
        Assert.Contains("/api/app-tiers/app-1", handler.Requests[0].Url);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_SurfacesScheduledCancelPayload()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("my-subscription/cancel",
            """{"success":true,"isScheduled":true,"effectiveDate":"2026-08-01T00:00:00Z"}""");

        var result = await service.CancelSubscriptionAsync("app-1");

        Assert.True(result.Success);
        Assert.True(result.IsScheduled);
        Assert.Equal(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), result.EffectiveDate!.Value.ToUniversalTime());
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/api/app-tiers/app-1/my-subscription/cancel", request.Url);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_ReportsFailure_WithoutThrowing()
    {
        var (service, handler) = CreateService();
        handler.When("my-subscription/cancel", System.Net.HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        var result = await service.CancelSubscriptionAsync("app-1");

        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
    }

    [Fact]
    public async Task CancelUserSubscriptionAsync_SurfacesStoreBillingFollowUp()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("cancel/user-7",
            """{"requiresUserAction":true,"userActionUrl":"https://apps.apple.com/account/subscriptions","userActionInstructions":"Also cancel in your App Store settings."}""");

        var result = await service.CancelUserSubscriptionAsync("app-1", "user-7");

        Assert.True(result.Success);
        Assert.True(result.RequiresUserAction);
        Assert.Equal("https://apps.apple.com/account/subscriptions", result.UserActionUrl);
        Assert.Contains("/api/app-tiers/app-1/cancel/user-7", handler.Requests[0].Url);
    }

    [Fact]
    public async Task GetMySubscriptionAsync_ReturnsNull_OnNoContent()
    {
        // 204/no-content is the API's "no subscription" answer — NOT an error.
        var (service, handler) = CreateService();
        handler.When("my-subscription", System.Net.HttpStatusCode.NoContent, "");

        var subscription = await service.GetMySubscriptionAsync("app-1");

        Assert.Null(subscription);
    }

    [Fact]
    public async Task GetMySubscriptionAsync_ReturnsNull_OnNotFound()
    {
        // 404 = no subscription (pre-July-2026 backend behavior) — NOT an error.
        var (service, handler) = CreateService();
        handler.When("my-subscription", System.Net.HttpStatusCode.NotFound, """{"error":"not found"}""");

        var subscription = await service.GetMySubscriptionAsync("app-1");

        Assert.Null(subscription);
    }

    [Fact]
    public async Task GetMySubscriptionAsync_Throws_OnLookupFailure()
    {
        // A failed lookup must be distinguishable from "no subscription" — subscribed users
        // were shown "no plan" on transient errors when both resolved to null.
        var (service, handler) = CreateService();
        handler.When("my-subscription", System.Net.HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetMySubscriptionAsync("app-1"));
    }

    [Fact]
    public async Task GetUserFeaturesAsync_ReturnsMap_ButThrowsOnFailure()
    {
        // Failures must not masquerade as an empty (= no access) map — feature gates would
        // lock entitled users out during transient errors.
        var (service, handler) = CreateService();
        handler.WhenOk("user-features", """{"CHAT":true,"PAYMENTS":false}""");

        var features = await service.GetUserFeaturesAsync("app-1");
        Assert.True(features["CHAT"]);
        Assert.False(features["PAYMENTS"]);

        var (failingService, failingHandler) = CreateService();
        failingHandler.When("user-features", System.Net.HttpStatusCode.InternalServerError, """{"error":"boom"}""");
        await Assert.ThrowsAsync<HttpRequestException>(() => failingService.GetUserFeaturesAsync("app-1"));
    }
}
