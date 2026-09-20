using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Components.RegistrationSubscription;

/// <summary>
/// The registration + subscription component: one tag helper over the three surfaces a customer
/// meets — the price list, the sign-up, and managing what they already pay for.
/// </summary>
/// <remarks>
/// <para>
/// Ported from React's <c>RegistrationAndSubscriptionComponent</c> (and the Blazor component of
/// the same name), which is a <c>view</c> switch and nothing else. This is the same: it holds no
/// state, reads nothing, and renders whichever of the three ViewComponents the <c>view</c>
/// attribute names, forwarding the parameters that apply to it.
/// </para>
/// <para>
/// A host may equally write <c>&lt;vc:registration-subscription-pricing /&gt;</c>,
/// <c>&lt;vc:registration-subscription-signup /&gt;</c> or
/// <c>&lt;vc:registration-subscription-manage /&gt;</c> directly. The shell exists for the page
/// that decides at run time — a route that shows pricing to a visitor and the manage view to a
/// subscriber — so the decision is one attribute rather than a branch in the markup.
/// </para>
/// <para>
/// <b>An unknown view is a developer mistake, and is reported as one.</b> In Development it
/// renders the view names in plain sight; anywhere else it renders NOTHING, because a production
/// page must not carry a message about a tag helper. Either way it logs a warning, so a
/// misconfiguration is visible in the logs of the environment where it was found.
/// </para>
/// </remarks>
public class RegistrationAndSubscriptionViewComponent : ViewComponent
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<RegistrationAndSubscriptionViewComponent> _logger;

    public RegistrationAndSubscriptionViewComponent(
        IWebHostEnvironment environment,
        ILogger<RegistrationAndSubscriptionViewComponent> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Renders one of the three views.
    /// </summary>
    /// <param name="view">
    /// <c>pricing</c>, <c>signup</c> or <c>manage</c>. Anything else is a developer mistake: see
    /// the remarks on the class.
    /// </param>
    /// <param name="appId">ALL views. Defaults to the configured <c>AppId</c>.</param>
    /// <param name="currency">ALL views. Display currency override.</param>
    /// <param name="labels">ALL views. Copy overrides; unset strings keep the shipped word.</param>
    /// <param name="contactUrl">ALL views. Where "Contact us" points.</param>
    /// <param name="returnUrl">
    /// <c>signup</c> and <c>manage</c>. Carried, never navigated to by the component.
    /// </param>
    /// <param name="componentId">ALL views. A stable id for the root element.</param>
    /// <param name="showPlans"><c>pricing</c>. Render the plan grid.</param>
    /// <param name="showAddOns">
    /// <c>pricing</c> (render the pack grid, default off) and <c>manage</c> (offer the packs
    /// section, default on). Not used by <c>signup</c>, which has <c>packSelection</c>.
    /// </param>
    /// <param name="offerFreeTierChoice"><c>pricing</c>. False hides every free plan.</param>
    /// <param name="packSelection">
    /// <c>pricing</c> (<c>none</c>|<c>multi</c>) and <c>signup</c> (<c>none</c>|<c>choose</c>).
    /// </param>
    /// <param name="addOnGroups"><c>pricing</c>. Headings matched on the pack's category.</param>
    /// <param name="showBillingToggle">
    /// <c>pricing</c> and <c>manage</c> (its plans panel). Shown only when some plan is priced by
    /// the year.
    /// </param>
    /// <param name="defaultBilling"><c>pricing</c>. <c>monthly</c> or <c>annual</c>.</param>
    /// <param name="showFeatureComparison"><c>pricing</c> and <c>signup</c>. Plan-card features.</param>
    /// <param name="showLimits"><c>pricing</c> and <c>signup</c>. Plan-card usage limits.</param>
    /// <param name="highlightTierId"><c>pricing</c>. Marks one plan as pre-selected.</param>
    /// <param name="includeJsonLd"><c>pricing</c>. Emit a schema.org offers graph.</param>
    /// <param name="jsonLdUrl"><c>pricing</c>. The canonical URL for that graph.</param>
    /// <param name="selectUrl"><c>pricing</c>. Where a selection navigates.</param>
    /// <param name="unavailableText"><c>pricing</c>. Replaces "Pricing is unavailable right now".</param>
    /// <param name="preSelectedTierId"><c>signup</c>. The plan the visitor arrived wanting.</param>
    /// <param name="preSelectedPricingId"><c>signup</c>. Its pricing option.</param>
    /// <param name="preSelectedAddOnIds"><c>signup</c>. Comma-separated packs to buy.</param>
    /// <param name="registrationToken"><c>signup</c>. A token to redeem.</param>
    /// <param name="prefillEmail"><c>signup</c>. Fills the email and the username.</param>
    /// <param name="planSelection"><c>signup</c>. <c>choose</c> or <c>skip</c>.</param>
    /// <param name="tokenMode"><c>signup</c>. <c>auto</c> or <c>required</c> (invite redemption).</param>
    /// <param name="requireBillingAddress"><c>signup</c>. Collect an address with the card.</param>
    /// <param name="completeUrl"><c>signup</c>. Where "Get Started" goes.</param>
    /// <param name="alreadySignedInUrl"><c>signup</c>. Where a signed-in visitor goes.</param>
    /// <param name="closedText"><c>signup</c>. Replaces "Registration is closed".</param>
    /// <param name="layout"><c>manage</c>. <c>tabs</c> or <c>stacked</c>.</param>
    /// <param name="sections"><c>manage</c>. Which panels, comma-separated and in order.</param>
    /// <param name="showStatusAboveTabs"><c>manage</c>. Lift the subscription card out.</param>
    /// <param name="isAdmin"><c>manage</c>. Show the admin controls and the overrides panel.</param>
    /// <param name="userId"><c>manage</c>. An admin managing one user's subscription.</param>
    /// <param name="companyId"><c>manage</c>. An admin managing a company's subscription.</param>
    /// <param name="allowPackSelfService"><c>manage</c>. Offer "Add packs" and the picker.</param>
    /// <param name="allowCancel"><c>manage</c>. Offer cancelling the plan and its packs.</param>
    /// <param name="paymentRequiredText">
    /// <c>manage</c>. Replaces "This change needs a payment method."
    /// </param>
    /// <param name="proxyBaseUrl"><c>manage</c>. The HOST's app-tier proxy.</param>
    /// <param name="regSubProxyUrl">
    /// <c>signup</c> and <c>manage</c>. Where the shipped proxy controller is mounted.
    /// </param>
    public IViewComponentResult Invoke(
        string view = "pricing",
        string? appId = null,
        string? currency = null,
        RegistrationSubscriptionLabels? labels = null,
        string? contactUrl = null,
        string? returnUrl = null,
        string? componentId = null,

        // Pricing
        bool showPlans = true,
        bool showAddOns = false,
        bool offerFreeTierChoice = true,
        string packSelection = "none",
        IReadOnlyList<AddOnGroup>? addOnGroups = null,
        bool showBillingToggle = true,
        string defaultBilling = "monthly",
        bool showFeatureComparison = true,
        bool showLimits = true,
        string? highlightTierId = null,
        bool includeJsonLd = false,
        string? jsonLdUrl = null,
        string? selectUrl = null,
        string? unavailableText = null,

        // Signup
        string? preSelectedTierId = null,
        string? preSelectedPricingId = null,
        string? preSelectedAddOnIds = null,
        string? registrationToken = null,
        string? prefillEmail = null,
        string planSelection = "choose",
        string tokenMode = "auto",
        bool requireBillingAddress = false,
        string? completeUrl = null,
        string? alreadySignedInUrl = null,
        string? closedText = null,

        // Manage
        string layout = "tabs",
        string? sections = null,
        bool showStatusAboveTabs = false,
        bool isAdmin = false,
        string? userId = null,
        string? companyId = null,
        bool allowPackSelfService = false,
        bool allowCancel = true,
        string? paymentRequiredText = null,
        string proxyBaseUrl = "/api/wildwood-app-tiers",
        string regSubProxyUrl = "/api/wildwood-regsub")
    {
        var resolved = RegistrationAndSubscriptionShell.ParseView(view);

        if (resolved is null)
        {
            _logger.LogWarning(
                "<vc:registration-and-subscription> was given view=\"{View}\"; it renders \"pricing\", \"signup\" or \"manage\".",
                view);
        }

        var model = new RegistrationAndSubscriptionViewModel
        {
            View = resolved ?? RegistrationSubscriptionView.Pricing,
            IsKnownView = resolved is not null,
            RequestedView = view,

            // Development sees the mistake; production sees nothing at all, because a page a
            // customer reads is no place for a message about a tag helper.
            ShowDeveloperMessage = resolved is null && _environment.IsDevelopment(),
            DeveloperMessage = RegistrationAndSubscriptionShell.UnknownViewMessage(view),

            AppId = appId,
            Currency = currency,
            Labels = labels,
            ContactUrl = contactUrl,
            ReturnUrl = returnUrl,
            ComponentId = componentId,

            ShowPlans = showPlans,
            ShowAddOns = showAddOns,
            OfferFreeTierChoice = offerFreeTierChoice,
            PackSelection = packSelection,
            AddOnGroups = addOnGroups,
            ShowBillingToggle = showBillingToggle,
            DefaultBilling = defaultBilling,
            ShowFeatureComparison = showFeatureComparison,
            ShowLimits = showLimits,
            HighlightTierId = highlightTierId,
            IncludeJsonLd = includeJsonLd,
            JsonLdUrl = jsonLdUrl,
            SelectUrl = selectUrl,
            UnavailableText = unavailableText,

            PreSelectedTierId = preSelectedTierId,
            PreSelectedPricingId = preSelectedPricingId,
            PreSelectedAddOnIds = preSelectedAddOnIds,
            RegistrationToken = registrationToken,
            PrefillEmail = prefillEmail,
            PlanSelection = planSelection,
            TokenMode = tokenMode,
            RequireBillingAddress = requireBillingAddress,
            CompleteUrl = completeUrl,
            AlreadySignedInUrl = alreadySignedInUrl,
            ClosedText = closedText,

            Layout = layout,
            Sections = sections,
            ShowStatusAboveTabs = showStatusAboveTabs,
            IsAdmin = isAdmin,
            UserId = userId,
            CompanyId = companyId,
            AllowPackSelfService = allowPackSelfService,
            AllowCancel = allowCancel,
            PaymentRequiredText = paymentRequiredText,
            ProxyBaseUrl = proxyBaseUrl,
            RegSubProxyUrl = regSubProxyUrl
        };

        return View(model);
    }
}
