using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Extensions;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Controllers;

/// <summary>
/// Same-origin proxy for the registration-and-subscription routes the browser needs: trial
/// eligibility, the pack checkout (quote → card → buy → complete), the pack lifecycle
/// (subscribe / cancel / reactivate), the 3-D Secure plan change and its completion, the public
/// catalog, and what a registration token grants.
///
/// It ships WITH the library, like <see cref="WildwoodAttributionProxyController"/> and
/// <see cref="WildwoodNotificationsProxyController"/>, because a Razor app keeps the user's JWT in
/// the server session: the client JS cannot call WildwoodAPI itself, and leaving these routes to
/// every host to implement would make each new route 404 silently. The services behind it put the
/// session's bearer on the REQUEST, never on the shared client's default headers.
///
/// Host wiring: register MVC controllers (<c>builder.Services.AddControllers()</c> +
/// <c>app.MapControllers()</c>) and have server-side session available, as for the notifications
/// and attribution proxies. If your host restricts controller discovery, add this assembly
/// explicitly: <c>AddControllers().AddApplicationPart(typeof(WildwoodRegistrationSubscriptionProxyController).Assembly)</c>.
///
/// <para><b>The relay rule (one rule, so the client can rely on it).</b> Every structured answer is
/// relayed as <b>HTTP 200 with the service's result DTO, refusals included</b> — a refusal is data
/// (<c>success:false</c> with <c>errorCode</c>/<c>errorMessage</c>, or a per-pack
/// <c>status:"failed"</c>), never an HTTP error status, because the upstream endpoints answer a
/// refusal with the same DTO they answer a success with and the client has to be able to word
/// "you already own that pack" differently from "that route does not exist yet". Only the proxy's
/// own preconditions, and an answer that could not be read at all, produce something else:
/// <b>401</b> when there is no signed-in session (no request is forwarded and no empty bearer is
/// ever sent), <b>400</b> for a missing body or an app id that is not this app's, and <b>502</b>
/// for the two loads that have no structured refusal — the public catalog and the token details —
/// so "unavailable" stays distinguishable from "sells nothing" / "invalid token".</para>
///
/// <para><b>App id.</b> Always the one configured in <c>AddWildwoodComponentsRazor</c>. A client
/// may send <c>appId</c>, but only so the proxy can REJECT a mismatch (400): the session token is
/// held server-side, so honouring a client-named app would let a page act on any app in the
/// company with the user's token.</para>
///
/// <para><b>Anti-forgery.</b> None, matching the notifications and attribution proxies — do not
/// invent a different scheme here. Every route is JSON-bodied and session-authorised; a host that
/// wants CSRF tokens should apply its own filter/middleware uniformly across all three proxies.</para>
/// </summary>
[ApiController]
[Route("api/wildwood-regsub")]
[Produces("application/json")]
public class WildwoodRegistrationSubscriptionProxyController : ControllerBase
{
    private readonly IWildwoodAppTierService _appTiers;
    private readonly IWildwoodRegistrationService _registration;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly IWildwoodDisclaimerService _disclaimers;
    private readonly IWildwoodPaymentService _payments;
    private readonly WildwoodComponentsRazorOptions _options;
    private readonly ILogger<WildwoodRegistrationSubscriptionProxyController> _logger;

    public WildwoodRegistrationSubscriptionProxyController(
        IWildwoodAppTierService appTiers,
        IWildwoodRegistrationService registration,
        IWildwoodSessionManager sessionManager,
        IWildwoodDisclaimerService disclaimers,
        IWildwoodPaymentService payments,
        WildwoodComponentsRazorOptions options,
        ILogger<WildwoodRegistrationSubscriptionProxyController> logger)
    {
        _appTiers = appTiers;
        _registration = registration;
        _sessionManager = sessionManager;
        _disclaimers = disclaimers;
        _payments = payments;
        _options = options;
        _logger = logger;
    }

    #region Preconditions

    private IActionResult NotAuthenticated()
        => Unauthorized(new { error = "not_authenticated", message = "Sign in to continue." });

    /// <summary>
    /// The signed-in gate. Authenticated routes answer 401 and forward nothing when the session
    /// holds no token — a request with no bearer would reach WildwoodAPI as an anonymous call.
    /// </summary>
    private bool TryRequireSession(out IActionResult? failure)
    {
        if (_sessionManager.IsAuthenticated)
        {
            failure = null;
            return true;
        }

        failure = NotAuthenticated();
        return false;
    }

    /// <summary>
    /// The configured app id, refusing a client-named one that is not it. Never returns the
    /// client's value.
    /// </summary>
    private bool TryResolveAppId(string? clientAppId, out string appId, out IActionResult? failure)
    {
        appId = _options.AppId?.Trim() ?? string.Empty;

        if (appId.Length == 0)
        {
            failure = BadRequest(new
            {
                error = "app_not_configured",
                message = "No AppId is configured for WildwoodComponents.Razor."
            });
            return false;
        }

        if (!string.IsNullOrWhiteSpace(clientAppId)
            && !string.Equals(clientAppId!.Trim(), appId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rejected a Wildwood proxy call for app {ClientAppId}; this host serves {AppId}.",
                clientAppId, appId);
            failure = BadRequest(new { error = "app_id_mismatch", message = "That app is not served by this site." });
            return false;
        }

        failure = null;
        return true;
    }

    /// <summary>Session + app id together, for the authenticated app-scoped routes.</summary>
    private bool TryBegin(string? clientAppId, out string appId, out IActionResult? failure)
    {
        appId = string.Empty;
        return TryRequireSession(out failure) && TryResolveAppId(clientAppId, out appId, out failure);
    }

    private IActionResult MissingBody()
        => BadRequest(new { error = "invalid_request", message = "A request body is required." });

    #endregion

    #region Trial eligibility

    /// <summary>
    /// GET /api/wildwood-regsub/trial-eligibility — whether this account may still start a free
    /// trial, on a tier and per pack. Never fails structurally: the service answers "eligible" with
    /// an empty (unknown) pack map when the lookup fails.
    /// </summary>
    [HttpGet("trial-eligibility")]
    public async Task<IActionResult> TrialEligibility([FromQuery] string? appId)
    {
        if (!TryBegin(appId, out var resolvedAppId, out var failure)) return failure!;

        return Ok(await _appTiers.GetTrialEligibilityAsync(resolvedAppId));
    }

    #endregion

    #region Pack checkout

    /// <summary>
    /// POST /api/wildwood-regsub/checkout/quote — price a basket of packs. Body:
    /// <c>{ "Items": [{ "AddOnId": "...", "PricingId": "..." }] }</c>.
    /// </summary>
    [HttpPost("checkout/quote")]
    public async Task<IActionResult> Quote([FromBody] AddOnCheckoutQuoteRequestModel? request, [FromQuery] string? appId)
    {
        if (!TryBegin(appId, out var resolvedAppId, out var failure)) return failure!;
        if (request is null) return MissingBody();

        return Ok(await _appTiers.QuoteAddOnCheckoutAsync(resolvedAppId, request.Items));
    }

    /// <summary>
    /// POST /api/wildwood-regsub/checkout/payment-method — start the one-off card entry for an
    /// account with no card on file. Body: <c>{ "ProviderId": "..." }</c>.
    /// </summary>
    [HttpPost("checkout/payment-method")]
    public async Task<IActionResult> CheckoutPaymentMethod(
        [FromBody] AddOnCheckoutPaymentMethodRequestModel? request, [FromQuery] string? appId)
    {
        if (!TryBegin(appId, out var resolvedAppId, out var failure)) return failure!;
        if (request is null) return MissingBody();

        return Ok(await _appTiers.CreateCheckoutPaymentMethodAsync(resolvedAppId, request.ProviderId));
    }

    /// <summary>
    /// POST /api/wildwood-regsub/checkout — buy the quoted basket. Read <c>results</c> per pack:
    /// one pack failing does not stop the others, and a <c>requires_action</c> line still has to be
    /// authenticated and completed.
    /// </summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] AddOnCheckoutRequestModel? request, [FromQuery] string? appId)
    {
        if (!TryBegin(appId, out var resolvedAppId, out var failure)) return failure!;
        if (request is null) return MissingBody();

        return Ok(await _appTiers.CheckoutAddOnsAsync(resolvedAppId, request));
    }

    /// <summary>
    /// POST /api/wildwood-regsub/checkout/complete — finish one pack whose card the customer has
    /// just authenticated. Body: <c>{ "PaymentTransactionId": "..." }</c>.
    /// </summary>
    [HttpPost("checkout/complete")]
    public async Task<IActionResult> CompleteCheckout(
        [FromBody] AddOnCheckoutCompleteRequestModel? request, [FromQuery] string? appId)
    {
        if (!TryBegin(appId, out var resolvedAppId, out var failure)) return failure!;
        if (request is null) return MissingBody();

        return Ok(await _appTiers.CompleteAddOnCheckoutAsync(resolvedAppId, request.PaymentTransactionId));
    }

    #endregion

    #region Pack lifecycle

    /// <summary>
    /// POST /api/wildwood-regsub/addons/subscribe — subscribe to a single pack. A refusal (already
    /// owned, bundled in the tier, payment not accepted) comes back as 200 with
    /// <c>success:false</c> and the reason.
    /// </summary>
    [HttpPost("addons/subscribe")]
    public async Task<IActionResult> SubscribeAddOn([FromBody] AddOnSubscribeProxyRequest? request, [FromQuery] string? appId)
    {
        if (!TryBegin(appId, out var resolvedAppId, out var failure)) return failure!;
        if (request is null) return MissingBody();

        return Ok(await _appTiers.SubscribeToAddOnDetailedAsync(
            resolvedAppId, request.AddOnId, request.PricingId, request.PaymentTransactionId));
    }

    /// <summary>
    /// POST /api/wildwood-regsub/addons/{subscriptionId}/cancel?immediate=false — cancel one of the
    /// caller's own packs. Without <c>immediate=true</c> access continues to the end of the period
    /// already paid for.
    /// </summary>
    [HttpPost("addons/{subscriptionId}/cancel")]
    public async Task<IActionResult> CancelAddOn(string subscriptionId, [FromQuery] bool immediate = false)
    {
        if (!TryRequireSession(out var failure)) return failure!;

        return Ok(await _appTiers.CancelAddOnDetailedAsync(subscriptionId, immediate));
    }

    /// <summary>
    /// POST /api/wildwood-regsub/addons/{subscriptionId}/reactivate — take back a scheduled
    /// cancellation.
    /// </summary>
    [HttpPost("addons/{subscriptionId}/reactivate")]
    public async Task<IActionResult> ReactivateAddOn(string subscriptionId)
    {
        if (!TryRequireSession(out var failure)) return failure!;

        return Ok(await _appTiers.ReactivateAddOnAsync(subscriptionId));
    }

    #endregion

    #region Plan change

    /// <summary>
    /// POST /api/wildwood-regsub/tier-change — the self-service plan change. Body keys are the
    /// ones WildwoodAPI binds: <c>NewAppTierId</c>, <c>NewAppTierPricingId</c>, <c>Immediate</c>,
    /// <c>PaymentTransactionId</c>, <c>SupportsPaymentAction</c>. Send
    /// <c>SupportsPaymentAction:true</c> only from a page that can confirm a card payment and will
    /// then call the completion route — otherwise a change needing 3-D Secure parks with nobody to
    /// finish it.
    /// </summary>
    [HttpPost("tier-change")]
    public async Task<IActionResult> TierChange([FromBody] SelfChangeTierOptions? options, [FromQuery] string? appId)
    {
        if (!TryBegin(appId, out var resolvedAppId, out var failure)) return failure!;
        if (options is null) return MissingBody();

        return Ok(await _appTiers.ChangeTierAsync(resolvedAppId, options));
    }

    /// <summary>
    /// POST /api/wildwood-regsub/tier-change/{pendingChangeId}/complete — finish a parked change
    /// once its prorated payment has been authenticated. Idempotent; the server answers
    /// <c>processing:true</c> while the payment settles.
    /// </summary>
    [HttpPost("tier-change/{pendingChangeId}/complete")]
    public async Task<IActionResult> CompleteTierChange(string pendingChangeId, [FromQuery] string? appId)
    {
        if (!TryBegin(appId, out var resolvedAppId, out var failure)) return failure!;

        return Ok(await _appTiers.CompleteTierChangeAsync(resolvedAppId, pendingChangeId));
    }

    #endregion

    #region Public catalog and token details (anonymous)

    /// <summary>
    /// GET /api/wildwood-regsub/catalog?currency= — everything the app sells, in display order,
    /// under one resolved currency. Anonymous: this is the same public data the pricing page
    /// renders server-side. A failed load is 502, NOT an empty catalog, so "pricing is unavailable
    /// right now" stays distinguishable from "this app sells nothing".
    /// </summary>
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog([FromQuery] string? appId, [FromQuery] string? currency)
    {
        if (!TryResolveAppId(appId, out var resolvedAppId, out var failure)) return failure!;

        try
        {
            return Ok(await _appTiers.GetPublicCatalogAsync(resolvedAppId, currency));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Public catalog unavailable for app {AppId}", resolvedAppId);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "catalog_unavailable", message = "Pricing is unavailable right now." });
        }
    }

    /// <summary>
    /// GET /api/wildwood-regsub/token-details?token= — what a registration token grants.
    /// Anonymous, because registration happens before there is a session; it returns exactly what
    /// the anonymous WildwoodAPI route returns for that token and nothing else. 502 means the
    /// details could not be READ (an older server, a transport failure) — which is NOT "your token
    /// is invalid": an invalid token is 200 with <c>isValid:false</c> and the server's message.
    /// </summary>
    [HttpGet("token-details")]
    public Task<IActionResult> TokenDetails([FromQuery] string? token, [FromQuery] string? appId)
        => ReadTokenDetailsAsync(token, appId);

    /// <summary>
    /// POST /api/wildwood-regsub/token-details — the same lookup with the token in the body, so it
    /// never lands in an access log or a Referer header. Body: <c>{ "Token": "..." }</c>.
    /// </summary>
    [HttpPost("token-details")]
    public Task<IActionResult> TokenDetails([FromBody] RegistrationTokenDetailsProxyRequest? request)
        => request is null
            ? Task.FromResult(MissingBody())
            : ReadTokenDetailsAsync(request.Token, request.AppId);

    private async Task<IActionResult> ReadTokenDetailsAsync(string? token, string? clientAppId)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { error = "invalid_request", message = "A registration token is required." });

        // Scope to the configured app when there is one; a client-named app is only ever checked,
        // never forwarded. With nothing configured the lookup stays unscoped, which is what the
        // anonymous WildwoodAPI route does by default.
        var configuredAppId = _options.AppId?.Trim() ?? string.Empty;
        if (configuredAppId.Length > 0
            && !string.IsNullOrWhiteSpace(clientAppId)
            && !string.Equals(clientAppId!.Trim(), configuredAppId, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "app_id_mismatch", message = "That app is not served by this site." });
        }

        var details = await _registration.GetRegistrationTokenDetailsAsync(
            token!, configuredAppId.Length > 0 ? configuredAppId : null);

        // null = unreadable, not invalid: the caller falls back to the plain validity check.
        return details is null
            ? StatusCode(StatusCodes.Status502BadGateway,
                new { error = "token_details_unavailable", message = "The registration token could not be checked." })
            : Ok(details);
    }

    #endregion

    #region Signup: registration mode, account creation, sign-in

    /// <summary>
    /// GET /api/wildwood-regsub/registration-mode?tokenMode= — how the signup screen may offer
    /// registration right now. Anonymous, because a signup screen has no session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The answer is the RESOLVED mode plus nothing else: the app's auth configuration is read
    /// server-side and reduced to <c>AllowOpenRegistration</c>/<c>AllowTokenRegistration</c>
    /// before <see cref="SignupRegistrationMode.Resolve"/> turns those into the four flags a
    /// screen renders from. Password policy, rate limits and provider settings are on that same
    /// upstream route and never leave the server.
    /// </para>
    /// <para>
    /// Never 502: settings that cannot be read resolve to open sign-up with the optional token
    /// card (<c>source: "Fallback"</c>), because the server enforces its own rules anyway — the
    /// fallback can only offer a path that is refused with a clear message, never grant one.
    /// Pass <c>tokenMode=required</c> for invite redemption, which overrides even a closed
    /// configuration.
    /// </para>
    /// </remarks>
    [HttpGet("registration-mode")]
    public async Task<IActionResult> RegistrationMode([FromQuery] string? appId, [FromQuery] string? tokenMode)
    {
        if (!TryResolveAppId(appId, out var resolvedAppId, out var failure)) return failure!;

        var settings = string.Equals(tokenMode, "required", StringComparison.OrdinalIgnoreCase)
            ? null // Not even read: the invite's token decides, and the server validates it.
            : await _registration.GetSignupRegistrationSettingsAsync(resolvedAppId);

        var mode = SignupRegistrationMode.Resolve(
            settings,
            string.Equals(tokenMode, "required", StringComparison.OrdinalIgnoreCase)
                ? SignupTokenMode.Required
                : SignupTokenMode.Auto);

        return Ok(new SignupRegistrationModeModel
        {
            Closed = mode.Closed,
            RequireToken = mode.RequireToken,
            AllowOpenRegistration = mode.AllowOpenRegistration,
            ShowOptionalTokenEntry = mode.ShowOptionalTokenEntry,
            Source = mode.Source.ToString()
        });
    }

    /// <summary>
    /// POST /api/wildwood-regsub/register — create the account. Anonymous, necessarily: this is
    /// what makes the session that every other route needs.
    /// </summary>
    /// <remarks>
    /// A non-empty <c>Token</c> takes the token path, which is also the path that applies whatever
    /// plan the token grants; otherwise open registration, carrying the app's default pricing
    /// model when the page was given one. The browser's attribution payload rides along exactly as
    /// it does on the existing Razor registration, so a signup through this view is attributed the
    /// same way. A refusal is 200 with <c>success:false</c> and the server's own words.
    /// </remarks>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] SignupRegisterProxyRequest? request, [FromQuery] string? appId)
    {
        if (!TryResolveAppId(appId, out var resolvedAppId, out var failure)) return failure!;
        if (request is null) return MissingBody();

        RegistrationSuccessResponse? response;

        if (request.Token is { Length: > 0 })
        {
            response = await _registration.RegisterWithTokenAsync(new TokenRegistrationRequest
            {
                Token = request.Token,
                FirstName = request.FirstName ?? string.Empty,
                LastName = request.LastName ?? string.Empty,
                Username = request.Username ?? string.Empty,
                Email = request.Email ?? string.Empty,
                Password = request.Password ?? string.Empty,
                AppId = resolvedAppId,
                Platform = "Web",
                DeviceInfo = "Browser",
                Attribution = request.Attribution
            });
        }
        else
        {
            response = await _registration.RegisterAsync(new OpenRegistrationRequest
            {
                FirstName = request.FirstName ?? string.Empty,
                LastName = request.LastName ?? string.Empty,
                Username = request.Username ?? string.Empty,
                Email = request.Email ?? string.Empty,
                Password = request.Password ?? string.Empty,
                AppId = resolvedAppId,
                Platform = "Web",
                DeviceInfo = "Browser",
                PricingModelId = request.PricingModelId,
                Attribution = request.Attribution
            });
        }

        if (response is null || !response.Success)
        {
            return Ok(new SignupRegisterResultModel
            {
                Success = false,
                Message = response?.Message is { Length: > 0 } m ? m : "Registration failed. Please try again.",
                ErrorCode = "registration_refused"
            });
        }

        // Only the id and the message: the provider keys and granted-app list on the upstream
        // answer are of no use to a signup page and are not the browser's business.
        return Ok(new SignupRegisterResultModel
        {
            Success = true,
            UserId = response.UserId,
            Message = response.Message
        });
    }

    /// <summary>
    /// POST /api/wildwood-regsub/login — sign the new account in and put its tokens in the SERVER
    /// session, through the same <see cref="IWildwoodRegistrationService.LoginAsync"/> the existing
    /// Razor registration uses. Anonymous, for the same reason register is.
    /// </summary>
    /// <remarks>
    /// The answer carries the user id and whether disclaimers are pending, and nothing else. There
    /// is exactly one session mechanism in this package — <see cref="IWildwoodSessionManager"/>,
    /// written by that service — and handing the browser a JWT here would throw away the reason it
    /// exists.
    /// </remarks>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] SignupLoginProxyRequest? request, [FromQuery] string? appId)
    {
        if (!TryResolveAppId(appId, out var resolvedAppId, out var failure)) return failure!;
        if (request is null) return MissingBody();

        var result = await _registration.LoginAsync(
            request.Username ?? string.Empty,
            request.Email ?? string.Empty,
            request.Password ?? string.Empty,
            resolvedAppId);

        if (!result.Succeeded || result.Response is null)
        {
            return Ok(new SignupLoginResultModel
            {
                Success = false,
                Message = result.ErrorMessage is { Length: > 0 } m
                    ? m
                    : "Login failed after registration. Please try logging in manually.",
                ErrorCode = "login_failed"
            });
        }

        return Ok(new SignupLoginResultModel
        {
            Success = true,
            UserId = result.Response.UserId,
            RequiresDisclaimerAcceptance = result.Response.RequiresDisclaimerAcceptance
        });
    }

    /// <summary>
    /// POST /api/wildwood-regsub/link-transaction — attach the plan's payment to the account that
    /// now exists. The card was taken before there was an account (pay-first), so the transaction
    /// belongs to nobody until this runs.
    /// </summary>
    [HttpPost("link-transaction")]
    public async Task<IActionResult> LinkTransaction([FromBody] SignupLinkTransactionProxyRequest? request)
    {
        if (!TryRequireSession(out var failure)) return failure!;
        if (request is null) return MissingBody();

        var linked = await _registration.LinkTransactionToUserAsync(
            request.ExternalTransactionId, request.UserId);

        // Never an error status: the money is already taken and the account already exists, so a
        // failed link is something to repair, not something to fail a signup over.
        return Ok(new SignupLinkTransactionResultModel { Success = linked });
    }

    /// <summary>
    /// POST /api/wildwood-regsub/subscribe — start the plan the signup chose, as the signed-in
    /// user. A refusal is 200 with the structured result, as everywhere else here.
    /// </summary>
    /// <remarks>
    /// A registration token that carried a plan has ALREADY subscribed the account; subscribing
    /// over it replaces that subscription. The caller is the one that knows, so this route
    /// subscribes whatever it is asked to and the signup driver simply does not ask when a grant
    /// is in play.
    /// </remarks>
    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] SignupSubscribeProxyRequest? request, [FromQuery] string? appId)
    {
        if (!TryBegin(appId, out var resolvedAppId, out var failure)) return failure!;
        if (request is null) return MissingBody();

        return Ok(await _appTiers.SubscribeToTierAsync(
            resolvedAppId, request.TierId, request.PricingId, request.PaymentTransactionId));
    }

    #endregion

    #region Signup: the disclaimer gate

    /// <summary>
    /// GET /api/wildwood-regsub/disclaimers/pending — what the freshly signed-in account still has
    /// to accept. Session-scoped: these are the CALLER's pending disclaimers.
    /// </summary>
    [HttpGet("disclaimers/pending")]
    public async Task<IActionResult> PendingDisclaimers([FromQuery] string? appId)
    {
        if (!TryBegin(appId, out var resolvedAppId, out var failure)) return failure!;

        var pending = await _disclaimers.GetPendingDisclaimersAsync(resolvedAppId, null, "registration");

        // An unreadable list is answered as "nothing pending", matching the gate's shipped
        // fail-open behaviour: a signup that has already created an account and taken a card must
        // not dead-end because a disclaimer list did not load.
        return Ok(pending ?? new PendingDisclaimersResponse());
    }

    /// <summary>
    /// POST /api/wildwood-regsub/disclaimers/accept — record the acceptances in one call.
    /// </summary>
    [HttpPost("disclaimers/accept")]
    public async Task<IActionResult> AcceptDisclaimers(
        [FromBody] SignupDisclaimerAcceptProxyRequest? request, [FromQuery] string? appId)
    {
        if (!TryBegin(appId, out var resolvedAppId, out var failure)) return failure!;
        if (request is null) return MissingBody();

        var result = await _disclaimers.AcceptDisclaimersAsync(resolvedAppId, request.Acceptances);
        return Ok(new { success = result.Succeeded, errorMessage = result.Message });
    }

    #endregion

    #region Payment

    /// <summary>
    /// POST /api/wildwood-regsub/payment/initiate — the payment intent, for a
    /// <c>&lt;vc:payment proxy-base-url="/api/wildwood-regsub/payment" /&gt;</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Anonymous, and that is the pay-first order, not an oversight.</b> The plan's card is
    /// taken BEFORE the account exists — that is what stops a declined card leaving a half-made
    /// account on a plan nobody paid for — so there is no session to require at this point, and
    /// the payment is tied to the visitor by <c>customerEmail</c> until
    /// <c>link-transaction</c> attaches it to the new user. WildwoodAPI applies its own rules to
    /// the call; this route adds no authority the browser did not already have, and forwards the
    /// bound request rather than the raw body.
    /// </para>
    /// <para>
    /// Existing hosts are untouched: <c>PaymentViewComponent</c> still defaults to
    /// <c>/api/wildwood-payment</c>, the host-supplied proxy. Pointing its
    /// <c>proxy-base-url</c> here is what the signup view does, and what any other page may do.
    /// </para>
    /// </remarks>
    [HttpPost("payment/initiate")]
    public async Task<IActionResult> InitiatePayment([FromBody] InitiatePaymentRequest? request, [FromQuery] string? appId)
    {
        if (!TryResolveAppId(appId ?? request?.AppId, out var resolvedAppId, out var failure)) return failure!;
        if (request is null) return MissingBody();

        // The app is the configured one, never the body's: a client-named app would let this page
        // initiate a payment against any app in the company.
        request.AppId = resolvedAppId;

        return Ok(await _payments.InitiatePaymentAsync(request));
    }

    /// <summary>
    /// POST /api/wildwood-regsub/payment/confirm — verify with the provider what the browser just
    /// confirmed. Anonymous for the same reason <c>payment/initiate</c> is.
    /// </summary>
    [HttpPost("payment/confirm")]
    public async Task<IActionResult> ConfirmPayment([FromBody] SignupConfirmPaymentProxyRequest? request)
    {
        if (request is null || !(request.PaymentIntentId is { Length: > 0 })) return MissingBody();

        return Ok(await _payments.ConfirmPaymentAsync(
            request.PaymentIntentId!, (PaymentProviderType)request.ProviderType));
    }

    #endregion
}
