using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

public class PaymentProviderServiceTests
{
    /// <summary>Fake web platform: flags=1 (Web), no app store requirement.</summary>
    private sealed class FakeWebPlatformService : IPlatformDetectionService
    {
        public RuntimePlatform CurrentPlatform => RuntimePlatform.Web;
        public PlatformInfo GetPlatformInfo() => new();
        public bool RequiresAppStorePayment => false;
        public int? RequiredAppStoreProviderType => null;
        public bool IsProviderAvailable(int providerType) => true;
        public int GetPlatformFlags() => 1;
        public Task<bool> IsApplePayAvailableAsync() => Task.FromResult(false);
        public Task<bool> IsGooglePayAvailableAsync() => Task.FromResult(false);
        public bool IsDistributedApp => false;
    }

    private static (PaymentProviderService Service, FakeHttpMessageHandler Handler) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var service = new PaymentProviderService(
            handler.CreateClient("https://api.test/"),
            new FakeWebPlatformService(),
            NullLogger<PaymentProviderService>.Instance);
        service.SetApiBaseUrl("https://api.test/api");
        return (service, handler);
    }

    [Fact]
    public async Task LinkTransactionToUserAsync_PostsToLinkByExternalId()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("link-by-external-id", """{"success":true}""");

        var ok = await service.LinkTransactionToUserAsync("pi_ext_123", "user-1", "client-7");

        Assert.True(ok);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/api/paymenttransactions/link-by-external-id", request.Url);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("pi_ext_123", body.RootElement.GetProperty("externalTransactionId").GetString());
        Assert.Equal("user-1", body.RootElement.GetProperty("userId").GetString());
        Assert.Equal("client-7", body.RootElement.GetProperty("companyClientId").GetString());
    }

    [Fact]
    public async Task GetAvailableProvidersAsync_FiltersDisabledAndWrongPlatform_AndSortsByDisplayOrder()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("payment/configuration", """
        {
            "appId": "app-1",
            "isPaymentEnabled": true,
            "providers": [
                { "id": "p-disabled", "name": "Disabled", "providerType": 1, "isEnabled": false, "allowedPlatforms": 1, "displayOrder": 0 },
                { "id": "p-mobile",   "name": "MobileOnly", "providerType": 1, "isEnabled": true,  "allowedPlatforms": 6, "displayOrder": 1 },
                { "id": "p-paypal",   "name": "PayPal", "providerType": 2, "isEnabled": true,  "allowedPlatforms": 1, "displayOrder": 2 },
                { "id": "p-stripe",   "name": "Stripe", "providerType": 1, "isEnabled": true,  "allowedPlatforms": 1, "displayOrder": 1, "isDefault": true }
            ]
        }
        """);

        var result = await service.GetAvailableProvidersAsync("app-1");

        // Disabled and non-web providers filtered out; remaining sorted by display order
        Assert.Equal(2, result.AvailableProviders.Count);
        Assert.Equal("p-stripe", result.AvailableProviders[0].Id);
        Assert.Equal("p-paypal", result.AvailableProviders[1].Id);
        Assert.Equal("p-stripe", result.DefaultProvider?.Id);
        Assert.False(result.RequiresAppStorePayment);
    }

    [Fact]
    public async Task GetAvailableProvidersAsync_ReturnsEmpty_WhenConfigUnavailable()
    {
        var (service, handler) = CreateService();
        handler.When("payment/configuration", System.Net.HttpStatusCode.NotFound, """{"error":"no config"}""");

        var result = await service.GetAvailableProvidersAsync("app-1");

        Assert.Empty(result.AvailableProviders);
        Assert.Equal("app-1", result.AppId);
    }
}
