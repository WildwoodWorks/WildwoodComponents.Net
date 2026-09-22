using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Extensions;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Components.RegistrationSubscription;

/// <summary>
/// The signup surface of the registration + subscription component: an account, a plan, packs and
/// a card, in the order that keeps them consistent.
/// </summary>
/// <remarks>
/// <para>
/// Ported from packages/wildwood-react/src/components/registrationSubscription/views/SignupView.tsx
/// by way of the Blazor <c>RegistrationSubscriptionSignup</c>, and named after them so the same
/// surface is called the same thing on every stack.
/// </para>
/// <para>
/// <b>PAY-FIRST.</b> The plan's card is taken BEFORE the account exists, because a declined card
/// then leaves nothing behind rather than an account sitting on a plan nobody paid for — and
/// because a card already on file is what lets the pack checkout ask for one card for a basket of
/// any size. Packs are bought AFTER the sign-in, as the user. This replaces the old Razor signup's
/// plan-then-register-then-pay order, whose paid step was a raw card form with a permanently
/// disabled button; there are no card-number fields anywhere in this view, and a source guard
/// greps for one.
/// </para>
/// <para>
/// <b>Everything decidable before the first byte is decided here</b>: the app's LIVE registration
/// mode (never cached — an operator who closes sign-up is obeyed by the next visitor), the public
/// catalog and its prices, the plan a link preselected, the packs, the password policy, the
/// payment provider's publishable key, and every visible string. A closed sign-up therefore never
/// flashes a form, and no price is ever formatted in the browser.
/// </para>
/// <para>
/// <b>Nothing is host-supplied.</b> Register, sign in, link the plan's payment, subscribe, the
/// disclaimer gate, the pack checkout and the payment intent all go to the shipped
/// <c>/api/wildwood-regsub</c> proxy. A consuming app writes no routes for this view.
/// </para>
/// </remarks>
public class RegistrationSubscriptionSignupViewComponent : ViewComponent
{
    private readonly IWildwoodPublicCatalogService _catalog;
    private readonly IWildwoodRegistrationService _registration;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly IWildwoodPaymentService _payments;
    private readonly WildwoodComponentsRazorOptions _options;
    private readonly ILogger<RegistrationSubscriptionSignupViewComponent> _logger;

    public RegistrationSubscriptionSignupViewComponent(
        IWildwoodPublicCatalogService catalog,
        IWildwoodRegistrationService registration,
        IWildwoodSessionManager sessionManager,
        IWildwoodPaymentService payments,
        WildwoodComponentsRazorOptions options,
        ILogger<RegistrationSubscriptionSignupViewComponent> logger)
    {
        _catalog = catalog;
        _registration = registration;
        _sessionManager = sessionManager;
        _payments = payments;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Renders the signup flow against the app's live registration mode and catalog.
    /// </summary>
    /// <param name="appId">
    /// The app to sign up for. Defaults to the one configured in <c>AddWildwoodComponentsRazor</c>.
    /// </param>
    /// <param name="preSelectedTierId">
    /// The plan the visitor arrived wanting. Matched case-insensitively against the catalog; a
    /// plan the app does not sell is IGNORED rather than honoured, because the link is stale or
    /// hand-edited. Falls back to <c>?tier=</c>.
    /// </param>
    /// <param name="preSelectedPricingId">
    /// The pricing option within that plan. Falls back to <c>?pricing=</c>, and is dropped with
    /// the plan when the plan is not sold.
    /// </param>
    /// <param name="preSelectedAddOnIds">
    /// Packs to buy, comma-separated. Vetted against the catalog, de-duplicated and capped at 25 —
    /// a signup link is not a shopping cart. Falls back to <c>?addons=</c>. A comma-separated
    /// string rather than a list for the same reason the mode attributes are strings.
    /// </param>
    /// <param name="registrationToken">
    /// A registration token to redeem. Falls back to <c>?token=</c>, then <c>?invite=</c>.
    /// </param>
    /// <param name="prefillEmail">
    /// An email to put in the form as BOTH the username and the email. Falls back to
    /// <c>?email=</c>.
    /// </param>
    /// <param name="planSelection">
    /// <c>choose</c> (default) shows the plan grid; <c>skip</c> takes the app's default plan and
    /// removes the step. A string, not an enum: a Razor tag-helper attribute whose property is not
    /// a string is compiled as a C# expression, so an enum would force every host to write
    /// <c>plan-selection="@SignupPlanSelection.Skip"</c>.
    /// </param>
    /// <param name="planDefault">
    /// <c>free</c> opens the plan grid highlighted on the app's free plan — a suggestion the
    /// visitor still confirms, not a choice already made; <c>none</c> (default) opens it on
    /// nothing. Ignored once a link or an invite has chosen. A string, not an enum, for the same
    /// reason the other mode attributes are strings.
    /// </param>
    /// <param name="packSelection">
    /// <c>choose</c> offers the pack step; <c>none</c> (default) removes it. <b>It removes the
    /// STEP only</b> — packs a signup link chose are still bought, because the visitor picked them
    /// on the pricing page.
    /// </param>
    /// <param name="tokenMode">
    /// <c>required</c> is invite redemption: the token step is the only way in, the plan and the
    /// packs come from the token, and it overrides a CLOSED configuration, because the server
    /// validates the invite itself.
    /// </param>
    /// <param name="requireBillingAddress">Collect a billing address with the plan's card.</param>
    /// <param name="returnUrl">
    /// Carried through the flow and handed to whoever listens for the completion. Never navigated
    /// to by this component.
    /// </param>
    /// <param name="completeUrl">
    /// Where "Get Started" goes once the signup is done — the Razor analog of React's
    /// <c>onSignupComplete</c> navigating. The <c>ww-regsub-signup-complete</c> event is raised
    /// first either way, and a listener that calls <c>preventDefault()</c> stops the navigation.
    /// </param>
    /// <param name="alreadySignedInUrl">
    /// Where a visitor who already has a session is sent. Omit it to show the notice and raise
    /// <c>ww-regsub-already-signed-in</c> instead.
    /// </param>
    /// <param name="contactUrl">Where the closed notice's "Contact us" points.</param>
    /// <param name="currency">
    /// Display currency override. Rarely needed: the server names the currency its catalog is
    /// quoted in.
    /// </param>
    /// <param name="labels">
    /// Copy the host may replace. An instance the host only partially set keeps the shipped word
    /// for every string it left alone.
    /// </param>
    /// <param name="closedText">
    /// Replaces "Registration is closed". The Razor analog of React's <c>renderClosed</c>: a
    /// server-rendered component cannot take a markup callback, so the sentence is a string.
    /// </param>
    /// <param name="regSubProxyUrl">
    /// Where the shipped <c>WildwoodRegistrationSubscriptionProxyController</c> is mounted.
    /// </param>
    /// <param name="showFeatureComparison">Show each plan's feature list in the plan grid.</param>
    /// <param name="showLimits">Show each plan's usage limits in the plan grid.</param>
    /// <param name="componentId">A stable id for the root element. Generated when omitted.</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string? appId = null,
        string? preSelectedTierId = null,
        string? preSelectedPricingId = null,
        string? preSelectedAddOnIds = null,
        string? registrationToken = null,
        string? prefillEmail = null,
        string planSelection = "choose",
        string planDefault = "none",
        string packSelection = "none",
        string tokenMode = "auto",
        bool requireBillingAddress = false,
        string? returnUrl = null,
        string? completeUrl = null,
        string? alreadySignedInUrl = null,
        string? contactUrl = null,
        string? currency = null,
        RegistrationSubscriptionLabels? labels = null,
        string? closedText = null,
        string regSubProxyUrl = "/api/wildwood-regsub",
        bool showFeatureComparison = true,
        bool showLimits = true,
        string? componentId = null)
    {
        var resolvedAppId = appId is { Length: > 0 } ? appId : _options.AppId?.Trim() ?? string.Empty;
        var resolvedTokenMode = RegistrationSubscriptionSignupDecisions.ParseTokenMode(tokenMode);

        var model = new RegistrationSubscriptionSignupViewModel
        {
            AppId = resolvedAppId,
            ProxyUrl = (regSubProxyUrl ?? string.Empty).TrimEnd('/'),
            Labels = RegistrationSubscriptionLabels.Resolve(labels),
            TokenMode = resolvedTokenMode,
            PlanSelection = RegistrationSubscriptionSignupDecisions.ParsePlanSelection(planSelection),
            PlanDefault = RegistrationSubscriptionSignupDecisions.ParsePlanDefault(planDefault),
            PackSelection = RegistrationSubscriptionSignupDecisions.ParsePackSelection(packSelection),
            RequireBillingAddress = requireBillingAddress,
            ReturnUrl = returnUrl,
            CompleteUrl = completeUrl,
            AlreadySignedInUrl = alreadySignedInUrl,
            ContactUrl = contactUrl,
            ClosedText = closedText,
            ShowFeatureComparison = showFeatureComparison,
            ShowLimits = showLimits,
            AlreadySignedIn = _sessionManager.IsAuthenticated
        };

        if (componentId is { Length: > 0 }) model.ComponentId = componentId;

        if (resolvedAppId.Length == 0)
        {
            // No app to sign up for: the unavailable panel, never a form. The same answer React
            // gives when `appId` resolves to an empty string.
            model.CatalogError = "An appId is required to load the public catalog.";
            model.Currency = RegistrationSubscriptionPricingDecisions.ResolveDisplayCurrency(currency, null);
            _logger.LogWarning(
                "The registration-subscription signup component has no appId — set one on the tag helper or in AddWildwoodComponentsRazor.");
            return View(model);
        }

        // The catalog and the registration mode are read together: the machine opens the form only
        // once both are in, exactly as the React hook does, and here both are in before the first
        // byte.
        var catalogTask = LoadCatalogAsync(resolvedAppId);
        var modeTask = LoadModeAsync(resolvedAppId, resolvedTokenMode);

        var (catalog, catalogError) = await catalogTask;
        model.Catalog = catalog;
        model.CatalogError = catalogError;
        model.Mode = await modeTask;
        model.Currency = RegistrationSubscriptionPricingDecisions.ResolveDisplayCurrency(currency, catalog);

        model.Params = RegistrationSubscriptionSignupDecisions.ResolveParams(
            catalog,
            ReadQuery(),
            preSelectedTierId,
            preSelectedPricingId,
            preSelectedAddOnIds,
            registrationToken,
            prefillEmail);

        model.PresetPlan = RegistrationSubscriptionSignupDecisions.ResolvePresetPlan(
            catalog,
            model.IsInvite,
            model.PlanSelection,
            model.Params.TierId,
            model.Params.PricingId);

        // Nothing below here is worth a round trip for a visitor who cannot use the form.
        if (!model.AlreadySignedIn && !model.Mode.Closed && !model.IsCatalogUnavailable)
        {
            model.PasswordRequirements = await LoadPasswordRequirementsAsync(resolvedAppId);
            await LoadPublishableKeyAsync(model, resolvedAppId);
            model.Disclaimers = await LoadDisclaimersAsync(resolvedAppId);
        }

        return View(model);
    }

    #region Loads

    private async Task<(PublicCatalog? Catalog, string? Error)> LoadCatalogAsync(string appId)
    {
        try
        {
            return (await _catalog.GetAsync(appId), null);
        }
        catch (Exception ex)
        {
            // A failure is NOT cached, so the Retry genuinely retries.
            _logger.LogWarning(ex, "Public catalog unavailable for app {AppId}", appId);
            return (null, ex.Message is { Length: > 0 } ? ex.Message : "Failed to load the catalog");
        }
    }

    /// <summary>
    /// The app's live registration mode. Invite redemption does not read the settings at all: the
    /// token is the way in and the server validates it, so a closed configuration does not block it.
    /// </summary>
    private async Task<SignupRegistrationMode.Result> LoadModeAsync(string appId, SignupTokenMode tokenMode)
    {
        if (tokenMode == SignupTokenMode.Required)
        {
            return SignupRegistrationMode.Resolve(null, SignupTokenMode.Required);
        }

        var settings = await _registration.GetSignupRegistrationSettingsAsync(appId);
        return SignupRegistrationMode.Resolve(settings, tokenMode);
    }

    /// <summary>
    /// The app's password policy in the server's own words — the SAME source
    /// <c>token-registration.js</c> reads, rather than a second copy of the rules in this view.
    /// </summary>
    private async Task<string?> LoadPasswordRequirementsAsync(string appId)
    {
        try
        {
            return await _registration.GetPasswordRequirementsAsync(appId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the password requirements for app {AppId}", appId);
            return null;
        }
    }

    /// <summary>
    /// The app's registration disclaimers, read from the anonymous
    /// <c>disclaimeracceptance/pending/{appId}?showOn=registration</c> route so they can be
    /// rendered as real markup before the account exists.
    /// </summary>
    /// <remarks>
    /// Rendering them here is what keeps a server-supplied string out of <c>innerHTML</c>: a
    /// disclaimer whose content format is HTML has to be rendered AS HTML, and only a Razor view
    /// may do that safely. The gate then shows only the ones the signed-in account is still asked
    /// for. An unreadable list is an empty one — the gate fails open, exactly as the shipped
    /// signup wizard's does, because a signup that has already created an account and taken a card
    /// must not dead-end on a list that did not load.
    /// </remarks>
    private async Task<List<PendingDisclaimerModel>> LoadDisclaimersAsync(string appId)
    {
        try
        {
            var pending = await _registration.GetRegistrationDisclaimersAsync(appId);
            return pending?.Disclaimers ?? new List<PendingDisclaimerModel>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the registration disclaimers for app {AppId}", appId);
            return new List<PendingDisclaimerModel>();
        }
    }

    /// <summary>
    /// The publishable key the pack checkout's card form and its 3-D Secure confirmations need,
    /// resolved by the rule React follows: the app's default provider, else one marked default,
    /// else the first enabled provider that actually has a key.
    /// </summary>
    private async Task LoadPublishableKeyAsync(RegistrationSubscriptionSignupViewModel model, string appId)
    {
        try
        {
            var providers = await _payments.GetAvailableProvidersAsync(appId);
            if (providers is null) return;

            var chosen = PickKeyedProvider(providers);
            if (chosen is null) return;

            model.PublishableKey = chosen.PublishableKey;
            model.PaymentProviderId = chosen.Id;
        }
        catch (Exception ex)
        {
            // Not fatal: without a key the pack checkout can still buy against a card on file,
            // and says so rather than failing silently.
            _logger.LogWarning(ex, "Could not read the payment providers for app {AppId}", appId);
        }
    }

    /// <summary>
    /// The app's payment provider with a publishable key: its default, else one marked default,
    /// else the first enabled one that has a key. <c>internal</c> rather than private because the
    /// manage view resolves the SAME key for its 3-D Secure confirmation and its pack card, and a
    /// second copy of the rule is a second answer to "which Stripe account is this?".
    /// </summary>
    internal static PaymentProviderDto? PickKeyedProvider(PlatformFilteredProvidersDto providers)
    {
        var fallbackDefault = providers.DefaultProvider;
        if (fallbackDefault is not null
            && fallbackDefault.IsEnabled
            && fallbackDefault.PublishableKey is { Length: > 0 })
        {
            return fallbackDefault;
        }

        PaymentProviderDto? first = null;
        foreach (var provider in providers.AvailableProviders)
        {
            if (provider is null || !provider.IsEnabled || !(provider.PublishableKey is { Length: > 0 })) continue;
            if (provider.IsDefault) return provider;
            first ??= provider;
        }

        return first;
    }

    #endregion

    /// <summary>
    /// The request's query, or null when there is no request (a unit test constructing the
    /// component by hand). The explicit parameters then stand alone.
    /// </summary>
    private QueryParameters? ReadQuery()
    {
        var request = ViewComponentContext?.ViewContext?.HttpContext?.Request;
        if (request is null) return null;

        return CatalogHelpers.AsSearchParams(request.QueryString.Value);
    }
}
