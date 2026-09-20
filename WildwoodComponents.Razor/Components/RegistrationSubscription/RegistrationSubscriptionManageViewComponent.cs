using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Extensions;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Components.RegistrationSubscription;

/// <summary>
/// The manage surface of the registration + subscription component: what a customer already pays
/// for, and every way of changing it.
/// </summary>
/// <remarks>
/// <para>
/// Ported from packages/wildwood-react/src/components/registrationSubscription/views/ManageView.tsx
/// by way of the Blazor <c>RegistrationSubscriptionManage</c>, and named after them so the same
/// surface is called the same thing on every stack.
/// </para>
/// <para>
/// <b>It is a COMPOSITION, not a new surface.</b> The six panels are the platform's own
/// (<c>&lt;vc:subscription-status-panel /&gt;</c> and its five siblings), so a site swapping a
/// hand-built plan page for this keeps its markup and its locators, and every panel action that
/// <c>subscription-admin.js</c> already wires — cancel, feature overrides, usage limits, the pack
/// rows' subscribe / cancel / reactivate — keeps working unchanged. The root therefore carries the
/// <c>ww-subscription-admin-component</c> class as well as <c>data-ww-view="manage"</c>.
/// </para>
/// <para>
/// <b>What is new is the middle.</b> The plan change runs through the shared
/// <c>wwwroot/js/regsub-planchange.js</c>: a preview is confirmed in EVERY layout, and a prorated
/// charge the bank wants to see is authenticated on the card ALREADY ON FILE and the parked change
/// completed, instead of being refused. <c>data-ww-view="manage"</c> is also what tells
/// <c>subscription-admin.js</c> to leave the plan change alone here, so one root never gets two
/// drivers.
/// </para>
/// <para>
/// <b>There is no plan-change payment form</b>, by standing decision. A change that needs a NEW
/// card says so and raises <c>ww-regsub-payment-required</c> with the tier, the pricing model, the
/// plan's price as the server formatted it and the trial days — so a host that wants to collect
/// one mounts <c>&lt;vc:payment /&gt;</c> itself.
/// </para>
/// </remarks>
public class RegistrationSubscriptionManageViewComponent : ViewComponent
{
    private readonly IWildwoodAppTierService _appTiers;
    private readonly IWildwoodPaymentService _payments;
    private readonly IWildwoodPublicCatalogService _catalog;
    private readonly WildwoodComponentsRazorOptions _options;
    private readonly ILogger<RegistrationSubscriptionManageViewComponent> _logger;

    public RegistrationSubscriptionManageViewComponent(
        IWildwoodAppTierService appTiers,
        IWildwoodPaymentService payments,
        IWildwoodPublicCatalogService catalog,
        WildwoodComponentsRazorOptions options,
        ILogger<RegistrationSubscriptionManageViewComponent> logger)
    {
        _appTiers = appTiers;
        _payments = payments;
        _catalog = catalog;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Renders the manage surface for whichever subscription the scope names.
    /// </summary>
    /// <param name="appId">
    /// The app being managed. Defaults to the one configured in <c>AddWildwoodComponentsRazor</c>.
    /// </param>
    /// <param name="layout">
    /// <c>tabs</c> (default) or <c>stacked</c>. A string, not an enum: a Razor tag-helper attribute
    /// whose property is not a string compiles as a C# expression, so an enum would force every
    /// host to write <c>layout="@ManageLayout.Stacked"</c>.
    /// </param>
    /// <param name="sections">
    /// Which panels to render, comma-separated and IN ORDER:
    /// <c>subscription,plans,features,addOns,usage,overrides</c>. A section left out is gone, not
    /// hidden behind a tab. Omit for all of them in that order.
    /// </param>
    /// <param name="showStatusAboveTabs">Lift the subscription card out of the section list.</param>
    /// <param name="isAdmin">
    /// Whether the viewer may see the admin controls. The overrides panel is admin-only, and the
    /// server enforces its own rules regardless of this.
    /// </param>
    /// <param name="userId">An admin managing ONE user's subscription.</param>
    /// <param name="companyId">An admin managing a COMPANY's subscription (in company tracking).</param>
    /// <param name="allowPackSelfService">
    /// Offer "Add packs" on the packs panel and render the pack picker. The signed-in user's own
    /// scope only: the shipped proxy acts as that user and has no admin scope.
    /// </param>
    /// <param name="allowCancel">Offer cancelling the subscription and its packs.</param>
    /// <param name="showAddOns">Show the packs section at all.</param>
    /// <param name="currency">
    /// Display currency override. Left out, the app's public catalog names one; failing that, USD.
    /// </param>
    /// <param name="labels">
    /// Copy the host may replace. An instance the host only partially set keeps the shipped word
    /// for every string it left alone.
    /// </param>
    /// <param name="contactUrl">Where a plan that names no price sends the visitor.</param>
    /// <param name="returnUrl">
    /// Carried on the root for a host that reads it. This view never navigates to it.
    /// </param>
    /// <param name="paymentRequiredText">
    /// Replaces "This change needs a payment method." — the sentence shown when a change needs a
    /// card this package does not collect.
    /// </param>
    /// <param name="proxyBaseUrl">
    /// The HOST's app-tier proxy, for the admin- and company-scoped writes that have no shipped
    /// route (feature overrides, usage limits, another user's plan).
    /// </param>
    /// <param name="regSubProxyUrl">Where the shipped proxy controller is mounted.</param>
    /// <param name="showBillingToggle">Show the plans panel's monthly/annual toggle.</param>
    /// <param name="componentId">A stable id for the root element. Generated when omitted.</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string? appId = null,
        string layout = "tabs",
        string? sections = null,
        bool showStatusAboveTabs = false,
        bool isAdmin = false,
        string? userId = null,
        string? companyId = null,
        bool allowPackSelfService = false,
        bool allowCancel = true,
        bool showAddOns = true,
        string? currency = null,
        RegistrationSubscriptionLabels? labels = null,
        string? contactUrl = null,
        string? returnUrl = null,
        string? paymentRequiredText = null,
        string proxyBaseUrl = "/api/wildwood-app-tiers",
        string regSubProxyUrl = "/api/wildwood-regsub",
        bool showBillingToggle = true,
        string? componentId = null)
    {
        var resolvedAppId = appId is { Length: > 0 } ? appId : _options.AppId?.Trim() ?? string.Empty;

        var model = new RegistrationSubscriptionManageViewModel
        {
            AppId = resolvedAppId,
            ProxyBaseUrl = (proxyBaseUrl ?? string.Empty).TrimEnd('/'),
            RegSubProxyUrl = (regSubProxyUrl ?? string.Empty).TrimEnd('/'),
            Labels = RegistrationSubscriptionLabels.Resolve(labels),
            Layout = RegistrationSubscriptionManageDecisions.ParseLayout(layout),
            RequestedSections = RegistrationSubscriptionManageDecisions.ParseSections(sections),
            ShowStatusAboveTabs = showStatusAboveTabs,
            IsAdmin = isAdmin,
            UserId = userId,
            CompanyId = companyId,
            AllowPackSelfService = allowPackSelfService,
            AllowCancel = allowCancel,
            ShowAddOns = showAddOns,
            ShowBillingToggle = showBillingToggle,
            ContactUrl = contactUrl,
            ReturnUrl = returnUrl,
            PaymentRequiredText = paymentRequiredText,
            Currency = currency is { Length: > 0 } ? currency : "USD"
        };

        if (componentId is { Length: > 0 }) model.ComponentId = componentId;

        if (resolvedAppId.Length == 0)
        {
            // Nothing to manage: the notice, never a set of empty panels. The same answer React
            // gives when `appId` resolves to an empty string.
            _logger.LogWarning(
                "The registration-subscription manage component has no appId — set one on the tag helper or in AddWildwoodComponentsRazor.");
            return View(model);
        }

        model.IsCompanyMode = await LoadIsCompanyModeAsync(resolvedAppId);
        model.Subscription = await LoadSubscriptionAsync(model, resolvedAppId);

        if (isAdmin) model.OverrideCount = await LoadOverrideCountAsync(model, resolvedAppId);

        // The packs are read HERE rather than inside the panel so the picker and the rows agree
        // about what is owned; the panel is handed the same two lists. A host that left the packs
        // section out is not charged two calls for it.
        if (model.VisibleSections.Contains(ManageSection.AddOns)) await LoadAddOnsAsync(model, resolvedAppId);

        // Only the signed-in user's own scope can buy a pack or answer a bank challenge, so the
        // key is only worth reading there.
        if (model.IsSelfScope) await LoadPublishableKeyAsync(model, resolvedAppId);

        if (!(currency is { Length: > 0 })) model.Currency = await ResolveCurrencyAsync(resolvedAppId);

        return View(model);
    }

    #region Loads

    private async Task<bool> LoadIsCompanyModeAsync(string appId)
    {
        try
        {
            var mode = await _appTiers.GetTrackingModeAsync(appId);
            return string.Equals(mode, "Company", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            // Per-user is the platform default, and the panels read the caller's own subscription
            // in that mode — the safe answer for a viewer managing their own plan.
            _logger.LogError(ex, "Failed to load the tracking mode for app {AppId}", appId);
            return false;
        }
    }

    private async Task<Shared.Models.UserTierSubscriptionModel?> LoadSubscriptionAsync(
        RegistrationSubscriptionManageViewModel model, string appId)
    {
        try
        {
            if (model.UseCompanyScope) return await _appTiers.GetCompanySubscriptionAsync(appId, model.CompanyId!);
            if (model.UseUserScope) return await _appTiers.GetUserSubscriptionAsync(appId, model.UserId!);
            return await _appTiers.GetMySubscriptionAsync(appId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the subscription for app {AppId}", appId);
            return null;
        }
    }

    private async Task<int> LoadOverrideCountAsync(RegistrationSubscriptionManageViewModel model, string appId)
    {
        try
        {
            var overrides = await _appTiers.GetFeatureOverridesAsync(appId, model.UseUserScope ? model.UserId : null);
            return overrides.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the feature overrides for app {AppId}", appId);
            return 0;
        }
    }

    private async Task LoadAddOnsAsync(RegistrationSubscriptionManageViewModel model, string appId)
    {
        try
        {
            if (model.UseCompanyScope)
            {
                model.ActiveAddOns = await _appTiers.GetCompanyAddOnSubscriptionsAsync(appId, model.CompanyId!);
            }
            else if (model.UseUserScope)
            {
                model.ActiveAddOns = await _appTiers.GetUserAddOnsAsync(appId, model.UserId!);
            }
            else
            {
                model.ActiveAddOns = await _appTiers.GetMyAddOnsAsync(appId);
            }

            model.AvailableAddOns = model.IsAdmin
                ? await _appTiers.GetAllAddOnsAsync(appId)
                : await _appTiers.GetAvailableAddOnsAsync(appId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the add-ons for app {AppId}", appId);
        }
    }

    /// <summary>
    /// The publishable key the 3-D Secure confirmation and the pack picker's card form need,
    /// resolved by the SAME rule the signup view uses.
    /// </summary>
    private async Task LoadPublishableKeyAsync(RegistrationSubscriptionManageViewModel model, string appId)
    {
        try
        {
            var providers = await _payments.GetAvailableProvidersAsync(appId);
            if (providers is null) return;

            var chosen = RegistrationSubscriptionSignupViewComponent.PickKeyedProvider(providers);
            if (chosen is null) return;

            model.PublishableKey = chosen.PublishableKey;
            model.PaymentProviderId = chosen.Id;
        }
        catch (Exception ex)
        {
            // Not fatal: every path that does not need a bank challenge still works, and one that
            // does says the charge could not be confirmed rather than failing silently.
            _logger.LogWarning(ex, "Could not read the payment providers for app {AppId}", appId);
        }
    }

    /// <summary>
    /// The currency when the host named none: the app's own catalog currency (a 60-second cache
    /// the pricing and signup views already warm), else USD. React reads the first tier's
    /// currency for the same reason — a page quoting one currency and the panels another is worse
    /// than either.
    /// </summary>
    private async Task<string> ResolveCurrencyAsync(string appId)
    {
        try
        {
            var catalog = await _catalog.GetAsync(appId);
            var resolved = RegistrationSubscriptionPricingDecisions.ResolveDisplayCurrency(null, catalog);
            return resolved is { Length: > 0 } ? resolved : "USD";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the catalog currency for app {AppId}", appId);
            return "USD";
        }
    }

    #endregion
}
