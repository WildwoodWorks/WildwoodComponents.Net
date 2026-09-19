using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Controllers;
using WildwoodComponents.Razor.Extensions;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// The HTTP contract the registration/subscription proxy owes its client JS:
/// 401 for a signed-out session with NOTHING forwarded, the configured app id (never the client's),
/// bodies re-serialised from the bound models so nothing extra reaches WildwoodAPI, and the one
/// relay rule — a refusal is 200 with the structured result, only an unreadable answer is 502.
/// </summary>
public class WildwoodRegistrationSubscriptionProxyControllerTests
{
    private const string ConfiguredAppId = "app-1";

    private static (WildwoodRegistrationSubscriptionProxyController Controller, FakeHttpMessageHandler Handler) CreateController(
        string? accessToken = "test-jwt", string? configuredAppId = ConfiguredAppId)
    {
        var handler = new FakeHttpMessageHandler();
        var session = new FakeSessionManager(accessToken);
        var appTiers = new WildwoodAppTierService(
            handler.CreateClient("https://api.test/api/"), session, NullLogger<WildwoodAppTierService>.Instance);
        var registration = new WildwoodRegistrationService(
            handler.CreateClient("https://api.test/api/"), session,
            NullLogger<WildwoodRegistrationService>.Instance, configuredAppId ?? string.Empty);

        var controller = new WildwoodRegistrationSubscriptionProxyController(
            appTiers,
            registration,
            session,
            new WildwoodComponentsRazorOptions { BaseUrl = "https://api.test/", AppId = configuredAppId },
            NullLogger<WildwoodRegistrationSubscriptionProxyController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        return (controller, handler);
    }

    private static JsonElement Body(FakeHttpMessageHandler.RecordedRequest request)
        => JsonDocument.Parse(request.Body ?? "{}").RootElement;

    private static T OkValue<T>(IActionResult result) where T : class
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<T>(ok.Value!);
    }

    private static void AssertStatus(IActionResult result, int expected)
    {
        var status = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(expected, status.StatusCode);
    }

    // ── Every authenticated route refuses a signed-out session ──────────────────

    [Fact]
    public async Task EveryAuthenticatedRoute_Answers401_AndForwardsNothing_WithoutASession()
    {
        var (controller, handler) = CreateController(accessToken: null);

        var results = new List<IActionResult>
        {
            await controller.TrialEligibility(null),
            await controller.Quote(new AddOnCheckoutQuoteRequestModel(), null),
            await controller.CheckoutPaymentMethod(new AddOnCheckoutPaymentMethodRequestModel { ProviderId = "prov-1" }, null),
            await controller.Checkout(new AddOnCheckoutRequestModel { CheckoutId = "co-1" }, null),
            await controller.CompleteCheckout(new AddOnCheckoutCompleteRequestModel { PaymentTransactionId = "txn-1" }, null),
            await controller.SubscribeAddOn(new AddOnSubscribeProxyRequest { AddOnId = "radar" }, null),
            await controller.CancelAddOn("sub-1"),
            await controller.ReactivateAddOn("sub-1"),
            await controller.TierChange(new SelfChangeTierOptions { NewTierId = "tier-2" }, null),
            await controller.CompleteTierChange("pc-1", null)
        };

        foreach (var result in results)
        {
            var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
            Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        }

        // Nothing reached WildwoodAPI: an empty bearer would have arrived as an anonymous call.
        Assert.Empty(handler.Requests);
    }

    // ── App id ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheConfiguredAppId_IsUsed_WhenTheClientNamesNone()
    {
        var (controller, handler) = CreateController();
        handler.WhenOk("trial-eligibility", """{"tierTrialEligible":true}""");

        await controller.TrialEligibility(null);

        Assert.Contains("/api/app-tiers/app-1/trial-eligibility", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task AClientNamedAppId_IsRejected_WhenItIsNotThisApp()
    {
        var (controller, handler) = CreateController();

        var result = await controller.Quote(new AddOnCheckoutQuoteRequestModel(), "some-other-app");

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AClientNamedAppId_IsAccepted_WhenItMatches_CaseAndSpacingAside()
    {
        var (controller, handler) = CreateController();
        handler.WhenOk("trial-eligibility", """{"tierTrialEligible":true}""");

        var result = await controller.TrialEligibility(" APP-1 ");

        Assert.IsType<OkObjectResult>(result);
        Assert.Contains("/api/app-tiers/app-1/trial-eligibility", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task WithNoConfiguredAppId_AnAppScopedRouteIsABadRequest()
    {
        // Honouring a client-named app would let a page act on any app in the company with the
        // session's token, so an unconfigured host refuses rather than trusting the browser.
        var (controller, handler) = CreateController(configuredAppId: null);

        var result = await controller.TrialEligibility("app-1");

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(handler.Requests);
    }

    // ── Body binding: only the bound model is forwarded ─────────────────────────

    [Fact]
    public async Task UnknownJsonProperties_AreNotForwarded()
    {
        // MVC binds the body to the Shared request model and the proxy re-serialises THAT, so a
        // property nobody declared cannot ride along to WildwoodAPI.
        var bound = JsonSerializer.Deserialize<AddOnCheckoutRequestModel>(
            """
            {"CheckoutId":"co-1","ProviderId":"prov-1","UseSavedCard":true,
             "Items":[{"AddOnId":"radar","Rogue":"x"}],
             "AdminOverride":true,"UserId":"someone-else"}
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var (controller, handler) = CreateController();
        handler.WhenOk("app-1/checkout", """{"success":true,"checkoutId":"co-1","results":[]}""");

        await controller.Checkout(bound, null);

        var body = Body(Assert.Single(handler.Requests));
        Assert.False(body.TryGetProperty("AdminOverride", out _));
        Assert.False(body.TryGetProperty("UserId", out _));
        var item = body.GetProperty("Items")[0];
        Assert.Equal("radar", item.GetProperty("AddOnId").GetString());
        Assert.False(item.TryGetProperty("Rogue", out _));
    }

    [Fact]
    public async Task AMissingBody_IsABadRequest_NotAForwardedEmptyCall()
    {
        var (controller, handler) = CreateController();

        Assert.IsType<BadRequestObjectResult>(await controller.Quote(null, null));
        Assert.IsType<BadRequestObjectResult>(await controller.Checkout(null, null));
        Assert.IsType<BadRequestObjectResult>(await controller.TierChange(null, null));
        Assert.IsType<BadRequestObjectResult>(await controller.SubscribeAddOn(null, null));
        Assert.Empty(handler.Requests);
    }

    // ── The relay rule ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ARefusal_IsRelayedAs200_WithTheStructuredResult()
    {
        var (controller, handler) = CreateController();
        handler.When("checkout/quote", HttpStatusCode.BadRequest,
            """{"success":false,"checkoutId":"co-2","errorCode":"AlreadySubscribed","errorMessage":"You already have that pack"}""");

        var result = await controller.Quote(
            new AddOnCheckoutQuoteRequestModel { Items = { new AddOnCheckoutItemInput { AddOnId = "radar" } } }, null);

        var quote = OkValue<AddOnCheckoutQuoteModel>(result);
        Assert.False(quote.Success);
        Assert.Equal(AddOnCheckoutErrorCodes.AlreadySubscribed, quote.ErrorCode);
        Assert.Equal("You already have that pack", quote.ErrorMessage);
    }

    [Fact]
    public async Task AServerWithoutTheRoute_IsRelayedAs200_NotSupported()
    {
        var (controller, handler) = CreateController();
        handler.When("checkout/payment-method", HttpStatusCode.NotFound, "");

        var result = await controller.CheckoutPaymentMethod(
            new AddOnCheckoutPaymentMethodRequestModel { ProviderId = "prov-1" }, null);

        var payment = OkValue<AddOnCheckoutPaymentMethodModel>(result);
        Assert.False(payment.Success);
        Assert.Equal(AppTierActionErrorCodes.NotSupported, payment.ErrorCode);
    }

    // ── Pack lifecycle ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SubscribeAddOn_ForwardsThePackAndItsPricing_UnderTheConfiguredApp()
    {
        var (controller, handler) = CreateController();
        handler.WhenOk("app-tier-addons/app-1/subscribe", """{"id":"sub-1","appId":"app-1"}""");

        var result = await controller.SubscribeAddOn(
            new AddOnSubscribeProxyRequest { AddOnId = "radar", PricingId = "ap-1", PaymentTransactionId = "txn-1" }, null);

        var subscribed = OkValue<AddOnSubscribeResultModel>(result);
        Assert.True(subscribed.Success);
        var body = Body(Assert.Single(handler.Requests));
        Assert.Equal("app-1", body.GetProperty("AppId").GetString());
        Assert.Equal("radar", body.GetProperty("AppTierAddOnId").GetString());
        Assert.Equal("ap-1", body.GetProperty("AppTierAddOnPricingId").GetString());
    }

    [Fact]
    public async Task CancelAddOn_PassesTheImmediateFlagThrough_Lowercase()
    {
        var (controller, handler) = CreateController();
        handler.WhenOk("subscriptions/sub-1/cancel", """{"isScheduled":true,"status":"Active"}""");

        var scheduled = OkValue<AddOnSubscriptionCancelResultModel>(await controller.CancelAddOn("sub-1"));
        Assert.True(scheduled.IsScheduled);
        Assert.Contains("cancel?immediate=false", handler.Requests[0].Url);

        await controller.CancelAddOn("sub-1", immediate: true);
        Assert.Contains("cancel?immediate=true", handler.Requests[1].Url);
    }

    [Fact]
    public async Task ReactivateAddOn_RelaysTheRestoredSubscription()
    {
        var (controller, handler) = CreateController();
        handler.WhenOk("subscriptions/sub-1/reactivate", """{"id":"sub-1","status":"Active"}""");

        var result = OkValue<AddOnSubscriptionReactivateResultModel>(await controller.ReactivateAddOn("sub-1"));

        Assert.True(result.Success);
        Assert.Equal("Active", result.Status);
        Assert.Contains("/api/app-tier-addons/subscriptions/sub-1/reactivate", Assert.Single(handler.Requests).Url);
    }

    // ── Plan change ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task TierChange_ForwardsSupportsPaymentAction_AndRelaysRequiresActionAsData()
    {
        var (controller, handler) = CreateController();
        handler.WhenOk("my-subscription/change",
            """{"success":false,"requiresAction":true,"clientSecret":"pi_1_secret","pendingChangeId":"pc-1"}""");

        var result = await controller.TierChange(
            new SelfChangeTierOptions { NewTierId = "tier-2", SupportsPaymentAction = true }, null);

        var change = OkValue<AppTierChangeResultModel>(result);
        Assert.True(change.RequiresAction);
        Assert.Equal("pc-1", change.PendingChangeId);
        Assert.True(Body(Assert.Single(handler.Requests)).GetProperty("SupportsPaymentAction").GetBoolean());
    }

    [Fact]
    public async Task CompleteTierChange_PostsTheParkedChangeId()
    {
        var (controller, handler) = CreateController();
        handler.WhenOk("change/pc-1/complete", """{"success":true}""");

        var change = OkValue<AppTierChangeResultModel>(await controller.CompleteTierChange("pc-1", null));

        Assert.True(change.Success);
        Assert.Contains("/api/app-tiers/app-1/my-subscription/change/pc-1/complete", Assert.Single(handler.Requests).Url);
    }

    // ── Catalog (anonymous) ─────────────────────────────────────────────────────

    [Fact]
    public async Task Catalog_IsServedWithoutASession_AndReports502WhenItCannotBeLoaded()
    {
        var (controller, handler) = CreateController(accessToken: null);
        handler.WhenOk("app-tiers/app-1/public", """[{"id":"t1","name":"Pro","status":"Active","currency":"GBP"}]""");
        handler.WhenOk("app-tier-addons/app-1/public", """[{"id":"radar","name":"Radar","status":"Active"}]""");

        var catalog = OkValue<PublicCatalog>(await controller.Catalog(null, null));
        Assert.Equal("GBP", catalog.Currency);
        Assert.Single(catalog.Tiers);
        Assert.Single(catalog.AddOns);

        // "Pricing is unavailable" must not read as "this app sells nothing".
        var (failing, failingHandler) = CreateController(accessToken: null);
        failingHandler.When("app-tiers/app-1/public", HttpStatusCode.ServiceUnavailable, """{"error":"down"}""");
        AssertStatus(await failing.Catalog(null, null), StatusCodes.Status502BadGateway);
    }

    // ── Token details (anonymous) ───────────────────────────────────────────────

    [Fact]
    public async Task TokenDetails_IsServedWithoutASession_AndScopesToTheConfiguredApp()
    {
        var (controller, handler) = CreateController(accessToken: null);
        handler.WhenOk("validate-detailed",
            """{"isValid":true,"appGrants":[{"appId":"app-1","appTierId":"tier-pro","addOnIds":[],"featureCodes":[]}]}""");

        var details = OkValue<RegistrationTokenDetails>(await controller.TokenDetails("TOKEN/1", null));

        Assert.True(details.IsValid);
        Assert.Equal("tier-pro", Assert.Single(details.AppGrants).AppTierId);
        var url = Assert.Single(handler.Requests).Url;
        Assert.Contains("registrationtokens/validate-detailed/TOKEN%2F1", url);
        Assert.Contains("appId=app-1", url);
    }

    [Fact]
    public async Task TokenDetails_TakesTheTokenFromThePostedBodyToo()
    {
        var (controller, handler) = CreateController(accessToken: null);
        handler.WhenOk("validate-detailed", """{"isValid":true}""");

        var details = OkValue<RegistrationTokenDetails>(
            await controller.TokenDetails(new RegistrationTokenDetailsProxyRequest { Token = "T" }));

        Assert.True(details.IsValid);
        Assert.Empty(details.AppGrants);
        Assert.Contains("validate-detailed/T", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task TokenDetails_SeparatesAnInvalidTokenFromOneItCouldNotRead()
    {
        var (invalid, invalidHandler) = CreateController(accessToken: null);
        invalidHandler.WhenOk("validate-detailed", """{"isValid":false,"errorMessage":"That token has expired"}""");

        var details = OkValue<RegistrationTokenDetails>(await invalid.TokenDetails("T", null));
        Assert.False(details.IsValid);
        Assert.Equal("That token has expired", details.ErrorMessage);

        // An older server without the route is 502, NOT "your token is invalid".
        var (unreadable, unreadableHandler) = CreateController(accessToken: null);
        unreadableHandler.When("validate-detailed", HttpStatusCode.NotFound, "");
        AssertStatus(await unreadable.TokenDetails("T", null), StatusCodes.Status502BadGateway);
    }

    [Fact]
    public async Task TokenDetails_RefusesABlankToken_AndAForeignAppScope()
    {
        var (controller, handler) = CreateController(accessToken: null);

        Assert.IsType<BadRequestObjectResult>(await controller.TokenDetails("  ", null));
        Assert.IsType<BadRequestObjectResult>(await controller.TokenDetails("T", "some-other-app"));
        Assert.IsType<BadRequestObjectResult>(await controller.TokenDetails((RegistrationTokenDetailsProxyRequest?)null));
        Assert.Empty(handler.Requests);
    }
}
