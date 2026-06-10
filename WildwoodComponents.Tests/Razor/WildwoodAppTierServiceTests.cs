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
}
