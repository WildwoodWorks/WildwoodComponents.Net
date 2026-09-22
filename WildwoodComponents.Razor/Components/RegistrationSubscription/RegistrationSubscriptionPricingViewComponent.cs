using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Extensions;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Components.RegistrationSubscription;

/// <summary>
/// The pricing surface of the registration + subscription component: what the app sells, at the
/// price the server is quoting right now.
/// </summary>
/// <remarks>
/// <para>
/// Ported from packages/wildwood-react/src/components/registrationSubscription/views/PricingView.tsx
/// by way of the Blazor <c>RegistrationSubscriptionPricing</c>, and named after them so the same
/// surface is called the same thing on every stack.
/// </para>
/// <para>
/// Razor is server-rendered, which is what React reaches for with <c>initialCatalog</c>: the
/// catalog is READ HERE and the page ships with real prices on the first paint, so there is no
/// skeleton, no hydration flash and no client round trip to quote a plan. The other side of that
/// coin is that a failed load has to be decided here too — the component then renders "Pricing is
/// unavailable right now" and a Retry, and NO price survives with it. There is no fallback price,
/// no remembered price and no "from" price anywhere in this component, and a source guard greps
/// the view and the script for one.
/// </para>
/// <para>
/// Nothing navigates on its own. A click raises the bubbling, cancelable <c>ww-regsub-select</c>
/// event (the Razor analog of React's <c>onSelect</c>, per the run's Razor decision that every
/// <c>on*</c> prop becomes an event plus a URL parameter); when <c>select-url</c> is configured and
/// no listener called <c>preventDefault()</c>, the browser then goes there with the selection in
/// the query.
/// </para>
/// </remarks>
public class RegistrationSubscriptionPricingViewComponent : ViewComponent
{
    private readonly IWildwoodPublicCatalogService _catalog;
    private readonly WildwoodComponentsRazorOptions _options;
    private readonly ILogger<RegistrationSubscriptionPricingViewComponent> _logger;

    public RegistrationSubscriptionPricingViewComponent(
        IWildwoodPublicCatalogService catalog,
        WildwoodComponentsRazorOptions options,
        ILogger<RegistrationSubscriptionPricingViewComponent> logger)
    {
        _catalog = catalog;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Renders the pricing surface from the live public catalog.
    /// </summary>
    /// <param name="appId">
    /// The app whose catalog to show. Defaults to the one configured in
    /// <c>AddWildwoodComponentsRazor</c>.
    /// </param>
    /// <param name="currency">
    /// Display currency override. Rarely needed: the server names the currency its catalog is
    /// quoted in, and this only wins for display.
    /// </param>
    /// <param name="showPlans">Show the plan grid.</param>
    /// <param name="showAddOns">Show the pack grid. Off by default — most pricing pages sell plans only.</param>
    /// <param name="offerFreeTierChoice">Keep free plans in the grid. False hides every free tier.</param>
    /// <param name="packSelection">
    /// <c>none</c> gives each pack its own "Select"; <c>multi</c> lets the visitor tick several and
    /// continue once. A string, not an enum: a Razor tag-helper attribute whose property is not a
    /// string is compiled as a C# expression, so an enum would force every host to write
    /// <c>pack-selection="@PricingPackSelection.Multi"</c>. These are the JS union's own spellings.
    /// </param>
    /// <param name="addOnGroups">
    /// Headings to file packs under, matched on the add-on's catalog category. Empty groups are
    /// dropped; packs matching no group get a trailing "More packs" group.
    /// </param>
    /// <param name="showBillingToggle">
    /// Show the monthly/annual toggle. It still only appears when some plan is actually priced by
    /// the year.
    /// </param>
    /// <param name="defaultBilling">
    /// Which side the toggle starts on: <c>monthly</c> or <c>annual</c>. A string for the same
    /// reason <paramref name="packSelection"/> is one.
    /// </param>
    /// <param name="showFeatureComparison">Show each plan's feature list.</param>
    /// <param name="showLimits">Show each plan's usage limits.</param>
    /// <param name="highlightTierId">Marks one plan as the visitor's current choice (case-insensitive).</param>
    /// <param name="contactUrl">Where an unpriced plan's "Contact Sales" points.</param>
    /// <param name="includeJsonLd">Emit schema.org offers for the live catalog.</param>
    /// <param name="jsonLdUrl">Canonical URL applied to every emitted offer.</param>
    /// <param name="labels">
    /// Copy the host may replace. An instance the host only partially set keeps the shipped word
    /// for every string it left alone.
    /// </param>
    /// <param name="selectUrl">
    /// Where a selection navigates — a URL TEMPLATE, stamped with <c>tier</c>, <c>pricing</c> and
    /// <c>addons</c>. A query the template already carries is kept; those three keys are replaced
    /// by the visitor's choice. Omit it to leave navigation to a <c>ww-regsub-select</c> listener.
    /// </param>
    /// <param name="unavailableText">
    /// Replaces "Pricing is unavailable right now". The Razor analog of React's
    /// <c>errorFallback</c>: a server-rendered component cannot take a markup callback, so the
    /// sentence is a string and the Retry stays.
    /// </param>
    /// <param name="componentId">
    /// A stable id for the root element, for a host that renders two of these on one page and
    /// wants predictable hooks. Generated when omitted.
    /// </param>
    public async Task<IViewComponentResult> InvokeAsync(
        string? appId = null,
        string? currency = null,
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
        string? contactUrl = null,
        bool includeJsonLd = false,
        string? jsonLdUrl = null,
        RegistrationSubscriptionLabels? labels = null,
        string? selectUrl = null,
        string? unavailableText = null,
        string? componentId = null)
    {
        var resolvedAppId = appId is { Length: > 0 } ? appId : _options.AppId?.Trim() ?? string.Empty;

        var model = new RegistrationSubscriptionPricingViewModel
        {
            AppId = resolvedAppId,
            Labels = RegistrationSubscriptionLabels.Resolve(labels),
            ShowPlans = showPlans,
            ShowAddOns = showAddOns,
            OfferFreeTierChoice = offerFreeTierChoice,
            PackSelection = RegistrationSubscriptionPricingDecisions.ParsePackSelection(packSelection),
            AddOnGroups = addOnGroups,
            ShowBillingToggle = showBillingToggle,
            Billing = RegistrationSubscriptionPricingDecisions.ParseBilling(defaultBilling),
            ShowFeatureComparison = showFeatureComparison,
            ShowLimits = showLimits,
            HighlightTierId = highlightTierId,
            ContactUrl = contactUrl,
            IncludeJsonLd = includeJsonLd,
            JsonLdUrl = jsonLdUrl,
            SelectUrl = RegistrationSubscriptionPricingDecisions.ParseSelectUrl(selectUrl),
            UnavailableText = unavailableText
        };

        if (componentId is { Length: > 0 }) model.ComponentId = componentId;

        if (resolvedAppId.Length == 0)
        {
            // No app to ask about: the unavailable panel, never a price. The same answer React
            // gives when `appId` resolves to an empty string and the catalog hook refuses to load.
            model.Error = "An appId is required to load the public catalog.";
            _logger.LogWarning(
                "The registration-subscription pricing component has no appId — set one on the tag helper or in AddWildwoodComponentsRazor.");
        }
        else
        {
            PublicCatalog? catalog = null;
            try
            {
                catalog = await _catalog.GetAsync(resolvedAppId, currency);
            }
            catch (Exception ex)
            {
                // A failure is NOT cached, so the Retry (a reload of this page) genuinely retries.
                model.Error = ex.Message is { Length: > 0 } ? ex.Message : "Failed to load the catalog";
                _logger.LogWarning(ex, "Public catalog unavailable for app {AppId}", resolvedAppId);
            }

            model.Catalog = catalog;
        }

        model.Currency = RegistrationSubscriptionPricingDecisions.ResolveDisplayCurrency(currency, model.Catalog);

        return View(model);
    }
}
