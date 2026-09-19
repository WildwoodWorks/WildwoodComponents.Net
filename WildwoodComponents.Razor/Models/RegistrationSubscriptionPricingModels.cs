using WildwoodComponents.Razor.Components.RegistrationSubscription;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Models;

// The Razor pricing surface's types. Named after their React and Blazor counterparts so the same
// thing is called the same thing on every stack; declared here rather than shared with Blazor
// because the Razor package does not reference the Blazor package.

/// <summary>
/// Which billing cycle a pricing surface is quoting. Ported from the JS <c>PricingBilling</c>
/// union, whose two members are the strings <c>monthly</c> and <c>annual</c> —
/// <see cref="RegistrationSubscriptionPricingDecisions.BillingCode"/> spells them for the wire.
/// </summary>
public enum PricingBilling
{
    Monthly = 0,
    Annual = 1
}

/// <summary>Whether, and how many, packs a visitor may pick on a pricing surface.</summary>
public enum PricingPackSelection
{
    /// <summary>Each pack carries its own call to action; picking one takes it straight through.</summary>
    None = 0,

    /// <summary>The visitor ticks several packs and continues once.</summary>
    Multi = 1
}

/// <summary>
/// A heading the host files packs under, matched on the add-on's catalog <c>Category</c>.
/// </summary>
/// <remarks>
/// A plain settable class rather than a record, so a host can write one in a <c>.cshtml</c>
/// object initialiser and so it round-trips through a configuration binder.
/// </remarks>
public class AddOnGroup
{
    /// <summary>
    /// Stable id, rendered as the group's <c>data-ww-group</c> attribute. Left empty, the view
    /// numbers the group (<c>group-1</c>, …) so the hook is never blank.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The heading itself.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional sentence under the heading.</summary>
    public string? Blurb { get; set; }

    /// <summary>Catalog categories that belong under this heading.</summary>
    public List<string> Categories { get; set; } = new();
}

/// <summary>One rendered heading and the packs filed under it.</summary>
public class PricingPackGroup
{
    public PricingPackGroup(string id, string? title, string? blurb, List<AppTierAddOnModel> addOns)
    {
        Id = id;
        Title = title;
        Blurb = blurb;
        AddOns = addOns;
    }

    /// <summary>
    /// The group's <c>data-ww-group</c> value: the host's group id, <c>all</c> for an ungrouped
    /// grid, or <c>more</c> for the trailing catch-all.
    /// </summary>
    public string Id { get; }

    /// <summary>The heading, or null for the single untitled group of an ungrouped grid.</summary>
    public string? Title { get; }

    /// <summary>The sentence under the heading, when the host wrote one.</summary>
    public string? Blurb { get; }

    /// <summary>The packs in this group, in catalog order.</summary>
    public List<AppTierAddOnModel> AddOns { get; }
}

/// <summary>
/// What the visitor chose on a pricing surface: the payload of the bubbling
/// <c>ww-regsub-select</c> event, and what a <c>select-url</c> is stamped with.
/// </summary>
public class PricingSelection
{
    /// <summary>The chosen plan, when a plan was chosen. Null when only packs were picked.</summary>
    public string? TierId { get; set; }

    /// <summary>The plan's pricing option under the current billing cycle.</summary>
    public string? PricingId { get; set; }

    /// <summary>The billing cycle the toggle was on when the choice was made.</summary>
    public PricingBilling Billing { get; set; }

    /// <summary>
    /// Every pack selected at that moment, in catalog order. Empty rather than null, so a host
    /// never has to null-check a basket.
    /// </summary>
    public List<string> AddOnIds { get; set; } = new();
}

/// <summary>
/// A host's <c>select-url</c>, split into the pieces a selection link is reassembled from.
/// </summary>
/// <remarks>
/// <see cref="Query"/> has already had <c>tier</c>, <c>pricing</c> and <c>addons</c> removed, so
/// the browser only ever APPENDS — the merge rule is a server-side decision with a test, not an
/// inference in the page.
/// </remarks>
public sealed class SelectUrlTemplate
{
    /// <summary>The template a host did not configure. Nothing navigates.</summary>
    public static readonly SelectUrlTemplate None = new(string.Empty, string.Empty, string.Empty);

    public SelectUrlTemplate(string path, string query, string fragment)
    {
        Path = path;
        Query = query;
        Fragment = fragment;
    }

    /// <summary>Everything before the <c>?</c>.</summary>
    public string Path { get; }

    /// <summary>The host's own query parameters, already stripped of the three catalog keys.</summary>
    public string Query { get; }

    /// <summary>The <c>#fragment</c>, leading hash included, or an empty string.</summary>
    public string Fragment { get; }

    /// <summary>Whether the host configured a destination at all.</summary>
    public bool HasValue
    {
        get { return Path.Length > 0 || Query.Length > 0 || Fragment.Length > 0; }
    }
}

/// <summary>How a plan card's footer calls the visitor to action.</summary>
public enum PricingFooterKind
{
    /// <summary>The plan sells nothing and names nowhere to go. Nothing is rendered.</summary>
    None = 0,

    /// <summary>The plan's own contact link ("Contact Us").</summary>
    ContactLink = 1,

    /// <summary>The component's contact URL, for an unpriced plan ("Contact Sales").</summary>
    ContactSalesLink = 2,

    /// <summary>An unpriced plan with nowhere configured: a button that raises the select event.</summary>
    ContactSalesButton = 3,

    /// <summary>The ordinary subscribe button.</summary>
    Select = 4
}

/// <summary>One plan card, priced for both billing cycles off the live catalog.</summary>
/// <remarks>
/// Both cycles are formatted HERE. <c>regsub-pricing.js</c> only shows one of the two and hides the
/// other — the browser never formats money, so a price the visitor reads is always one the server
/// quoted and rendered.
/// </remarks>
public class PricingPlanCardViewModel
{
    public PricingPlanCardViewModel(
        AppTierModel tier,
        string currency,
        string? contactUrl,
        string? highlightTierId)
    {
        Tier = tier;
        IsHighlighted = RegistrationSubscriptionPricingDecisions.IsHighlightedTier(tier.Id, highlightTierId);
        IsEnterprise = RegistrationSubscriptionPricingDecisions.IsEnterpriseTier(tier);

        MonthlyPricing = RegistrationSubscriptionPricingDecisions.PlanPriceOption(tier, PricingBilling.Monthly);
        AnnualPricing = RegistrationSubscriptionPricingDecisions.PlanPriceOption(tier, PricingBilling.Annual);

        MonthlyPriceText = PriceText(tier, MonthlyPricing, currency);
        AnnualPriceText = PriceText(tier, AnnualPricing, currency);
        MonthlyIntervalText = RegistrationSubscriptionPricingDecisions.IntervalText(MonthlyPricing);
        AnnualIntervalText = RegistrationSubscriptionPricingDecisions.IntervalText(AnnualPricing);
        MonthlyTrialText = CatalogHelpers.TierCardTrialLabel(tier, MonthlyPricing, tier.ShowPrice);
        AnnualTrialText = CatalogHelpers.TierCardTrialLabel(tier, AnnualPricing, tier.ShowPrice);

        AnnualDiscount = RegistrationSubscriptionPricingDecisions.AnnualDiscount(tier);
        CallToActionText = RegistrationSubscriptionPricingDecisions.CallToAction(tier, IsHighlighted);

        if (tier.ShowContactButton && tier.ContactButtonUrl is { Length: > 0 })
        {
            Footer = PricingFooterKind.ContactLink;
            FooterUrl = tier.ContactButtonUrl;
            FooterText = RegistrationSubscriptionPricingDecisions.ContactUsText;
        }
        else if (IsEnterprise && contactUrl is { Length: > 0 })
        {
            Footer = PricingFooterKind.ContactSalesLink;
            FooterUrl = contactUrl;
            FooterText = RegistrationSubscriptionPricingDecisions.ContactSalesText;
        }
        else if (IsEnterprise)
        {
            Footer = PricingFooterKind.ContactSalesButton;
            FooterText = RegistrationSubscriptionPricingDecisions.ContactSalesText;
        }
        else if (tier.ShowSubscribeButton)
        {
            Footer = PricingFooterKind.Select;
            FooterText = CallToActionText;
        }
        else
        {
            Footer = PricingFooterKind.None;
            FooterText = string.Empty;
        }
    }

    public AppTierModel Tier { get; }

    public bool IsHighlighted { get; }

    /// <summary>A paid plan that sells no pricing option at all.</summary>
    public bool IsEnterprise { get; }

    public AppTierPricingModel? MonthlyPricing { get; }
    public AppTierPricingModel? AnnualPricing { get; }

    /// <summary>The monthly amount, "Free", or "Custom" — never a guess and never a zero.</summary>
    public string MonthlyPriceText { get; }

    /// <summary>The annual amount, "Free", or "Custom".</summary>
    public string AnnualPriceText { get; }

    public string MonthlyIntervalText { get; }
    public string AnnualIntervalText { get; }

    /// <summary>"14-day free trial", or empty when this cycle starts none.</summary>
    public string MonthlyTrialText { get; }

    public string AnnualTrialText { get; }

    /// <summary>Whole percent saved by paying yearly; 0 when the year is not cheaper.</summary>
    public int AnnualDiscount { get; }

    /// <summary>"Save 17%", or empty. Shown only on the annual side.</summary>
    public string AnnualDiscountText
    {
        get { return AnnualDiscount > 0 ? "Save " + AnnualDiscount.ToString() + "%" : string.Empty; }
    }

    /// <summary>The subscribe button's wording.</summary>
    public string CallToActionText { get; }

    public PricingFooterKind Footer { get; }

    /// <summary>Where the footer links, for the two link footers.</summary>
    public string? FooterUrl { get; }

    public string FooterText { get; }

    /// <summary>A link that leaves the app opens in a new tab; a relative one gets no attribute.</summary>
    public string? FooterTarget
    {
        get { return RegistrationSubscriptionPricingDecisions.LeavesTheApp(FooterUrl) ? "_blank" : null; }
    }

    /// <summary>The companion of <see cref="FooterTarget"/>: never a bare <c>_blank</c>.</summary>
    public string? FooterRel
    {
        get { return RegistrationSubscriptionPricingDecisions.LeavesTheApp(FooterUrl) ? "noopener noreferrer" : null; }
    }

    /// <summary>The pricing option a click on this card selects under one cycle.</summary>
    public string PricingIdFor(PricingBilling billing)
    {
        var pricing = billing == PricingBilling.Annual ? AnnualPricing : MonthlyPricing;
        return pricing?.Id ?? string.Empty;
    }

    /// <summary>
    /// What the card shows where a price goes: the live amount, "Free" for a free plan with no
    /// option, "Custom" for a "contact us" plan. Never a zero and never a remembered figure.
    /// </summary>
    private static string PriceText(AppTierModel tier, AppTierPricingModel? pricing, string currency)
    {
        if (RegistrationSubscriptionPricingDecisions.IsEnterpriseTier(tier))
        {
            return RegistrationSubscriptionPricingDecisions.CustomPriceText;
        }

        if (tier.IsFreeTier && pricing is null) return RegistrationSubscriptionPricingDecisions.FreePriceText;
        if (pricing is null) return string.Empty;

        return CatalogHelpers.FormatPrice(tier, pricing.Price, currency);
    }
}

/// <summary>One pack card, priced off the live catalog.</summary>
/// <remarks>
/// A pack quotes its DEFAULT pricing option regardless of the plan toggle, exactly as React's
/// <c>PackGrid</c> and the Blazor <c>PackCardBody</c> do — the toggle is a plan control, and
/// quoting a pack differently here would make the Razor page disagree with the React page about
/// what the same pack costs.
/// </remarks>
public class PricingPackCardViewModel
{
    public PricingPackCardViewModel(
        AppTierAddOnModel addOn,
        string currency,
        RegistrationSubscriptionLabels labels)
    {
        AddOn = addOn;
        PriceText = RegistrationSubscriptionPricingDecisions.PackPriceText(addOn, currency, labels);
        HasPrice = RegistrationSubscriptionPricingDecisions.HasPackPrice(addOn);
        TrialText = RegistrationSubscriptionPricingDecisions.PackTrialLabel(addOn);
        SelectAriaLabel = RegistrationSubscriptionLabels.Format(labels.PackSelectNamed, "name", addOn.Name);
    }

    public AppTierAddOnModel AddOn { get; }

    /// <summary>The live amount with its period suffix, or "Not yet available".</summary>
    public string PriceText { get; }

    /// <summary>False when the operator has defined the pack but never priced it.</summary>
    public bool HasPrice { get; }

    /// <summary>"14-day free trial", or empty.</summary>
    public string TrialText { get; }

    /// <summary>The accessible name of the single-select call to action.</summary>
    public string SelectAriaLabel { get; }
}

/// <summary>
/// Everything <c>&lt;vc:registration-subscription-pricing&gt;</c> renders: the live catalog, priced
/// for both billing cycles, plus the hooks <c>regsub-pricing.js</c> acts on.
/// </summary>
/// <remarks>
/// <para>
/// Built fresh on every request from a catalog that may be CACHED and shared. Nothing here mutates
/// the catalog or the lists hanging off it — a view model that sorted or filtered in place would
/// poison every later render of every surface that shares the entry.
/// </para>
/// <para>
/// There is no loading state: a server-rendered component has the catalog (or has failed to get
/// it) before the first byte leaves, so React's skeleton has no counterpart. What remains is
/// content, the unavailable panel, or — with plans and packs both switched off — an empty root.
/// </para>
/// </remarks>
public class RegistrationSubscriptionPricingViewModel
{
    private List<AppTierModel>? _visibleTiers;
    private List<PricingPlanCardViewModel>? _planCards;
    private List<PricingPackGroup>? _packGroups;
    private string? _jsonLd;

    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string AppId { get; set; } = string.Empty;

    /// <summary>
    /// The live catalog, or null when it could not be read. Never an empty stand-in: "this app
    /// sells nothing" and "pricing is unavailable right now" are different answers.
    /// </summary>
    public PublicCatalog? Catalog { get; set; }

    /// <summary>The message behind the unavailable panel, for the server log and the error hook.</summary>
    public string? Error { get; set; }

    /// <summary>The host's replacement for "Pricing is unavailable right now".</summary>
    public string? UnavailableText { get; set; }

    /// <summary>Copy the host may replace; the shipped words otherwise.</summary>
    public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

    public bool ShowPlans { get; set; } = true;
    public bool ShowAddOns { get; set; }
    public bool OfferFreeTierChoice { get; set; } = true;
    public PricingPackSelection PackSelection { get; set; } = PricingPackSelection.None;
    public IReadOnlyList<AddOnGroup>? AddOnGroups { get; set; }
    public bool ShowBillingToggle { get; set; } = true;
    public PricingBilling Billing { get; set; } = PricingBilling.Monthly;
    public bool ShowFeatureComparison { get; set; } = true;
    public bool ShowLimits { get; set; } = true;
    public string? HighlightTierId { get; set; }
    public string? ContactUrl { get; set; }
    public bool IncludeJsonLd { get; set; }
    public string? JsonLdUrl { get; set; }

    /// <summary>Where a selection navigates, split into the pieces the browser reassembles.</summary>
    public SelectUrlTemplate SelectUrl { get; set; } = SelectUrlTemplate.None;

    /// <summary>The currency the surface quotes in — the catalog's, unless the host overrode it.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>True when the catalog could not be read: the unavailable panel, never a price.</summary>
    public bool IsUnavailable
    {
        get { return Catalog is null || Error is { Length: > 0 }; }
    }

    /// <summary>The sentence the unavailable panel says.</summary>
    public string UnavailableMessage
    {
        get { return UnavailableText is { Length: > 0 } ? UnavailableText : Labels.PricingUnavailable; }
    }

    /// <summary>
    /// The cross-stack code for an unreadable catalog, rendered on the unavailable panel as
    /// <c>data-ww-error-code</c>. It is the .NET spelling of what React hands its <c>onError</c>
    /// (<c>{ code: 'catalog_unavailable', message }</c>), so a host's analytics can tell a pricing
    /// outage apart from every other failure without parsing the sentence.
    /// </summary>
    public string ErrorCode
    {
        get
        {
            return IsUnavailable ? RegistrationSubscriptionPricingDecisions.CatalogErrorCode : string.Empty;
        }
    }

    /// <summary>The plans to render: active plans in display order, free ones filtered if asked.</summary>
    public List<AppTierModel> VisibleTiers
    {
        get
        {
            return _visibleTiers ??=
                RegistrationSubscriptionPricingDecisions.VisibleTiers(Catalog, OfferFreeTierChoice);
        }
    }

    /// <summary>One card per visible plan, priced for both cycles.</summary>
    public List<PricingPlanCardViewModel> PlanCards
    {
        get { return _planCards ??= BuildPlanCards(); }
    }

    /// <summary>The pack headings and what is filed under each.</summary>
    public List<PricingPackGroup> PackGroups
    {
        get
        {
            return _packGroups ??= RegistrationSubscriptionPricingDecisions.GroupAddOns(
                Catalog?.AddOns, AddOnGroups, Labels.MorePacks);
        }
    }

    /// <summary>One pack card's rendering decisions, built on demand by the view.</summary>
    public PricingPackCardViewModel PackCard(AppTierAddOnModel addOn)
    {
        return new PricingPackCardViewModel(addOn, Currency, Labels);
    }

    /// <summary>Whether the visitor may tick several packs and continue once.</summary>
    public bool IsMultiSelect
    {
        get { return PackSelection == PricingPackSelection.Multi; }
    }

    /// <summary>
    /// The monthly/annual toggle appears only when some plan is actually priced by the year — a
    /// toggle with nothing to switch to is a control that lies.
    /// </summary>
    public bool ShowToggle
    {
        get
        {
            return ShowPlans
                && ShowBillingToggle
                && RegistrationSubscriptionPricingDecisions.HasAnnualPricing(VisibleTiers);
        }
    }

    /// <summary>"Save up to 17%", or empty when no plan is cheaper by the year.</summary>
    public string AnnualSavingsText
    {
        get
        {
            var best = RegistrationSubscriptionPricingDecisions.BestAnnualDiscount(VisibleTiers);
            return best > 0
                ? RegistrationSubscriptionLabels.Format(Labels.AnnualSavings, "percent", best)
                : string.Empty;
        }
    }

    /// <summary>Whether the plan grid has anything in it.</summary>
    public bool HasPlans
    {
        get { return ShowPlans && VisibleTiers.Count > 0; }
    }

    /// <summary>Whether the pack grid has anything in it.</summary>
    public bool HasPacks
    {
        get { return ShowAddOns && PackGroups.Count > 0; }
    }

    /// <summary>The wire spelling of the current cycle, for the root's data attribute.</summary>
    public string BillingCode
    {
        get { return RegistrationSubscriptionPricingDecisions.BillingCode(Billing); }
    }

    /// <summary>Every pack id in catalog order, so a basket reads in catalog order in the browser.</summary>
    public string PackOrderAttribute
    {
        get { return string.Join(",", RegistrationSubscriptionPricingDecisions.PackOrder(Catalog)); }
    }

    /// <summary>The cap a selection may not exceed. A signup link is not a shopping cart.</summary>
    public int MaxPackSelection
    {
        get { return CatalogHelpers.MaxAddOnSelection; }
    }

    /// <summary>
    /// The multi-select Continue button's wording before anything is ticked. The script recomputes
    /// it from the <c>{count}</c> templates on the root, so no English is hard-coded in the page's
    /// behaviour.
    /// </summary>
    public string ContinuePacksInitialLabel
    {
        get { return RegistrationSubscriptionPricingDecisions.ContinueWithPacksLabel(0, Labels); }
    }

    /// <summary>
    /// The schema.org offers graph, or an empty string when the host did not ask for one or the
    /// catalog prices nothing.
    /// </summary>
    /// <remarks>
    /// Serialised with <see cref="System.Text.Json"/>'s DEFAULT encoder, which escapes <c>&lt;</c>,
    /// <c>&gt;</c> and <c>&amp;</c> into their JSON unicode escapes. That is what makes the payload
    /// safe to write with <c>Html.Raw</c> inside a <c>&lt;script&gt;</c>: a pack the operator named
    /// with a closing script tag cannot close the element and run as markup.
    /// <c>JavaScriptEncoder.UnsafeRelaxedJsonEscaping</c> would write that name through verbatim,
    /// so it must never be used here.
    /// </remarks>
    public string JsonLd
    {
        get { return _jsonLd ??= BuildJsonLd(); }
    }

    private List<PricingPlanCardViewModel> BuildPlanCards()
    {
        var cards = new List<PricingPlanCardViewModel>();

        foreach (var tier in VisibleTiers)
        {
            cards.Add(new PricingPlanCardViewModel(tier, Currency, ContactUrl, HighlightTierId));
        }

        return cards;
    }

    private string BuildJsonLd()
    {
        if (!IncludeJsonLd) return string.Empty;

        var offers = CatalogHelpers.CatalogToJsonLdOffers(Catalog, JsonLdUrl);
        if (offers.Count == 0) return string.Empty;

        return System.Text.Json.JsonSerializer.Serialize(new JsonLdGraph { Graph = offers });
    }

    /// <summary>The <c>@graph</c> wrapper schema.org readers expect around a list of offers.</summary>
    private sealed class JsonLdGraph
    {
        [System.Text.Json.Serialization.JsonPropertyName("@context")]
        public string Context { get; set; } = "https://schema.org";

        [System.Text.Json.Serialization.JsonPropertyName("@graph")]
        public List<JsonLdOffer> Graph { get; set; } = new();
    }
}
