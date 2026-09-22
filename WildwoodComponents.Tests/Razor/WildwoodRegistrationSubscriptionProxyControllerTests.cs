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

        var disclaimers = new WildwoodDisclaimerService(
            handler.CreateClient("https://api.test/api/"), session,
            NullLogger<WildwoodDisclaimerService>.Instance);
        var payments = new WildwoodPaymentService(
            handler.CreateClient("https://api.test/api/"), session,
            NullLogger<WildwoodPaymentService>.Instance);

        var controller = new WildwoodRegistrationSubscriptionProxyController(
            appTiers,
            registration,
            session,
            disclaimers,
            payments,
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

    // ── The signup routes ───────────────────────────────────────────────────────
    //
    // Register, log in, link the plan's payment, subscribe, the disclaimer gate and the payment
    // intent used to be routes the HOST wrote (/api/wildwood-auth, /api/wildwood-subscription,
    // /api/wildwood-payment) or origin-relative hits on api/userregistration/* that only work when
    // WildwoodAPI happens to sit behind the same origin. They ship now, so these pin the contract
    // the signup script relies on.

    /// <summary>
    /// Registration, sign-in and the plan's card all happen BEFORE there is a session - that is
    /// what pay-first means - so requiring one on those routes would make the signup impossible.
    /// </summary>
    [Fact]
    public async Task TheRoutesASignupNeedsBeforeItHasASession_AreAnonymous()
    {
        var (controller, handler) = CreateController(accessToken: null);
        handler.WhenOk("auth-configuration", """{"allowOpenRegistration":true,"allowTokenRegistration":false}""");
        handler.WhenOk("userregistration/register", """{"success":true,"userId":"user-1"}""");
        handler.WhenOk("auth/login", """{"id":"user-1","jwtToken":"jwt","refreshToken":"rt"}""");
        handler.WhenOk("payment/initiate", """{"success":true,"paymentIntentId":"pi_1"}""");
        handler.WhenOk("payment/confirm", """{"success":true,"transactionId":"txn-1"}""");

        Assert.IsType<OkObjectResult>(await controller.RegistrationMode(null, null));
        Assert.IsType<OkObjectResult>(await controller.Register(new SignupRegisterProxyRequest { Email = "a@b.test" }, null));
        Assert.IsType<OkObjectResult>(await controller.Login(new SignupLoginProxyRequest { Email = "a@b.test" }, null));
        Assert.IsType<OkObjectResult>(await controller.InitiatePayment(new InitiatePaymentRequest { ProviderId = "prov-1" }, null));
        Assert.IsType<OkObjectResult>(await controller.ConfirmPayment(
            new SignupConfirmPaymentProxyRequest { PaymentIntentId = "pi_1", ProviderType = 1 }));
    }

    /// <summary>The routes that act AS the account do need one, and forward nothing without it.</summary>
    [Fact]
    public async Task TheRoutesThatActAsTheAccount_Answer401_AndForwardNothing_WithoutASession()
    {
        var (controller, handler) = CreateController(accessToken: null);

        var results = new List<IActionResult>
        {
            await controller.LinkTransaction(new SignupLinkTransactionProxyRequest { ExternalTransactionId = "pi_1", UserId = "user-1" }),
            await controller.Subscribe(new SignupSubscribeProxyRequest { TierId = "tier-1" }, null),
            await controller.PendingDisclaimers(null),
            await controller.AcceptDisclaimers(new SignupDisclaimerAcceptProxyRequest(), null)
        };

        foreach (var result in results) Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EveryNewRoute_RefusesAMissingBody()
    {
        var (controller, handler) = CreateController();

        Assert.IsType<BadRequestObjectResult>(await controller.Register(null, null));
        Assert.IsType<BadRequestObjectResult>(await controller.Login(null, null));
        Assert.IsType<BadRequestObjectResult>(await controller.LinkTransaction(null));
        Assert.IsType<BadRequestObjectResult>(await controller.Subscribe(null, null));
        Assert.IsType<BadRequestObjectResult>(await controller.AcceptDisclaimers(null, null));
        Assert.IsType<BadRequestObjectResult>(await controller.InitiatePayment(null, null));
        Assert.IsType<BadRequestObjectResult>(await controller.ConfirmPayment(null));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EveryNewAppScopedRoute_RejectsAClientNamedAppThatIsNotThisApp()
    {
        var (controller, handler) = CreateController();

        Assert.IsType<BadRequestObjectResult>(await controller.RegistrationMode("other-app", null));
        Assert.IsType<BadRequestObjectResult>(await controller.Register(new SignupRegisterProxyRequest(), "other-app"));
        Assert.IsType<BadRequestObjectResult>(await controller.Login(new SignupLoginProxyRequest(), "other-app"));
        Assert.IsType<BadRequestObjectResult>(await controller.Subscribe(new SignupSubscribeProxyRequest(), "other-app"));
        Assert.IsType<BadRequestObjectResult>(await controller.PendingDisclaimers("other-app"));
        Assert.Empty(handler.Requests);
    }

    // ── Registration mode ───────────────────────────────────────────────────────

    [Fact]
    public async Task RegistrationMode_ResolvesTheLiveFlags_AndLeaksNothingElse()
    {
        var (controller, handler) = CreateController(accessToken: null);
        handler.WhenOk("auth-configuration", """
            {"allowOpenRegistration":false,"allowTokenRegistration":true,
             "passwordMinimumLength":12,"registrationRateLimitPerHour":5,"defaultPricingModelId":"pm-1"}
            """);

        var mode = OkValue<SignupRegistrationModeModel>(await controller.RegistrationMode(null, null));

        Assert.False(mode.Closed);
        Assert.True(mode.RequireToken);
        Assert.False(mode.AllowOpenRegistration);
        Assert.False(mode.ShowOptionalTokenEntry);
        Assert.Equal("Config", mode.Source);

        // The password policy, the rate limits and the pricing model are on the upstream answer
        // and must not travel to a browser through this route.
        var relayed = JsonSerializer.Serialize(mode);
        Assert.DoesNotContain("passwordMinimumLength", relayed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RateLimit", relayed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pm-1", relayed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegistrationMode_BothFlagsOff_IsClosed()
    {
        var (controller, handler) = CreateController(accessToken: null);
        handler.WhenOk("auth-configuration", """{"allowOpenRegistration":false,"allowTokenRegistration":false}""");

        var mode = OkValue<SignupRegistrationModeModel>(await controller.RegistrationMode(null, null));

        Assert.True(mode.Closed);
    }

    /// <summary>
    /// Unreadable settings are not a closed sign-up: the fallback offers open registration with
    /// the optional token card, and the server refuses anything it does not allow.
    /// </summary>
    [Fact]
    public async Task RegistrationMode_FallsBackToOpen_WhenTheSettingsCannotBeRead()
    {
        var (controller, handler) = CreateController(accessToken: null);
        handler.When("auth-configuration", HttpStatusCode.ServiceUnavailable, "");

        var mode = OkValue<SignupRegistrationModeModel>(await controller.RegistrationMode(null, null));

        Assert.False(mode.Closed);
        Assert.True(mode.AllowOpenRegistration);
        Assert.True(mode.ShowOptionalTokenEntry);
        Assert.Equal("Fallback", mode.Source);
    }

    /// <summary>
    /// Invite redemption overrides even a CLOSED configuration, and does not read it at all: the
    /// server validates the invite token itself.
    /// </summary>
    [Fact]
    public async Task RegistrationMode_TokenModeRequired_OverridesTheConfiguration_WithoutReadingIt()
    {
        var (controller, handler) = CreateController(accessToken: null);
        handler.WhenOk("auth-configuration", """{"allowOpenRegistration":false,"allowTokenRegistration":false}""");

        var mode = OkValue<SignupRegistrationModeModel>(await controller.RegistrationMode(null, "required"));

        Assert.False(mode.Closed);
        Assert.True(mode.RequireToken);
        Assert.Equal("TokenMode", mode.Source);
        Assert.Empty(handler.Requests);
    }

    // ── Register ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_TakesTheTokenPath_WhenTheBodyCarriesOne()
    {
        var (controller, handler) = CreateController(accessToken: null);
        handler.WhenOk("register-with-token", """{"success":true,"userId":"user-1"}""");

        var result = OkValue<SignupRegisterResultModel>(await controller.Register(
            new SignupRegisterProxyRequest { Email = "a@b.test", Password = "pw", Token = "TOK" }, null));

        Assert.True(result.Success);
        Assert.Equal("user-1", result.UserId);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("userregistration/register-with-token", request.Url);

        var body = Body(request);
        Assert.Equal("TOK", body.GetProperty("token").GetString());
        // The app id is the configured one, never the body's.
        Assert.Equal(ConfiguredAppId, body.GetProperty("appId").GetString());
    }

    [Fact]
    public async Task Register_TakesTheOpenPath_AndCarriesTheAttributionPayload()
    {
        var (controller, handler) = CreateController(accessToken: null);
        handler.WhenOk("userregistration/register", """{"success":true,"userId":"user-2"}""");

        await controller.Register(new SignupRegisterProxyRequest
        {
            Email = "a@b.test",
            Password = "pw",
            PricingModelId = "pm-1",
            Attribution = new AttributionPayloadModel { VisitorKey = "visitor-9" }
        }, null);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("userregistration/register", request.Url);
        Assert.DoesNotContain("register-with-token", request.Url);

        var body = Body(request);
        Assert.Equal("pm-1", body.GetProperty("pricingModelId").GetString());
        Assert.Equal("visitor-9", body.GetProperty("attribution").GetProperty("visitorKey").GetString());
    }

    /// <summary>A refusal is 200 with the server's own words, never an HTTP error.</summary>
    [Fact]
    public async Task Register_RelaysARefusal_AsData()
    {
        var (controller, handler) = CreateController(accessToken: null);
        handler.WhenOk("userregistration/register", """{"success":false,"message":"That email is already registered."}""");

        var result = OkValue<SignupRegisterResultModel>(await controller.Register(
            new SignupRegisterProxyRequest { Email = "a@b.test" }, null));

        Assert.False(result.Success);
        Assert.Equal("That email is already registered.", result.Message);
        Assert.Equal("registration_refused", result.ErrorCode);
    }

    /// <summary>
    /// The register answer carries provider keys and a granted-app list upstream. None of that is
    /// of use to a signup page, so none of it is relayed.
    /// </summary>
    [Fact]
    public async Task Register_RelaysNeitherProviderKeysNorGrantedApps()
    {
        var (controller, handler) = CreateController(accessToken: null);
        handler.WhenOk("userregistration/register", """
            {"success":true,"userId":"user-3","paymentPublishableKey":"pk_live_secretish",
             "grantedApps":["app-1","app-2"],"paymentProviderId":"prov-1"}
            """);

        var result = OkValue<SignupRegisterResultModel>(await controller.Register(
            new SignupRegisterProxyRequest { Email = "a@b.test" }, null));

        var relayed = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("pk_live_secretish", relayed, StringComparison.Ordinal);
        Assert.DoesNotContain("grantedApps", relayed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prov-1", relayed, StringComparison.Ordinal);
    }

    // ── Login ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// The login route establishes the SERVER session through the existing registration service,
    /// which is the one session mechanism this package has. The browser gets a user id, never a
    /// token.
    /// </summary>
    [Fact]
    public async Task Login_PutsTheTokensInTheSession_AndReturnsNoneOfThem()
    {
        var handler = new FakeHttpMessageHandler();
        handler.WhenOk("auth/login", """
            {"id":"user-7","email":"a@b.test","jwtToken":"jwt-value","refreshToken":"refresh-value",
             "requiresDisclaimerAcceptance":true}
            """);

        var session = new FakeSessionManager(accessToken: null);
        var registration = new WildwoodRegistrationService(
            handler.CreateClient("https://api.test/api/"), session,
            NullLogger<WildwoodRegistrationService>.Instance, ConfiguredAppId);

        var controller = new WildwoodRegistrationSubscriptionProxyController(
            new WildwoodAppTierService(handler.CreateClient("https://api.test/api/"), session,
                NullLogger<WildwoodAppTierService>.Instance),
            registration,
            session,
            new WildwoodDisclaimerService(handler.CreateClient("https://api.test/api/"), session,
                NullLogger<WildwoodDisclaimerService>.Instance),
            new WildwoodPaymentService(handler.CreateClient("https://api.test/api/"), session,
                NullLogger<WildwoodPaymentService>.Instance),
            new WildwoodComponentsRazorOptions { BaseUrl = "https://api.test/", AppId = ConfiguredAppId },
            NullLogger<WildwoodRegistrationSubscriptionProxyController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = OkValue<SignupLoginResultModel>(await controller.Login(
            new SignupLoginProxyRequest { Username = "a@b.test", Email = "a@b.test", Password = "pw" }, null));

        Assert.True(result.Success);
        Assert.Equal("user-7", result.UserId);
        Assert.True(result.RequiresDisclaimerAcceptance);

        // The session now holds the tokens...
        Assert.True(session.IsAuthenticated);
        Assert.Equal("jwt-value", session.GetAccessToken());

        // ...and the browser does not.
        var relayed = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("jwt-value", relayed, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-value", relayed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_RelaysARefusal_AsData()
    {
        var (controller, handler) = CreateController(accessToken: null);
        handler.When("auth/login", HttpStatusCode.Unauthorized, """{"message":"no"}""");

        var result = OkValue<SignupLoginResultModel>(await controller.Login(
            new SignupLoginProxyRequest { Email = "a@b.test", Password = "pw" }, null));

        Assert.False(result.Success);
        Assert.Equal("login_failed", result.ErrorCode);
    }

    // ── Link, subscribe, disclaimers ────────────────────────────────────────────

    [Fact]
    public async Task LinkTransaction_ForwardsTheExternalIdAndTheUser()
    {
        var (controller, handler) = CreateController();
        handler.WhenOk("link-by-external-id", "{}");

        var result = OkValue<SignupLinkTransactionResultModel>(await controller.LinkTransaction(
            new SignupLinkTransactionProxyRequest { ExternalTransactionId = "pi_1", UserId = "user-1" }));

        Assert.True(result.Success);

        var body = Body(Assert.Single(handler.Requests));
        Assert.Equal("pi_1", body.GetProperty("externalTransactionId").GetString());
        Assert.Equal("user-1", body.GetProperty("userId").GetString());
    }

    /// <summary>A failed link is answered as data: the money is taken and the account exists.</summary>
    [Fact]
    public async Task LinkTransaction_AnswersAFailureAsData()
    {
        var (controller, handler) = CreateController();
        handler.When("link-by-external-id", HttpStatusCode.NotFound, "");

        var result = OkValue<SignupLinkTransactionResultModel>(await controller.LinkTransaction(
            new SignupLinkTransactionProxyRequest { ExternalTransactionId = "pi_1", UserId = "user-1" }));

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Subscribe_SendsThePricingOptionAndTheTransaction_ToTheConfiguredApp()
    {
        var (controller, handler) = CreateController();
        handler.WhenOk("my-subscription", """{"success":true}""");

        var result = OkValue<AppTierChangeResultModel>(await controller.Subscribe(
            new SignupSubscribeProxyRequest
            {
                TierId = "tier-pro",
                PricingId = "price-m",
                PaymentTransactionId = "txn-1"
            }, null));

        Assert.True(result.Success);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/api/app-tiers/app-1/my-subscription", request.Url);

        var body = Body(request);
        Assert.Equal("tier-pro", body.GetProperty("AppTierId").GetString());
        Assert.Equal("price-m", body.GetProperty("AppTierPricingId").GetString());
        Assert.Equal("txn-1", body.GetProperty("PaymentTransactionId").GetString());
    }

    [Fact]
    public async Task PendingDisclaimers_AsksForTheRegistrationOnes_AndAnswersEmptyWhenUnreadable()
    {
        var (controller, handler) = CreateController();
        handler.WhenOk("disclaimeracceptance/pending", """
            {"hasPendingDisclaimers":true,"disclaimers":[{"disclaimerId":"d1","versionId":"v1","title":"Terms"}]}
            """);

        var pending = OkValue<PendingDisclaimersResponse>(await controller.PendingDisclaimers(null));
        Assert.True(pending.HasPendingDisclaimers);
        Assert.Equal("d1", Assert.Single(pending.Disclaimers).DisclaimerId);
        Assert.Contains("showOn=registration", Assert.Single(handler.Requests).Url);

        // Fails OPEN: a signup that has already created an account and taken a card must not
        // dead-end because a disclaimer list did not load.
        var (blind, blindHandler) = CreateController();
        blindHandler.When("disclaimeracceptance/pending", HttpStatusCode.ServiceUnavailable, "");

        var none = OkValue<PendingDisclaimersResponse>(await blind.PendingDisclaimers(null));
        Assert.False(none.HasPendingDisclaimers);
        Assert.Empty(none.Disclaimers);
    }

    [Fact]
    public async Task AcceptDisclaimers_PostsTheAcceptancesInOneCall()
    {
        var (controller, handler) = CreateController();
        handler.WhenOk("accept-bulk", "{}");

        var result = await controller.AcceptDisclaimers(new SignupDisclaimerAcceptProxyRequest
        {
            Acceptances =
            [
                new DisclaimerAcceptanceResult { CompanyDisclaimerId = "d1", CompanyDisclaimerVersionId = "v1" }
            ]
        }, null);

        Assert.IsType<OkObjectResult>(result);
        Assert.Contains("accept-bulk", Assert.Single(handler.Requests).Url);
    }

    // ── Payment ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The app id is the configured one even when the body names another: a client-named app would
    /// let a page initiate a payment against any app in the company.
    /// </summary>
    [Fact]
    public async Task InitiatePayment_PinsTheAppId_AndForwardsTheBoundRequest()
    {
        var (controller, handler) = CreateController(accessToken: null);
        handler.WhenOk("payment/initiate", """{"success":true,"paymentIntentId":"pi_1","clientSecret":"cs_1"}""");

        var result = OkValue<InitiatePaymentResponse>(await controller.InitiatePayment(new InitiatePaymentRequest
        {
            ProviderId = "prov-1",
            AppId = "app-1",
            Amount = 10m,
            Currency = "USD",
            CustomerEmail = "a@b.test",
            IsSubscription = true
        }, null));

        Assert.True(result.Success);

        var body = Body(Assert.Single(handler.Requests));
        Assert.Equal(ConfiguredAppId, body.GetProperty("AppId").GetString());
        Assert.Equal("a@b.test", body.GetProperty("CustomerEmail").GetString());
    }

    [Fact]
    public async Task ConfirmPayment_RelaysTheProvidersAnswer()
    {
        var (controller, handler) = CreateController(accessToken: null);
        handler.WhenOk("payment/confirm", """{"success":true,"transactionId":"txn-9","paymentIntentId":"pi_1"}""");

        var result = OkValue<PaymentCompletionResult>(await controller.ConfirmPayment(
            new SignupConfirmPaymentProxyRequest { PaymentIntentId = "pi_1", ProviderType = 1 }));

        Assert.True(result.Success);
        Assert.Equal("txn-9", result.TransactionId);
        Assert.Contains("payment/confirm", Assert.Single(handler.Requests).Url);
    }
}
