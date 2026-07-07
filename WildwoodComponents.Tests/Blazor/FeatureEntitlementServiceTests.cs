using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

public class FeatureEntitlementServiceTests
{
    private static (FeatureEntitlementService Service, FakeHttpMessageHandler Handler) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var appTierService = new AppTierComponentService(
            handler.CreateClient("https://api.test/"),
            NullLogger<AppTierComponentService>.Instance);
        var service = new FeatureEntitlementService(
            appTierService,
            NullLogger<FeatureEntitlementService>.Instance);
        return (service, handler);
    }

    [Fact]
    public async Task HasFeatureAsync_LooksUpCaseInsensitively()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("user-features", """{"ai_chat":true,"PAYMENTS":false}""");

        Assert.True(await service.HasFeatureAsync("AI_CHAT", "app-1"));
        Assert.False(await service.HasFeatureAsync("payments", "app-1"));
        Assert.False(await service.HasFeatureAsync("MISSING_FEATURE", "app-1"));
    }

    [Fact]
    public async Task HasFeatureAsync_SharesOneBulkFetch_AcrossGates()
    {
        // N gates = 1 request: the whole point of the shared cache (mirrors useFeatures).
        var (service, handler) = CreateService();
        handler.WhenOk("user-features", """{"CHAT":true}""");

        await service.HasFeatureAsync("CHAT", "app-1");
        await service.HasFeatureAsync("CHAT", "app-1");
        await service.HasFeatureAsync("OTHER", "app-1");

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task HasFeatureAsync_FailsOpen_WhenFetchFails_AndDoesNotCacheTheFailure()
    {
        // Client-side gating is UX; the server enforces the real entitlement — a transient
        // failure must not lock gates, and must not be pinned for the cache TTL.
        var (service, handler) = CreateService();
        handler.When("user-features", System.Net.HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        Assert.True(await service.HasFeatureAsync("AI_CHAT", "app-1"));

        // Failure was not cached — the next check fetches again.
        await service.HasFeatureAsync("AI_CHAT", "app-1");
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task HasFeatureAsync_FailsOpen_WithoutAppId()
    {
        var (service, handler) = CreateService();

        Assert.True(await service.HasFeatureAsync("AI_CHAT"));
        Assert.Empty(handler.Requests); // nothing to query — entitlements stay unknown
    }

    [Fact]
    public async Task Invalidate_ClearsCache_AndNotifiesSubscribers()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("user-features", """{"CHAT":true}""");

        await service.HasFeatureAsync("CHAT", "app-1");

        var notified = false;
        service.EntitlementsChanged += () => notified = true;
        service.Invalidate();

        Assert.True(notified);
        await service.HasFeatureAsync("CHAT", "app-1");
        Assert.Equal(2, handler.Requests.Count); // cache was dropped, so a refetch happened
    }
}
