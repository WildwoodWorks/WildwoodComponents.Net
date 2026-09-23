using System.Text.Json;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The pack-checkout models must read the server's camelCase answers and write request bodies
/// whose keys are the PascalCase ones WildwoodAPI binds — the same bodies the JS SDK posts.
/// </summary>
public class AppTierCheckoutModelsTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TrialEligibilityModel_ReadsTheEligibilityAnswer()
    {
        const string json = """{"tierTrialEligible":false,"addOns":{"a1":true,"a2":false}}""";

        var model = JsonSerializer.Deserialize<TrialEligibilityModel>(json, Web);

        Assert.NotNull(model);
        Assert.False(model!.TierTrialEligible);
        Assert.True(model.AddOns["a1"]);
        Assert.False(model.AddOns["a2"]);
        Assert.False(model.AddOns.ContainsKey("a3"));
    }

    [Fact]
    public void TrialEligibilityModel_DefaultsToEligibleWithAnEmptyMap()
    {
        var model = new TrialEligibilityModel();

        Assert.True(model.TierTrialEligible);
        Assert.Empty(model.AddOns);
    }

    [Fact]
    public void QuoteRequest_SerialisesItemsWithPascalCaseKeys()
    {
        var request = new AddOnCheckoutQuoteRequestModel
        {
            Items = new List<AddOnCheckoutItemInput>
            {
                new AddOnCheckoutItemInput { AddOnId = "a1", PricingId = "ap1" },
                new AddOnCheckoutItemInput { AddOnId = "a2" }
            }
        };

        var json = JsonSerializer.Serialize(request, Web);

        Assert.Contains("\"Items\":[", json);
        Assert.Contains("\"AddOnId\":\"a1\"", json);
        Assert.Contains("\"PricingId\":\"ap1\"", json);
        Assert.Contains("\"AddOnId\":\"a2\"", json);
    }

    [Fact]
    public void PaymentMethodRequest_SerialisesProviderId()
    {
        var json = JsonSerializer.Serialize(
            new AddOnCheckoutPaymentMethodRequestModel { ProviderId = "prov-1" }, Web);

        Assert.Equal("{\"ProviderId\":\"prov-1\"}", json);
    }

    [Fact]
    public void CheckoutRequest_SerialisesCheckoutIdAndUseSavedCard()
    {
        var request = new AddOnCheckoutRequestModel
        {
            CheckoutId = "chk-1",
            ProviderId = "prov-1",
            UseSavedCard = true,
            Items = new List<AddOnCheckoutItemInput>
            {
                new AddOnCheckoutItemInput { AddOnId = "a1", PricingId = "ap1" }
            }
        };

        var json = JsonSerializer.Serialize(request, Web);

        Assert.Contains("\"CheckoutId\":\"chk-1\"", json);
        Assert.Contains("\"ProviderId\":\"prov-1\"", json);
        Assert.Contains("\"UseSavedCard\":true", json);
        Assert.Contains("\"Items\":[{\"AddOnId\":\"a1\",\"PricingId\":\"ap1\"}]", json);
    }

    [Fact]
    public void CheckoutRequest_CarriesThePaymentTransactionInsteadOfASavedCard()
    {
        var json = JsonSerializer.Serialize(
            new AddOnCheckoutRequestModel
            {
                CheckoutId = "chk-2",
                ProviderId = "prov-1",
                PaymentTransactionId = "pt-9"
            }, Web);

        Assert.Contains("\"PaymentTransactionId\":\"pt-9\"", json);
        Assert.Contains("\"UseSavedCard\":false", json);
    }

    [Fact]
    public void CheckoutCompleteRequest_SerialisesThePaymentTransactionId()
    {
        var json = JsonSerializer.Serialize(
            new AddOnCheckoutCompleteRequestModel { PaymentTransactionId = "pt-9" }, Web);

        Assert.Equal("{\"PaymentTransactionId\":\"pt-9\"}", json);
    }

    [Fact]
    public void QuoteModel_ReadsLinesSavedCardAndTotals()
    {
        const string json = """
            {"success":true,"checkoutId":"chk-1","providerId":"prov-1","currency":"USD",
             "lines":[{"addOnId":"a1","pricingId":"ap1","name":"Extra Seats","price":9.5,
               "billingFrequency":"Monthly","trialDays":14,"trialEligible":true,"dueToday":0,
               "trialEnd":"2026-10-02T00:00:00Z"}],
             "totalDueToday":0,"requiresPaymentMethod":false,
             "savedCard":{"brand":"visa","last4":"4242"}}
            """;

        var quote = JsonSerializer.Deserialize<AddOnCheckoutQuoteModel>(json, Web);

        Assert.NotNull(quote);
        Assert.True(quote!.Success);
        Assert.Equal("chk-1", quote.CheckoutId);
        Assert.Equal("prov-1", quote.ProviderId);
        Assert.Equal("USD", quote.Currency);
        Assert.Equal(0m, quote.TotalDueToday);
        Assert.False(quote.RequiresPaymentMethod);
        Assert.Equal("visa", quote.SavedCard?.Brand);
        Assert.Equal("4242", quote.SavedCard?.Last4);

        var line = Assert.Single(quote.Lines);
        Assert.Equal("a1", line.AddOnId);
        Assert.Equal("ap1", line.PricingId);
        Assert.Equal("Extra Seats", line.Name);
        Assert.Equal(9.5m, line.Price);
        Assert.Equal("Monthly", line.BillingFrequency);
        Assert.Equal(14, line.TrialDays);
        Assert.True(line.TrialEligible);
        Assert.Equal(0m, line.DueToday);
        Assert.Equal(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), line.TrialEnd!.Value.ToUniversalTime());
    }

    [Fact]
    public void QuoteModel_RefusalKeepsTheServerFieldsAndABlankCurrency()
    {
        const string json = """
            {"success":false,"checkoutId":"","currency":"","lines":[],"totalDueToday":0,
             "requiresPaymentMethod":false,"errorCode":"AlreadySubscribed",
             "errorMessage":"You already have that pack."}
            """;

        var quote = JsonSerializer.Deserialize<AddOnCheckoutQuoteModel>(json, Web);

        Assert.NotNull(quote);
        Assert.False(quote!.Success);
        Assert.Equal(string.Empty, quote.Currency);
        Assert.Empty(quote.Lines);
        Assert.Equal(AddOnCheckoutErrorCodes.AlreadySubscribed, quote.ErrorCode);
        Assert.Equal("You already have that pack.", quote.ErrorMessage);
        Assert.Null(quote.SavedCard);
    }

    [Fact]
    public void PaymentMethodModel_ReadsTheSetupIntentSecretAndTransaction()
    {
        const string json = """
            {"success":true,"clientSecret":"seti_1_secret_2","setupIntentId":"seti_1",
             "paymentTransactionId":"pt-3"}
            """;

        var model = JsonSerializer.Deserialize<AddOnCheckoutPaymentMethodModel>(json, Web);

        Assert.NotNull(model);
        Assert.True(model!.Success);
        Assert.Equal("seti_1_secret_2", model.ClientSecret);
        Assert.Equal("seti_1", model.SetupIntentId);
        Assert.Equal("pt-3", model.PaymentTransactionId);
        Assert.Null(model.ErrorCode);
    }

    [Fact]
    public void CheckoutResultModel_ReadsOneResultPerPack()
    {
        const string json = """
            {"success":true,"checkoutId":"chk-1","results":[
              {"addOnId":"a1","pricingId":"ap1","status":"trialing","subscriptionId":"s1",
               "trialEnd":"2026-10-02T00:00:00Z","amountDueToday":0},
              {"addOnId":"a2","pricingId":"ap2","status":"requires_action",
               "clientSecret":"pi_2_secret_3","paymentIntentId":"pi_2","paymentTransactionId":"pt-2"},
              {"addOnId":"a3","pricingId":"ap3","status":"failed","errorCode":"ProcessorError",
               "errorMessage":"Your card was declined."}]}
            """;

        var result = JsonSerializer.Deserialize<AddOnCheckoutResultModel>(json, Web);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("chk-1", result.CheckoutId);
        Assert.Equal(3, result.Results.Count);

        Assert.Equal(AddOnCheckoutItemStatuses.Trialing, result.Results[0].Status);
        Assert.Equal("s1", result.Results[0].SubscriptionId);
        Assert.Equal(0m, result.Results[0].AmountDueToday);
        Assert.Equal(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), result.Results[0].TrialEnd!.Value.ToUniversalTime());

        Assert.Equal(AddOnCheckoutItemStatuses.RequiresAction, result.Results[1].Status);
        Assert.Equal("pi_2_secret_3", result.Results[1].ClientSecret);
        Assert.Equal("pi_2", result.Results[1].PaymentIntentId);
        Assert.Equal("pt-2", result.Results[1].PaymentTransactionId);
        Assert.Null(result.Results[1].SubscriptionId);

        Assert.Equal(AddOnCheckoutItemStatuses.Failed, result.Results[2].Status);
        Assert.Equal(AddOnCheckoutErrorCodes.ProcessorError, result.Results[2].ErrorCode);
        Assert.Equal("Your card was declined.", result.Results[2].ErrorMessage);
    }

    [Fact]
    public void CheckoutResultModel_RefusalBeforeAnythingWasAttempted()
    {
        const string json = """
            {"success":false,"checkoutId":"chk-1","results":[],"errorCode":"PaymentMethodRequired",
             "errorMessage":"A card is required."}
            """;

        var result = JsonSerializer.Deserialize<AddOnCheckoutResultModel>(json, Web);

        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Empty(result.Results);
        Assert.Equal(AddOnCheckoutErrorCodes.PaymentMethodRequired, result.ErrorCode);
    }

    [Fact]
    public void ItemResultModel_IsWhatTheCompleteEndpointAnswers()
    {
        const string json = """
            {"addOnId":"a1","pricingId":"ap1","status":"active","subscriptionId":"s1",
             "paymentTransactionId":"pt-9","amountDueToday":9.5}
            """;

        var item = JsonSerializer.Deserialize<AddOnCheckoutItemResultModel>(json, Web);

        Assert.NotNull(item);
        Assert.Equal(AddOnCheckoutItemStatuses.Active, item!.Status);
        Assert.Equal("s1", item.SubscriptionId);
        Assert.Equal(9.5m, item.AmountDueToday);
    }

    [Fact]
    public void ActionError_RoundTripsCodeMessageAndStatus()
    {
        const string json = """{"code":"addon_subscription_forbidden","message":"Not yours.","status":403}""";

        var error = JsonSerializer.Deserialize<AppTierActionError>(json, Web);

        Assert.NotNull(error);
        Assert.Equal(AddOnSubscriptionErrorCodes.Forbidden, error!.Code);
        Assert.Equal("Not yours.", error.Message);
        Assert.Equal(403, error.Status);

        var written = JsonSerializer.Serialize(error, Web);
        Assert.Contains("\"code\":\"addon_subscription_forbidden\"", written);
        Assert.Contains("\"status\":403", written);
    }

    [Fact]
    public void SubscribeResult_CarriesEitherTheSubscriptionOrTheError()
    {
        var ok = new AddOnSubscribeResultModel
        {
            Success = true,
            Subscription = new UserAddOnSubscriptionModel { Id = "s1", PaymentTransactionId = "pt-1" }
        };
        var failed = new AddOnSubscribeResultModel
        {
            Success = false,
            Error = new AppTierActionError
            {
                Code = AppTierActionErrorCodes.NotSupported,
                Message = "Failed to subscribe to the pack",
                Status = 404
            }
        };

        Assert.Null(ok.Error);
        Assert.Equal("pt-1", ok.Subscription?.PaymentTransactionId);
        Assert.Null(failed.Subscription);
        Assert.Equal("NotSupported", failed.Error?.Code);

        var json = JsonSerializer.Serialize(ok, Web);
        Assert.Contains("\"success\":true", json);
        Assert.Contains("\"subscription\":{", json);
    }

    [Fact]
    public void CancelResult_ReadsTheScheduledAnswer()
    {
        const string json = """
            {"success":true,"isScheduled":true,"status":"PendingCancellation",
             "effectiveDate":"2026-10-01T00:00:00Z"}
            """;

        var result = JsonSerializer.Deserialize<AddOnSubscriptionCancelResultModel>(json, Web);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.True(result.IsScheduled);
        Assert.Equal("PendingCancellation", result.Status);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), result.EffectiveDate!.Value.ToUniversalTime());
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void CancelResult_ReadsARefusalKeepingTheServersReason()
    {
        const string json = """
            {"success":false,"errorCode":"addon_subscription_not_found","errorMessage":"No such pack."}
            """;

        var result = JsonSerializer.Deserialize<AddOnSubscriptionCancelResultModel>(json, Web);

        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Null(result.IsScheduled);
        Assert.Equal(AddOnSubscriptionErrorCodes.NotFound, result.ErrorCode);
        Assert.Equal("No such pack.", result.ErrorMessage);
    }

    [Fact]
    public void ReactivateResult_ReadsTheRefreshedSubscription()
    {
        const string json = """
            {"success":true,"status":"Active",
             "subscription":{"id":"s1","userId":"u1","appId":"app-1","appTierAddOnId":"a1",
               "status":"Active","paymentTransactionId":"pt-1","addOnName":"Extra Seats",
               "isBundled":false,"startDate":"2026-09-01T00:00:00Z"}}
            """;

        var result = JsonSerializer.Deserialize<AddOnSubscriptionReactivateResultModel>(json, Web);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("Active", result.Status);
        Assert.Equal("s1", result.Subscription?.Id);
        Assert.Equal("pt-1", result.Subscription?.PaymentTransactionId);
    }

    [Fact]
    public void CheckoutItemStatuses_CarryTheServerWireValues()
    {
        Assert.Equal("trialing", AddOnCheckoutItemStatuses.Trialing);
        Assert.Equal("active", AddOnCheckoutItemStatuses.Active);
        Assert.Equal("requires_action", AddOnCheckoutItemStatuses.RequiresAction);
        Assert.Equal("failed", AddOnCheckoutItemStatuses.Failed);
    }

    [Fact]
    public void CheckoutErrorCodes_CarryTheNineteenServerCodes()
    {
        var codes = new[]
        {
            AddOnCheckoutErrorCodes.NoItems, AddOnCheckoutErrorCodes.DuplicateItem,
            AddOnCheckoutErrorCodes.AppNotFound, AddOnCheckoutErrorCodes.AddOnNotFound,
            AddOnCheckoutErrorCodes.AddOnNotAvailable, AddOnCheckoutErrorCodes.PricingNotFound,
            AddOnCheckoutErrorCodes.AlreadySubscribed, AddOnCheckoutErrorCodes.BundledInTier,
            AddOnCheckoutErrorCodes.TierTooLow, AddOnCheckoutErrorCodes.DependencyMissing,
            AddOnCheckoutErrorCodes.ProviderNotAvailable, AddOnCheckoutErrorCodes.PaymentMethodRequired,
            AddOnCheckoutErrorCodes.TransactionNotFound, AddOnCheckoutErrorCodes.TransactionNotYours,
            AddOnCheckoutErrorCodes.TransactionProviderMismatch, AddOnCheckoutErrorCodes.PaymentNotVerified,
            AddOnCheckoutErrorCodes.CheckoutIdRequired, AddOnCheckoutErrorCodes.ProcessorError,
            AddOnCheckoutErrorCodes.SubscriptionNotCreated
        };

        Assert.Equal(19, codes.Length);
        Assert.Equal("NoItems", codes[0]);
        Assert.Equal("SubscriptionNotCreated", codes[18]);
    }

    [Fact]
    public void SubscriptionErrorCodes_CarryTheSixServerCodes()
    {
        Assert.Equal("addon_subscription_not_found", AddOnSubscriptionErrorCodes.NotFound);
        Assert.Equal("addon_subscription_forbidden", AddOnSubscriptionErrorCodes.Forbidden);
        Assert.Equal("addon_subscription_provider_refused", AddOnSubscriptionErrorCodes.ProviderRefused);
        Assert.Equal("addon_subscription_not_pending_cancellation", AddOnSubscriptionErrorCodes.NotPendingCancellation);
        Assert.Equal("addon_subscription_not_provider_billed", AddOnSubscriptionErrorCodes.NotProviderBilled);
        Assert.Equal("addon_subscription_error", AddOnSubscriptionErrorCodes.Error);
    }

    [Fact]
    public void ActionErrorCodes_AreTheTwoTheSdkSuppliesItself()
    {
        Assert.Equal("NotSupported", AppTierActionErrorCodes.NotSupported);
        Assert.Equal("RequestFailed", AppTierActionErrorCodes.RequestFailed);
    }
}
