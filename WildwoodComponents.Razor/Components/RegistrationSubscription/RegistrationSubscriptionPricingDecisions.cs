using System.Net;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Components.RegistrationSubscription;

/// <summary>
/// Every decision the Razor pricing surface makes, as pure functions.
/// </summary>
/// <remarks>
/// <para>
/// Ported rule for rule from <c>WildwoodComponents.Blazor</c>'s <c>PricingViewDecisions</c>, itself
/// ported from packages/wildwood-react/src/components/registrationSubscription/views/PricingView.tsx
/// and its parts. The two .NET copies are deliberate: Razor cannot reference the Blazor package, and
/// the alternative — lifting them into Shared — would have meant reopening the reviewed Blazor port.
/// The strings and the orderings are a CROSS-STACK CONTRACT, so they are identical by construction
/// and pinned by tests rather than by a shared type.
/// </para>
/// <para>
/// Two rules bind the whole file. Every price comes off the live catalog — there is no fallback
/// price, no remembered price and no "from" price anywhere under this folder, which a source-guard
/// test enforces. And a pack selection never exceeds
/// <see cref="CatalogHelpers.MaxAddOnSelection"/> and always reads in CATALOG order rather than
/// click order, so a basket built by clicking and one built from a link cannot differ.
/// </para>
/// <para>No LINQ: the library convention, shared with Blazor.</para>
/// </remarks>
public static class RegistrationSubscriptionPricingDecisions
{
    /// <summary>The code the component reports when the catalog could not be read.</summary>
    public const string CatalogErrorCode = "catalog_unavailable";

    /// <summary>
    /// The plan-card footer's wording, matching React's shared tier-card footer and the Blazor
    /// port: a plan the visitor has already chosen says so, a free plan starts, anything else
    /// subscribes. These three strings are not labels — they belong to every plan grid on the
    /// platform.
    /// </summary>
    public const string ContinueWithThisPlanText = "Continue with This Plan";

    /// <summary>The free plan's call to action.</summary>
    public const string GetStartedText = "Get Started";

    /// <summary>The paid plan's call to action.</summary>
    public const string SubscribeText = "Subscribe";

    /// <summary>
    /// The tier card's own wording for a plan carrying its own contact link. <c>Labels.ContactUs</c>
    /// ("Contact us") is a DIFFERENT string, used elsewhere.
    /// </summary>
    public const string ContactUsText = "Contact Us";

    /// <summary>What an unpriced ("contact us") plan offers instead of a price.</summary>
    public const string ContactSalesText = "Contact Sales";

    /// <summary>
    /// What a free plan shows where a price would go. The tier card's own word, not a label:
    /// React's <c>TierCardHeader</c> hard-codes it, and <c>Labels.PlanFree</c> is the DIFFERENT
    /// string the plan summary card says.
    /// </summary>
    public const string FreePriceText = "Free";

    /// <summary>What an unpriced ("contact us") plan shows where a price would go.</summary>
    public const string CustomPriceText = "Custom";

    /// <summary>The badge on the plan the host marked as already chosen.</summary>
    public const string YourSelectionBadgeText = "Your Selection";

    /// <summary>The interval shown when a pricing option names no billing frequency at all.</summary>
    public const string DefaultIntervalText = "month";

    #region Catalog reading

    /// <summary>
    /// The currency the surface quotes in: the server names the one its catalog is quoted in, and
    /// the parameter is an override for the rare host that knows better.
    /// </summary>
    public static string ResolveDisplayCurrency(string? currencyOverride, PublicCatalog? catalog)
    {
        if (currencyOverride is { Length: > 0 }) return currencyOverride;
        if (catalog is not null && catalog.Currency is { Length: > 0 }) return catalog.Currency;
        return string.Empty;
    }

    /// <summary>
    /// The plans to show: the catalog's active plans, minus the free ones the host does not offer.
    /// Always a NEW list — the catalog instance behind it is shared and cached, and mutating it
    /// would poison every later render.
    /// </summary>
    public static List<AppTierModel> VisibleTiers(PublicCatalog? catalog, bool offerFreeTierChoice)
    {
        var tiers = new List<AppTierModel>();
        if (catalog is null) return tiers;

        foreach (var tier in catalog.Tiers)
        {
            if (tier is null) continue;
            if (!offerFreeTierChoice && tier.IsFreeTier) continue;
            tiers.Add(tier);
        }

        return tiers;
    }

    /// <summary>Whether a plan is the one the host marked as already chosen. Ids match case-insensitively.</summary>
    public static bool IsHighlightedTier(string? tierId, string? highlightTierId)
    {
        if (highlightTierId is not { Length: > 0 }) return false;
        if (tierId is not { Length: > 0 }) return false;
        return string.Equals(tierId, highlightTierId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The wire spelling of a billing cycle, matching the JS union's two members.</summary>
    public static string BillingCode(PricingBilling billing)
    {
        return billing == PricingBilling.Annual ? "annual" : "monthly";
    }

    /// <summary>
    /// Reads the billing cycle a tag helper was given. A Razor tag-helper attribute whose property
    /// is not a <see cref="string"/> is compiled as a C# EXPRESSION, so an enum parameter would
    /// force every host to write <c>default-billing="@PricingBilling.Annual"</c>. The attribute is
    /// therefore the JS union's own spelling — <c>monthly</c> or <c>annual</c> — and anything else
    /// reads as monthly rather than throwing a page away over a typo.
    /// </summary>
    public static PricingBilling ParseBilling(string? value)
    {
        var billing = (value ?? string.Empty).Trim();
        return string.Equals(billing, "annual", StringComparison.OrdinalIgnoreCase)
            ? PricingBilling.Annual
            : PricingBilling.Monthly;
    }

    /// <summary>
    /// Reads the pack-selection mode a tag helper was given: <c>none</c> or <c>multi</c>, the JS
    /// union's own spelling, for the reason <see cref="ParseBilling"/> gives. Anything else is
    /// <c>none</c>, the safer default — a visitor cannot accidentally be shown a basket.
    /// </summary>
    public static PricingPackSelection ParsePackSelection(string? value)
    {
        var selection = (value ?? string.Empty).Trim();
        return string.Equals(selection, "multi", StringComparison.OrdinalIgnoreCase)
            ? PricingPackSelection.Multi
            : PricingPackSelection.None;
    }

    /// <summary>
    /// The billing frequency <see cref="CatalogHelpers.ResolvePriceOption(AppTierModel, string, string)"/>
    /// is asked for. "Annual" matches "Yearly" and "Annually" too — one cycle to a customer.
    /// </summary>
    public static string BillingFrequency(PricingBilling billing)
    {
        return billing == PricingBilling.Annual ? "Annual" : "Monthly";
    }

    /// <summary>The pricing option a plan is quoted at under one billing cycle.</summary>
    public static AppTierPricingModel? PlanPriceOption(AppTierModel? tier, PricingBilling billing)
    {
        return CatalogHelpers.ResolvePriceOption(tier, null, BillingFrequency(billing));
    }

    /// <summary>A "contact us" plan: one that costs something and sells no pricing option at all.</summary>
    public static bool IsEnterpriseTier(AppTierModel? tier)
    {
        if (tier is null) return false;
        return !tier.IsFreeTier && tier.PricingOptions.Count == 0;
    }

    /// <summary>Whether any plan in the grid is priced by the year.</summary>
    /// <remarks>A toggle with nothing to switch to is a control that lies, so the view hides it.</remarks>
    public static bool HasAnnualPricing(IReadOnlyList<AppTierModel>? tiers)
    {
        if (tiers is null) return false;

        foreach (var tier in tiers)
        {
            if (tier is null) continue;
            foreach (var option in tier.PricingOptions)
            {
                if (option is not null && FormatHelpers.IsAnnualFrequency(option.BillingFrequency)) return true;
            }
        }

        return false;
    }

    /// <summary>What one plan saves by the year, as whole percent, or 0 when it is not cheaper annually.</summary>
    public static int AnnualDiscount(AppTierModel? tier)
    {
        if (tier is null || tier.PricingOptions.Count < 2) return 0;

        AppTierPricingModel? monthly = null;
        AppTierPricingModel? annual = null;

        foreach (var option in tier.PricingOptions)
        {
            if (option is null) continue;
            if (string.Equals(option.BillingFrequency, "Monthly", StringComparison.OrdinalIgnoreCase)) monthly = option;
            else if (FormatHelpers.IsAnnualFrequency(option.BillingFrequency)) annual = option;
        }

        if (monthly is null || annual is null) return 0;

        var monthlyTotal = monthly.Price * 12m;
        if (monthlyTotal <= 0m || annual.Price >= monthlyTotal) return 0;

        return (int)Math.Round((monthlyTotal - annual.Price) / monthlyTotal * 100m, MidpointRounding.AwayFromZero);
    }

    /// <summary>The largest annual saving on offer, or 0 when no plan is cheaper by the year.</summary>
    public static int BestAnnualDiscount(IReadOnlyList<AppTierModel>? tiers)
    {
        var best = 0;
        if (tiers is null) return best;

        foreach (var tier in tiers)
        {
            var discount = AnnualDiscount(tier);
            if (discount > best) best = discount;
        }

        return best;
    }

    /// <summary>
    /// The interval a plan's price is quoted per, lower-cased as the tier cards write it. Empty
    /// frequencies read as "month", matching the Blazor grid.
    /// </summary>
    public static string IntervalText(AppTierPricingModel? pricing)
    {
        if (pricing is null) return DefaultIntervalText;
        var frequency = pricing.BillingFrequency;
        return frequency is { Length: > 0 } ? frequency.ToLowerInvariant() : DefaultIntervalText;
    }

    /// <summary>The plan-card call to action.</summary>
    public static string CallToAction(AppTierModel tier, bool highlighted)
    {
        if (highlighted) return ContinueWithThisPlanText;
        return tier.IsFreeTier ? GetStartedText : SubscribeText;
    }

    /// <summary>
    /// A link that leaves the app opens in a new tab, as every other tier card in the library does.
    /// </summary>
    /// <remarks>
    /// The test is case-INSENSITIVE where React's <c>startsWith('http')</c> is not. A URL scheme is
    /// case-insensitive, so this is a strict superset of React's rule: every URL React marks
    /// external is marked external here, and a <c>HTTPS://</c> one additionally gets the
    /// <c>rel="noopener noreferrer"</c> it should have had.
    /// </remarks>
    public static bool LeavesTheApp(string? url)
    {
        return url is { Length: > 0 } && url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Packs

    /// <summary>
    /// The per-period suffix on a pack price. An unrecognised frequency contributes nothing rather
    /// than a guess — OneTime and Lifetime amounts stand on their own.
    /// </summary>
    public static string BillingSuffix(string? billingFrequency)
    {
        var frequency = (billingFrequency ?? string.Empty).Trim().ToLowerInvariant();
        switch (frequency)
        {
            case "monthly": return "/mo";
            case "yearly":
            case "annually":
            case "annual": return "/yr";
            case "weekly": return "/wk";
            case "daily": return "/day";
            default: return string.Empty;
        }
    }

    /// <summary>
    /// What a pack card shows where its price goes. A pack the operator defined but never priced
    /// says so rather than rendering a blank or a zero, either of which a visitor would read as
    /// "free".
    /// </summary>
    public static string PackPriceText(
        AppTierAddOnModel? addOn,
        string? catalogCurrency,
        RegistrationSubscriptionLabels labels)
    {
        var pricing = CatalogHelpers.ResolvePriceOption(addOn);
        if (pricing is null) return labels.PackUnavailable;

        var currency = AddOnRowRules.ResolveCurrency(addOn, catalogCurrency);
        return FormatHelpers.FormatMoney(pricing.Price, currency) + BillingSuffix(pricing.BillingFrequency);
    }

    /// <summary>Whether a pack card is showing a price at all, so the card can style the line.</summary>
    public static bool HasPackPrice(AppTierAddOnModel? addOn)
    {
        return CatalogHelpers.ResolvePriceOption(addOn) is not null;
    }

    /// <summary>The trial line under a pack's price, or an empty string when it starts no trial.</summary>
    public static string PackTrialLabel(AppTierAddOnModel? addOn)
    {
        var pricing = CatalogHelpers.ResolvePriceOption(addOn);
        return pricing is null ? string.Empty : CatalogHelpers.TrialLabel(pricing.TrialDays);
    }

    /// <summary>
    /// The packs under the host's headings. Empty groups are dropped; packs matching no group land
    /// in a trailing catch-all. With no groups at all, one untitled group.
    /// </summary>
    /// <remarks>
    /// The stray rule is the important one: a pack the company sells and has priced going silently
    /// missing from the page that sells it is the failure this must not have.
    /// </remarks>
    public static List<PricingPackGroup> GroupAddOns(
        IReadOnlyList<AppTierAddOnModel>? addOns,
        IReadOnlyList<AddOnGroup>? groups,
        string morePacksTitle)
    {
        var views = new List<PricingPackGroup>();

        var all = new List<AppTierAddOnModel>();
        if (addOns is not null)
        {
            foreach (var addOn in addOns)
            {
                if (addOn is not null) all.Add(addOn);
            }
        }

        if (groups is null || groups.Count == 0)
        {
            if (all.Count > 0) views.Add(new PricingPackGroup("all", null, null, all));
            return views;
        }

        // Case-SENSITIVE, matching JS: the grouping there is `group.categories.includes(category)`
        // over a Set, so "documents" and "Documents" are two different categories. A pack whose
        // category differs from the host's only in case therefore files as a stray and lands in the
        // catch-all rather than under the heading — visible, which is the rule that matters.
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (group is null) continue;
            foreach (var category in group.Categories)
            {
                if (category is not null) known.Add(category);
            }
        }

        var index = 0;
        foreach (var group in groups)
        {
            index++;
            if (group is null) continue;

            var filed = new List<AppTierAddOnModel>();
            foreach (var addOn in all)
            {
                if (ContainsCategory(group.Categories, addOn.Category)) filed.Add(addOn);
            }

            // A host writing groups in a .cshtml has no reason to invent ids, so an unnamed group
            // still gets a stable data-ww-group hook rather than an empty attribute.
            var id = group.Id is { Length: > 0 } ? group.Id : "group-" + index.ToString();
            if (filed.Count > 0) views.Add(new PricingPackGroup(id, group.Title, group.Blurb, filed));
        }

        var orphans = new List<AppTierAddOnModel>();
        foreach (var addOn in all)
        {
            if (!known.Contains(addOn.Category ?? string.Empty)) orphans.Add(addOn);
        }

        if (orphans.Count > 0) views.Add(new PricingPackGroup("more", morePacksTitle, null, orphans));

        return views;
    }

    /// <summary>
    /// Every pack id in catalog order, as the browser needs it: a multi-select basket reads in
    /// catalog order rather than click order, and a card can appear under two headings, so the JS
    /// is told the order instead of inferring it from the DOM.
    /// </summary>
    public static List<string> PackOrder(PublicCatalog? catalog)
    {
        var ids = new List<string>();
        if (catalog is null) return ids;

        foreach (var addOn in catalog.AddOns)
        {
            if (addOn is not null && addOn.Id is { Length: > 0 }) ids.Add(addOn.Id);
        }

        return ids;
    }

    /// <summary>The Continue button's copy, singular when exactly one pack is ticked.</summary>
    public static string ContinueWithPacksLabel(int count, RegistrationSubscriptionLabels labels)
    {
        if (count == 1) return labels.ContinueWithOnePack;
        return RegistrationSubscriptionLabels.Format(labels.ContinueWithPacks, "count", count);
    }

    #endregion

    #region The select URL template

    /// <summary>
    /// Splits the host's <c>selectUrl</c> into the three pieces the browser reassembles a selection
    /// link from, dropping any <c>tier</c>, <c>pricing</c> or <c>addons</c> the template already
    /// carried — the visitor's choice is what those mean, so a stale value in the template would
    /// silently win or duplicate.
    /// </summary>
    /// <remarks>
    /// The merge happens HERE rather than in the browser so it is a tested decision: the client
    /// only concatenates <c>tier=</c>, <c>pricing=</c> and <c>addons=</c> onto what it is handed.
    /// Every other parameter the host wrote survives verbatim, repeats and all.
    /// </remarks>
    public static SelectUrlTemplate ParseSelectUrl(string? selectUrl)
    {
        if (selectUrl is not { Length: > 0 }) return SelectUrlTemplate.None;

        var fragment = string.Empty;
        var withoutFragment = selectUrl;

        var hash = withoutFragment.IndexOf('#');
        if (hash >= 0)
        {
            fragment = withoutFragment.Substring(hash);
            withoutFragment = withoutFragment.Substring(0, hash);
        }

        var path = withoutFragment;
        var rawQuery = string.Empty;

        var question = withoutFragment.IndexOf('?');
        if (question >= 0)
        {
            path = withoutFragment.Substring(0, question);
            rawQuery = withoutFragment.Substring(question + 1);
        }

        return new SelectUrlTemplate(path, StripCatalogKeys(rawQuery), fragment);
    }

    /// <summary>
    /// The host's own query parameters, in their own encoding and order, minus the three keys a
    /// catalog selection owns.
    /// </summary>
    private static string StripCatalogKeys(string rawQuery)
    {
        if (rawQuery.Length == 0) return string.Empty;

        var kept = new List<string>();
        foreach (var segment in rawQuery.Split('&'))
        {
            if (segment.Length == 0) continue;

            var separator = segment.IndexOf('=');
            var rawName = separator >= 0 ? segment.Substring(0, separator) : segment;
            var name = WebUtility.UrlDecode(rawName) ?? string.Empty;

            if (string.Equals(name, CatalogHelpers.QueryKeys.Tier, StringComparison.Ordinal)) continue;
            if (string.Equals(name, CatalogHelpers.QueryKeys.Pricing, StringComparison.Ordinal)) continue;
            if (string.Equals(name, CatalogHelpers.QueryKeys.AddOns, StringComparison.Ordinal)) continue;

            kept.Add(segment);
        }

        return string.Join("&", kept);
    }

    /// <summary>
    /// The whole link for one selection, as the browser will build it. Server-side only the plan
    /// links are known (a pack basket is picked in the browser), so this exists for tests and for
    /// hosts that want to render their own anchors.
    /// </summary>
    public static string BuildSelectUrl(SelectUrlTemplate template, PricingSelection selection)
    {
        if (!template.HasValue) return string.Empty;

        var query = new QueryParameters();
        if (selection.TierId is { Length: > 0 }) query.Set(CatalogHelpers.QueryKeys.Tier, selection.TierId);
        if (selection.PricingId is { Length: > 0 }) query.Set(CatalogHelpers.QueryKeys.Pricing, selection.PricingId);

        var ids = CatalogHelpers.ParseAddOnIdList(string.Join(",", selection.AddOnIds));
        if (ids.Count > 0) query.Set(CatalogHelpers.QueryKeys.AddOns, string.Join(",", ids));

        var appended = query.ToString();
        var combined = template.Query;
        if (appended.Length > 0) combined = combined.Length > 0 ? combined + "&" + appended : appended;

        return template.Path + (combined.Length > 0 ? "?" + combined : string.Empty) + template.Fragment;
    }

    #endregion

    #region Selection payloads

    /// <summary>
    /// What a plan's call to action hands back: the plan, its option under the current cycle, and
    /// whatever packs are ticked — so one click buys the whole basket.
    /// </summary>
    public static PricingSelection PlanSelectionPayload(
        AppTierModel tier,
        PricingBilling billing,
        IReadOnlyList<string>? selectedPackIds)
    {
        var pricing = PlanPriceOption(tier, billing);
        return new PricingSelection
        {
            TierId = tier.Id,
            PricingId = pricing?.Id,
            Billing = billing,
            AddOnIds = Copy(selectedPackIds)
        };
    }

    /// <summary>What a single pack's call to action hands back. No plan is implied.</summary>
    public static PricingSelection PackSelectionPayload(string addOnId, PricingBilling billing)
    {
        var selection = new PricingSelection { Billing = billing };
        selection.AddOnIds.Add(addOnId);
        return selection;
    }

    #endregion

    #region Small helpers (no LINQ)

    /// <summary>
    /// Whether a group claims this category. Ordinal and case-SENSITIVE, because JS matches with
    /// <c>Array.includes</c> — the two stacks have to file the same pack under the same heading.
    /// </summary>
    private static bool ContainsCategory(IReadOnlyList<string>? categories, string? category)
    {
        if (categories is null) return false;

        foreach (var candidate in categories)
        {
            if (string.Equals(candidate ?? string.Empty, category ?? string.Empty, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> Copy(IReadOnlyList<string>? values)
    {
        var copy = new List<string>();
        if (values is null) return copy;

        foreach (var value in values)
        {
            if (value is not null) copy.Add(value);
        }

        return copy;
    }

    #endregion
}
