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

    [Fact]
    public async Task ValidateStorePurchaseAsync_PostsTheStoreContractToTheAppleEndpoint()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("validate-apple-receipt", """{"success":true,"transactionId":"txn-1"}""");

        var result = await service.ValidateStorePurchaseAsync("app-1", new StorePurchase
        {
            ProviderType = PaymentProviderType.AppleAppStore,
            ProductId = "com.app.pro.monthly",
            PurchaseToken = "jws-token",
            TransactionId = "2000000123",
            IsRestore = true
        });

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/payment/validate-apple-receipt", request.Url);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("app-1", body.RootElement.GetProperty("appId").GetString());
        Assert.Equal(10, body.RootElement.GetProperty("providerType").GetInt32());
        Assert.Equal("com.app.pro.monthly", body.RootElement.GetProperty("productId").GetString());
        Assert.Equal("jws-token", body.RootElement.GetProperty("purchaseToken").GetString());
        // The API binds the proof of purchase to receiptData, so it is sent alongside purchaseToken.
        Assert.Equal("jws-token", body.RootElement.GetProperty("receiptData").GetString());
        Assert.Equal("2000000123", body.RootElement.GetProperty("transactionId").GetString());
        Assert.True(body.RootElement.GetProperty("isRestore").GetBoolean());

        Assert.True(result.Success);
        Assert.Equal("txn-1", result.TransactionId);
    }

    [Fact]
    public async Task ValidateStorePurchaseAsync_RoutesGooglePlayToTheGoogleEndpoint()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("validate-google-receipt", """{"success":true,"transactionId":"txn-2"}""");

        var result = await service.ValidateStorePurchaseAsync("app-1", new StorePurchase
        {
            ProviderType = PaymentProviderType.GooglePlayStore,
            ProductId = "pro_monthly",
            PurchaseToken = "play-token"
        });

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/payment/validate-google-receipt", request.Url);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(11, body.RootElement.GetProperty("providerType").GetInt32());
        Assert.Equal("play-token", body.RootElement.GetProperty("purchaseToken").GetString());
        Assert.Equal("play-token", body.RootElement.GetProperty("receiptData").GetString());

        Assert.True(result.Success);
        Assert.Equal("txn-2", result.TransactionId);
    }

    [Fact]
    public async Task ValidateAppStoreReceiptAsync_StillValidatesThroughTheNewPath()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("validate-apple-receipt", """{"success":true,"transactionId":"txn-3"}""");

#pragma warning disable CS0618 // Deliberately exercising the obsolete overload's delegation.
        var result = await service.ValidateAppStoreReceiptAsync(
            "app-1", "legacy-receipt", PaymentProviderType.AppleAppStore);
#pragma warning restore CS0618

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/payment/validate-apple-receipt", request.Url);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("legacy-receipt", body.RootElement.GetProperty("purchaseToken").GetString());
        Assert.Equal("legacy-receipt", body.RootElement.GetProperty("receiptData").GetString());
        // The old signature carries no product id, so the contract's field goes out empty.
        Assert.Equal(string.Empty, body.RootElement.GetProperty("productId").GetString());

        Assert.True(result.Success);
        Assert.Equal("txn-3", result.TransactionId);
    }
}
