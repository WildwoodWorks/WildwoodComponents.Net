using System.Text.Json;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The catalog, add-on subscription and tier-change models must read the camelCase JSON
/// WildwoodAPI sends, field-for-field with the JS core types.
/// </summary>
public class AppTierModelsTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AppTierModel_ReadsTheCurrencyThePublicCatalogSends()
    {
        const string json = """
            {"id":"t1","appId":"app-1","name":"Pro","description":"","displayOrder":2,
             "status":"Active","currency":"EUR",
             "pricingOptions":[{"id":"p1","appTierId":"t1","price":79,"billingFrequency":"Monthly","trialDays":14}]}
            """;

        var tier = JsonSerializer.Deserialize<AppTierModel>(json, Web);

        Assert.NotNull(tier);
        Assert.Equal("EUR", tier!.Currency);
        Assert.Equal(14, tier.PricingOptions[0].TrialDays);
        Assert.True(tier.PricingOptions[0].HasTrial);
    }

    [Fact]
    public void AppTierModel_CurrencyIsNullWhenAnOlderServerOmitsIt()
    {
        var tier = JsonSerializer.Deserialize<AppTierModel>("""{"id":"t1","name":"Free"}""", Web);

        Assert.NotNull(tier);
        Assert.Null(tier!.Currency);
    }

    [Fact]
    public void AppTierAddOnModel_ReadsCurrencyAndPerPricingTrialDays()
    {
        const string json = """
            {"id":"a1","appId":"app-1","name":"Extra Seats","status":"Active","trialDays":7,"currency":"USD",
             "pricingOptions":[{"id":"ap1","pricingModelId":"pm1","pricingModelName":"Monthly",
               "price":9.5,"billingFrequency":"Monthly","trialDays":30,"isDefault":true}]}
            """;

        var addOn = JsonSerializer.Deserialize<AppTierAddOnModel>(json, Web);

        Assert.NotNull(addOn);
        Assert.Equal("USD", addOn!.Currency);
        Assert.Equal(7, addOn.TrialDays);
        Assert.Equal(30, addOn.PricingOptions[0].TrialDays);
        Assert.Equal(9.5m, addOn.PricingOptions[0].Price);

        var written = JsonSerializer.Serialize(addOn, Web);
        Assert.Contains("\"currency\":\"USD\"", written);
        Assert.Contains("\"trialDays\":30", written);
    }

    [Fact]
    public void AppTierAddOnPricingModel_TrialDaysIsNullWhenAbsent()
    {
        var pricing = JsonSerializer.Deserialize<AppTierAddOnPricingModel>("""{"id":"ap1","price":5}""", Web);

        Assert.NotNull(pricing);
        Assert.Null(pricing!.TrialDays);
    }

    [Fact]
    public void UserAddOnSubscriptionModel_ReadsThePaymentLinkFields()
    {
        const string json = """
            {"id":"s1","userId":"u1","appId":"app-1","companyId":"c1","appTierAddOnId":"a1",
             "appTierAddOnPricingId":"ap1","status":"Active","paymentTransactionId":"pt1",
             "userPaymentProviderId":"upp1","addOnName":"Extra Seats","addOnDescription":"",
             "isBundled":false,"startDate":"2026-09-01T00:00:00Z"}
            """;

        var sub = JsonSerializer.Deserialize<UserAddOnSubscriptionModel>(json, Web);

        Assert.NotNull(sub);
        Assert.Equal("u1", sub!.UserId);
        Assert.Equal("app-1", sub.AppId);
        Assert.Equal("ap1", sub.AppTierAddOnPricingId);
        Assert.Equal("pt1", sub.PaymentTransactionId);
        Assert.Equal("upp1", sub.UserPaymentProviderId);
    }

    [Fact]
    public void UserAddOnSubscriptionModel_ComplimentaryRowHasNoPaymentTransaction()
    {
        // A row granted by a registration token or an admin carries no payment transaction:
        // that absence is the "granted, not sold" signal the manage view depends on.
        const string json = """
            {"id":"s2","userId":"u1","appId":"app-1","appTierAddOnId":"a1","status":"Active",
             "addOnName":"Granted Pack","isBundled":false,"startDate":"2026-09-01T00:00:00Z"}
            """;

        var sub = JsonSerializer.Deserialize<UserAddOnSubscriptionModel>(json, Web);

        Assert.NotNull(sub);
        Assert.Null(sub!.PaymentTransactionId);
        Assert.Null(sub.UserPaymentProviderId);
        Assert.Null(sub.AppTierAddOnPricingId);
    }

    [Fact]
    public void AppTierChangeResultModel_ReadsTheThreeDSecureFields()
    {
        const string json = """
            {"success":false,"errorMessage":"","isScheduled":false,"requiresAction":true,
             "clientSecret":"pi_123_secret_456","pendingChangeId":"pc1","paymentIntentId":"pi_123",
             "expiresAt":"2026-09-18T12:30:00Z","amountDue":41.25,"currency":"USD","processing":false,
             "errorCode":"pending_change_expired"}
            """;

        var result = JsonSerializer.Deserialize<AppTierChangeResultModel>(json, Web);

        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.True(result.RequiresAction);
        Assert.Equal("pi_123_secret_456", result.ClientSecret);
        Assert.Equal("pc1", result.PendingChangeId);
        Assert.Equal("pi_123", result.PaymentIntentId);
        Assert.Equal(new DateTime(2026, 9, 18, 12, 30, 0, DateTimeKind.Utc), result.ExpiresAt!.Value.ToUniversalTime());
        Assert.Equal(41.25m, result.AmountDue);
        Assert.Equal("USD", result.Currency);
        Assert.False(result.Processing);
        Assert.Equal(TierChangeErrorCodes.PendingChangeExpired, result.ErrorCode);
    }

    [Fact]
    public void AppTierChangeResultModel_ThreeDSecureFieldsAreNullOnAnOlderServer()
    {
        const string json = """{"success":true,"errorMessage":"","isScheduled":false}""";

        var result = JsonSerializer.Deserialize<AppTierChangeResultModel>(json, Web);

        Assert.NotNull(result);
        Assert.Null(result!.RequiresAction);
        Assert.Null(result.Processing);
        Assert.Null(result.PendingChangeId);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void SelfChangeTierOptions_SerialisesTheKeysTheServerBinds()
    {
        var options = new SelfChangeTierOptions
        {
            NewTierId = "t2",
            NewPricingId = "p2",
            PaymentTransactionId = "pt1",
            SupportsPaymentAction = true
        };

        var json = JsonSerializer.Serialize(options, Web);

        Assert.Contains("\"NewAppTierId\":\"t2\"", json);
        Assert.Contains("\"NewAppTierPricingId\":\"p2\"", json);
        Assert.Contains("\"Immediate\":true", json);
        Assert.Contains("\"PaymentTransactionId\":\"pt1\"", json);
        Assert.Contains("\"SupportsPaymentAction\":true", json);
    }

    [Fact]
    public void SelfChangeTierOptions_DefaultsToImmediateAndNoPaymentAction()
    {
        var options = new SelfChangeTierOptions();

        Assert.True(options.Immediate);
        Assert.False(options.SupportsPaymentAction);
    }

    [Fact]
    public void TierChangeErrorCodes_CarryTheServerWireValues()
    {
        Assert.Equal("pending_change_not_found", TierChangeErrorCodes.PendingChangeNotFound);
        Assert.Equal("pending_change_expired", TierChangeErrorCodes.PendingChangeExpired);
        Assert.Equal("pending_change_payment_failed", TierChangeErrorCodes.PendingChangePaymentFailed);
        Assert.Equal("pending_change_superseded", TierChangeErrorCodes.PendingChangeSuperseded);
        Assert.Equal("tier_change_already_in_progress", TierChangeErrorCodes.TierChangeAlreadyInProgress);
    }

    [Fact]
    public void EntitlementsChangedReasons_CarryTheSixJsReasons()
    {
        Assert.Equal("signup", EntitlementsChangedReasons.Signup);
        Assert.Equal("tierChange", EntitlementsChangedReasons.TierChange);
        Assert.Equal("addOn", EntitlementsChangedReasons.AddOn);
        Assert.Equal("cancel", EntitlementsChangedReasons.Cancel);
        Assert.Equal("reactivate", EntitlementsChangedReasons.Reactivate);
        Assert.Equal("manual", EntitlementsChangedReasons.Manual);
    }
}
