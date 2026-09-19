using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// The Razor twin of AppTierComponentServiceCheckoutTests: same paths, verbs, JSON body keys and
/// error-mapper branches, plus the two things only the Razor stack can get wrong — the bearer must
/// ride on the REQUEST (never on the shared client's default headers), and <c>?immediate=</c> must
/// be the lowercase literal the JS SDK sends.
/// </summary>
public class WildwoodAppTierServiceCheckoutTests
{
    private static (WildwoodAppTierService Service, FakeHttpMessageHandler Handler, FakeSessionManager Session) CreateService(
        string? accessToken = "test-jwt")
    {
        var handler = new FakeHttpMessageHandler();
        var session = new FakeSessionManager(accessToken);
        var service = new WildwoodAppTierService(
            handler.CreateClient("https://api.test/api/"),
            session,
            NullLogger<WildwoodAppTierService>.Instance);
        return (service, handler, session);
    }

    /// <summary>The transport never reaching the server — the .NET twin of a rejected fetch.</summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("fetch failed");
    }

    private static WildwoodAppTierService CreateOfflineService()
        => new(new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://api.test/api/") },
            new FakeSessionManager(),
            NullLogger<WildwoodAppTierService>.Instance);

    private static JsonElement Body(FakeHttpMessageHandler.RecordedRequest request)
        => JsonDocument.Parse(request.Body ?? "{}").RootElement;

    // ── The Razor-specific hazard ───────────────────────────────────────────────

    [Fact]
    public async Task TheBearerRidesOnTheRequest_NotOnTheSharedClientsDefaultHeaders()
    {
        // One named "WildwoodAPI" client is handed to every service in the scope, so a token set
        // on DefaultRequestHeaders would outlive the call and reach another user's request.
        var (service, handler, session) = CreateService();
        handler.WhenOk("trial-eligibility", """{"tierTrialEligible":true}""");

        await service.GetTrialEligibilityAsync("app-1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer test-jwt", request.Authorization);
        Assert.Equal(0, session.ApplyAuthorizationHeaderCalls);
    }

    [Fact]
    public async Task NoSessionToken_SendsNoAuthorizationHeaderAtAll()
    {
        var (service, handler, _) = CreateService(accessToken: null);
        handler.WhenOk("trial-eligibility", """{"tierTrialEligible":true}""");

        await service.GetTrialEligibilityAsync("app-1");

        Assert.Null(Assert.Single(handler.Requests).Authorization);
    }

    // ── Trial eligibility ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetTrialEligibilityAsync_ReadsTheEligibilityEndpoint()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("app-tiers/app-1/trial-eligibility",
            """{"tierTrialEligible":false,"addOns":{"radar":true}}""");

        var eligibility = await service.GetTrialEligibilityAsync("app-1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("/api/app-tiers/app-1/trial-eligibility", request.Url);
        Assert.False(eligibility.TierTrialEligible);
        Assert.True(eligibility.AddOns["radar"]);
    }

    [Fact]
    public async Task GetTrialEligibilityAsync_AnswersEligiblePacksUnknown_WhenTheLookupFails()
    {
        var (service, handler, _) = CreateService();
        handler.When("trial-eligibility", HttpStatusCode.NotFound, """{"error":"nope"}""");

        var eligibility = await service.GetTrialEligibilityAsync("app-1");

        Assert.True(eligibility.TierTrialEligible);
        Assert.Empty(eligibility.AddOns);
    }

    // ── Pack checkout ───────────────────────────────────────────────────────────

    [Fact]
    public async Task QuoteAddOnCheckoutAsync_PostsAPascalCaseItemList_AndMapsTheAnswer()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("checkout/quote",
            """
            {"success":true,"checkoutId":"co-1","providerId":"prov-1","currency":"USD",
             "lines":[{"addOnId":"radar","pricingId":"ap-1","name":"Radar","price":19,
                       "billingFrequency":"Monthly","trialDays":14,"trialEligible":true,
                       "dueToday":0,"trialEnd":"2026-10-01T00:00:00Z"}],
             "totalDueToday":0,"requiresPaymentMethod":false,
             "savedCard":{"brand":"visa","last4":"4242"}}
            """);

        var quote = await service.QuoteAddOnCheckoutAsync("app-1", new List<AddOnCheckoutItemInput>
        {
            new() { AddOnId = "radar" },
            new() { AddOnId = "vault", PricingId = "ap-9" }
        });

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/api/app-tier-addons/app-1/checkout/quote", request.Url);
        var items = Body(request).GetProperty("Items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("radar", items[0].GetProperty("AddOnId").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("PricingId").ValueKind);
        Assert.Equal("ap-9", items[1].GetProperty("PricingId").GetString());

        Assert.True(quote.Success);
        Assert.Equal("co-1", quote.CheckoutId);
        Assert.Equal(0m, quote.Lines[0].DueToday);
        Assert.Equal("4242", quote.SavedCard!.Last4);
    }

    [Fact]
    public async Task QuoteAddOnCheckoutAsync_KeepsTheRefusedQuoteTheServerSent_ErrorCodeAndAll()
    {
        var (service, handler, _) = CreateService();
        handler.When("checkout/quote", HttpStatusCode.BadRequest,
            """
            {"success":false,"checkoutId":"co-2","currency":"USD","lines":[],"totalDueToday":0,
             "requiresPaymentMethod":false,"errorCode":"AlreadySubscribed",
             "errorMessage":"You already have that pack"}
            """);

        var quote = await service.QuoteAddOnCheckoutAsync("app-1", new List<AddOnCheckoutItemInput> { new() { AddOnId = "radar" } });

        Assert.False(quote.Success);
        Assert.Equal("co-2", quote.CheckoutId);
        Assert.Equal(AddOnCheckoutErrorCodes.AlreadySubscribed, quote.ErrorCode);
        Assert.Equal("You already have that pack", quote.ErrorMessage);
    }

    [Fact]
    public async Task QuoteAddOnCheckoutAsync_ReportsAServerWithoutCheckoutRoutes_AsNotSupported()
    {
        var (service, handler, _) = CreateService();
        handler.When("checkout/quote", HttpStatusCode.NotFound, "");

        var quote = await service.QuoteAddOnCheckoutAsync("app-1", null);

        Assert.False(quote.Success);
        Assert.Equal(AppTierActionErrorCodes.NotSupported, quote.ErrorCode);
        // Nothing was priced, so nothing names a currency.
        Assert.Equal(string.Empty, quote.Currency);
        Assert.Empty(quote.Lines);
        Assert.False(string.IsNullOrEmpty(quote.ErrorMessage));
        // A null basket is an empty one, not a missing property.
        Assert.Equal(0, Body(Assert.Single(handler.Requests)).GetProperty("Items").GetArrayLength());
    }

    [Fact]
    public async Task CreateCheckoutPaymentMethodAsync_StartsCardCollectionForOneProvider()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("checkout/payment-method",
            """{"success":true,"clientSecret":"seti_1_secret","setupIntentId":"seti_1","paymentTransactionId":"txn-1"}""");

        var result = await service.CreateCheckoutPaymentMethodAsync("app-1", "prov-1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/api/app-tier-addons/app-1/checkout/payment-method", request.Url);
        Assert.Equal("prov-1", Body(request).GetProperty("ProviderId").GetString());
        Assert.Equal("seti_1_secret", result.ClientSecret);
        Assert.Equal("txn-1", result.PaymentTransactionId);
    }

    [Fact]
    public async Task CheckoutAddOnsAsync_BuysTheBasket_AndReturnsAResultPerPack()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("app-1/checkout",
            """
            {"success":true,"checkoutId":"co-1","results":[
              {"addOnId":"radar","pricingId":"ap-1","status":"trialing","subscriptionId":"sub-1"},
              {"addOnId":"vault","pricingId":"ap-2","status":"requires_action",
               "clientSecret":"pi_1_secret","paymentTransactionId":"txn-2"}]}
            """);

        var result = await service.CheckoutAddOnsAsync("app-1", new AddOnCheckoutRequestModel
        {
            CheckoutId = "co-1",
            ProviderId = "prov-1",
            UseSavedCard = true,
            Items = new List<AddOnCheckoutItemInput> { new() { AddOnId = "radar" }, new() { AddOnId = "vault", PricingId = "ap-2" } }
        });

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/api/app-tier-addons/app-1/checkout", request.Url);
        var body = Body(request);
        Assert.Equal("co-1", body.GetProperty("CheckoutId").GetString());
        Assert.Equal("prov-1", body.GetProperty("ProviderId").GetString());
        Assert.True(body.GetProperty("UseSavedCard").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("PaymentTransactionId").ValueKind);
        Assert.Equal(2, body.GetProperty("Items").GetArrayLength());

        Assert.Equal(AddOnCheckoutItemStatuses.Trialing, result.Results[0].Status);
        Assert.Equal(AddOnCheckoutItemStatuses.RequiresAction, result.Results[1].Status);
        Assert.Equal("pi_1_secret", result.Results[1].ClientSecret);
    }

    [Fact]
    public async Task CheckoutAddOnsAsync_KeepsTheCheckoutIdOnARefusal()
    {
        var (service, handler, _) = CreateService();
        handler.When("app-1/checkout", HttpStatusCode.BadRequest, """{"error":"no card"}""");

        var result = await service.CheckoutAddOnsAsync("app-1", new AddOnCheckoutRequestModel
        {
            CheckoutId = "co-3",
            ProviderId = "prov-1",
            PaymentTransactionId = "txn-1",
            Items = new List<AddOnCheckoutItemInput> { new() { AddOnId = "radar" } }
        });

        var body = Body(Assert.Single(handler.Requests));
        Assert.False(body.GetProperty("UseSavedCard").GetBoolean());
        Assert.Equal("txn-1", body.GetProperty("PaymentTransactionId").GetString());

        Assert.False(result.Success);
        Assert.Equal("co-3", result.CheckoutId);
        Assert.Empty(result.Results);
        Assert.Equal(AppTierActionErrorCodes.RequestFailed, result.ErrorCode);
        Assert.Equal("no card", result.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAddOnCheckoutAsync_CompletesOnePack_AndKeepsThePackARefusalWasAbout()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("checkout/complete",
            """{"addOnId":"radar","pricingId":"ap-1","status":"active","subscriptionId":"sub-9"}""");

        var result = await service.CompleteAddOnCheckoutAsync("app-1", "txn-2");

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/api/app-tier-addons/app-1/checkout/complete", request.Url);
        Assert.Equal("txn-2", Body(request).GetProperty("PaymentTransactionId").GetString());
        Assert.Equal(AddOnCheckoutItemStatuses.Active, result.Status);
        Assert.Equal("sub-9", result.SubscriptionId);

        var (failingService, failingHandler, _) = CreateService();
        failingHandler.When("checkout/complete", HttpStatusCode.BadRequest,
            """
            {"addOnId":"radar","pricingId":"ap-1","status":"failed","errorCode":"PaymentNotVerified",
             "errorMessage":"Payment could not be verified"}
            """);

        var failed = await failingService.CompleteAddOnCheckoutAsync("app-1", "txn-2");

        Assert.Equal("radar", failed.AddOnId);
        Assert.Equal(AddOnCheckoutItemStatuses.Failed, failed.Status);
        Assert.Equal(AddOnCheckoutErrorCodes.PaymentNotVerified, failed.ErrorCode);
        Assert.Equal("Payment could not be verified", failed.ErrorMessage);
    }

    // ── Pack lifecycle ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SubscribeToAddOnDetailedAsync_ReturnsTheSubscription_OrAStructuredRefusal()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("app-tier-addons/app-1/subscribe",
            """{"id":"sub-1","userId":"u-1","appId":"app-1","appTierAddOnId":"radar"}""");

        var created = await service.SubscribeToAddOnDetailedAsync("app-1", "radar", "ap-1", "txn-1");

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/api/app-tier-addons/app-1/subscribe", request.Url);
        var body = Body(request);
        Assert.Equal("app-1", body.GetProperty("AppId").GetString());
        Assert.Equal("radar", body.GetProperty("AppTierAddOnId").GetString());
        Assert.Equal("ap-1", body.GetProperty("AppTierAddOnPricingId").GetString());
        Assert.Equal("txn-1", body.GetProperty("PaymentTransactionId").GetString());
        Assert.True(created.Success);
        Assert.Equal("sub-1", created.Subscription!.Id);
        Assert.Null(created.Error);

        var (refusingService, refusingHandler, _) = CreateService();
        refusingHandler.When("app-tier-addons/app-1/subscribe", HttpStatusCode.BadRequest,
            """{"errorCode":"BundledInTier","errorMessage":"Already in your plan"}""");

        var refused = await refusingService.SubscribeToAddOnDetailedAsync("app-1", "radar");

        Assert.False(refused.Success);
        Assert.Null(refused.Subscription);
        Assert.Equal(AddOnCheckoutErrorCodes.BundledInTier, refused.Error!.Code);
        Assert.Equal("Already in your plan", refused.Error.Message);
        Assert.Equal(400, refused.Error.Status);
    }

    [Fact]
    public async Task CancelAddOnDetailedAsync_PassesTheImmediateFlag_AndSurfacesTheSchedule()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("subscriptions/sub-1/cancel",
            """{"isScheduled":true,"status":"Active","effectiveDate":"2026-10-01T00:00:00Z"}""");

        var scheduled = await service.CancelAddOnDetailedAsync("sub-1");

        Assert.Contains("/api/app-tier-addons/subscriptions/sub-1/cancel?immediate=false", handler.Requests[0].Url);
        Assert.True(scheduled.Success);
        Assert.True(scheduled.IsScheduled);
        Assert.Equal("Active", scheduled.Status);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), scheduled.EffectiveDate!.Value.ToUniversalTime());

        await service.CancelAddOnDetailedAsync("sub-1", true);
        Assert.Contains("cancel?immediate=true", handler.Requests[1].Url);
    }

    [Fact]
    public async Task CancelAddOnDetailedAsync_KeepsTheServerErrorCodeOnA404ThatCarriesOne()
    {
        var (service, handler, _) = CreateService();
        handler.When("subscriptions/sub-nope/cancel", HttpStatusCode.NotFound,
            """{"error":"Add-on subscription not found","errorCode":"addon_subscription_not_found"}""");

        var result = await service.CancelAddOnDetailedAsync("sub-nope");

        Assert.False(result.Success);
        Assert.Equal(AddOnSubscriptionErrorCodes.NotFound, result.ErrorCode);
        Assert.Equal("Add-on subscription not found", result.ErrorMessage);
    }

    [Fact]
    public async Task ReactivateAddOnAsync_ReturnsTheRestoredSubscription()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("subscriptions/sub-1/reactivate",
            """{"id":"sub-1","userId":"u-1","appId":"app-1","status":"Active"}""");

        var result = await service.ReactivateAddOnAsync("sub-1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/api/app-tier-addons/subscriptions/sub-1/reactivate", request.Url);
        Assert.True(result.Success);
        Assert.Equal("Active", result.Status);
        Assert.Equal("sub-1", result.Subscription!.Id);

        var (refusingService, refusingHandler, _) = CreateService();
        refusingHandler.When("subscriptions/sub-1/reactivate", HttpStatusCode.BadRequest,
            """{"error":"Not scheduled to cancel","errorCode":"addon_subscription_not_pending_cancellation"}""");

        var refused = await refusingService.ReactivateAddOnAsync("sub-1");

        Assert.False(refused.Success);
        Assert.Equal(AddOnSubscriptionErrorCodes.NotPendingCancellation, refused.ErrorCode);
    }

    [Fact]
    public async Task ANetworkFailure_BecomesRequestFailed_NotNotSupported()
    {
        var service = CreateOfflineService();

        var result = await service.CancelAddOnDetailedAsync("sub-1");

        Assert.False(result.Success);
        Assert.Equal(AppTierActionErrorCodes.RequestFailed, result.ErrorCode);
        Assert.Equal("fetch failed", result.ErrorMessage);
    }

    [Fact]
    public async Task AServerErrorWithoutACode_BecomesRequestFailed()
    {
        var (service, handler, _) = CreateService();
        handler.When("subscriptions/sub-1/reactivate", HttpStatusCode.InternalServerError, "");

        var result = await service.ReactivateAddOnAsync("sub-1");

        Assert.False(result.Success);
        Assert.Equal(AppTierActionErrorCodes.RequestFailed, result.ErrorCode);
        Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
    }

    [Fact]
    public async Task TheDeprecatedBooleanWrappers_StillAnswerTrueOrFalse()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("app-tier-addons/app-1/subscribe", """{"id":"sub-1"}""");
        Assert.True(await service.SubscribeToAddOnAsync("app-1", "radar", null, null));

        var (refusingService, refusingHandler, _) = CreateService();
        refusingHandler.When("app-tier-addons/app-1/subscribe", HttpStatusCode.BadRequest, """{"errorCode":"TierTooLow"}""");
        Assert.False(await refusingService.SubscribeToAddOnAsync("app-1", "radar", null, null));

        var (cancelService, cancelHandler, _) = CreateService();
        cancelHandler.WhenOk("subscriptions/sub-1/cancel", """{"isScheduled":true}""");
        Assert.True(await cancelService.CancelAddOnSubscriptionAsync("sub-1"));
        // The deprecated wrapper now delegates, so it too states the schedule explicitly.
        Assert.Contains("cancel?immediate=false", cancelHandler.Requests[0].Url);

        var (missingService, missingHandler, _) = CreateService();
        missingHandler.When("subscriptions/sub-1/cancel", HttpStatusCode.NotFound, "");
        Assert.False(await missingService.CancelAddOnSubscriptionAsync("sub-1"));
    }

    // ── 3-D Secure plan change ──────────────────────────────────────────────────

    [Fact]
    public async Task ChangeTierAsync_OptionsOverload_PostsSupportsPaymentAction_AndReturnsRequiresActionAsData()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("my-subscription/change",
            """
            {"success":false,"requiresAction":true,"clientSecret":"pi_1_secret","pendingChangeId":"pc-1",
             "amountDue":20,"currency":"USD"}
            """);

        var result = await service.ChangeTierAsync("app-1", new SelfChangeTierOptions
        {
            NewTierId = "tier-2",
            NewPricingId = "pricing-7",
            SupportsPaymentAction = true
        });

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/api/app-tiers/app-1/my-subscription/change", request.Url);
        var body = Body(request);
        Assert.Equal("tier-2", body.GetProperty("NewAppTierId").GetString());
        Assert.Equal("pricing-7", body.GetProperty("NewAppTierPricingId").GetString());
        Assert.True(body.GetProperty("Immediate").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("PaymentTransactionId").ValueKind);
        Assert.True(body.GetProperty("SupportsPaymentAction").GetBoolean());

        // RequiresAction arrives with Success=false and is "not yet", not a refusal.
        Assert.False(result.Success);
        Assert.True(result.RequiresAction);
        Assert.Equal("pc-1", result.PendingChangeId);
        Assert.Equal("pi_1_secret", result.ClientSecret);
    }

    [Fact]
    public async Task ChangeTierAsync_PositionalOverload_PostsExactlyWhatItAlwaysDid()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("my-subscription/change", """{"success":true}""");

        await service.ChangeTierAsync("app-1", "tier-2", "pricing-7", true, "txn-9");

        var body = Body(Assert.Single(handler.Requests));
        Assert.Equal("tier-2", body.GetProperty("NewAppTierId").GetString());
        Assert.Equal("txn-9", body.GetProperty("PaymentTransactionId").GetString());
        // An older server rejects an unknown property, so the positional form must not send it.
        Assert.False(body.TryGetProperty("SupportsPaymentAction", out _));
    }

    [Fact]
    public async Task CompleteTierChangeAsync_PostsTheParkedChangeId_AndReportsRefusalsStructurally()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("change/pc-1/complete", """{"success":true,"isScheduled":false}""");

        var done = await service.CompleteTierChangeAsync("app-1", "pc-1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/api/app-tiers/app-1/my-subscription/change/pc-1/complete", request.Url);
        Assert.True(done.Success);

        var (expiringService, expiringHandler, _) = CreateService();
        expiringHandler.When("change/pc-1/complete", HttpStatusCode.BadRequest,
            """{"errorCode":"pending_change_expired","error":"That change lapsed"}""");

        var expired = await expiringService.CompleteTierChangeAsync("app-1", "pc-1");

        Assert.False(expired.Success);
        Assert.False(expired.IsScheduled);
        Assert.Equal(TierChangeErrorCodes.PendingChangeExpired, expired.ErrorCode);
        Assert.Equal("That change lapsed", expired.ErrorMessage);
    }

    [Fact]
    public async Task CompleteTierChangeAsync_ReturnsProcessingAsData()
    {
        // Processing, like RequiresAction, arrives with Success=false and means "not yet".
        var (service, handler, _) = CreateService();
        handler.WhenOk("change/pc-1/complete", """{"success":false,"processing":true}""");

        var result = await service.CompleteTierChangeAsync("app-1", "pc-1");

        Assert.False(result.Success);
        Assert.True(result.Processing);
        Assert.Null(result.ErrorCode);
    }

    // ── Public catalog ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPublicTiersOrThrowAsync_Throws_WhileTheSwallowingVariantReturnsEmpty()
    {
        // "No plans" and "pricing unavailable" have to be distinguishable.
        var (service, handler, _) = CreateService();
        handler.When("app-tiers/app-1/public", HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        Assert.Empty(await service.GetPublicTiersAsync("app-1"));
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetPublicTiersOrThrowAsync("app-1"));
    }

    [Fact]
    public async Task GetPublicCatalogAsync_SurfacesTheResponseCurrency_AndThrowsWhenTheCatalogFails()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("app-tiers/app-1/public",
            """[{"id":"t1","name":"Pro","status":"Active","displayOrder":1,"currency":"GBP"}]""");
        handler.WhenOk("app-tier-addons/app-1/public",
            """[{"id":"radar","name":"Radar","status":"Active","displayOrder":1}]""");

        var catalog = await service.GetPublicCatalogAsync("app-1");

        Assert.Equal("GBP", catalog.Currency);
        Assert.Single(catalog.Tiers);
        Assert.Single(catalog.AddOns);

        var (failingService, failingHandler, _) = CreateService();
        failingHandler.When("app-tiers/app-1/public", HttpStatusCode.ServiceUnavailable, """{"error":"down"}""");
        await Assert.ThrowsAsync<HttpRequestException>(() => failingService.GetPublicCatalogAsync("app-1"));
    }
}
