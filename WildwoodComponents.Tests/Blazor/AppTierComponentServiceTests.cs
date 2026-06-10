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
}
