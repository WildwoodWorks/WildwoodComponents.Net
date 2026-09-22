using System;
using System.Collections.Generic;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription
{
    /// <summary>Which of the pricing view's bodies applies right now.</summary>
    internal enum PricingBodyKind
    {
        /// <summary>The catalog is on its way. Shapes only — never a number.</summary>
        Loading = 0,

        /// <summary>The catalog could not be read. "Pricing is unavailable right now" and a Retry.</summary>
        Error = 1,

        /// <summary>A live catalog with something to sell.</summary>
        Content = 2,

        /// <summary>
        /// A live catalog the host's options leave nothing to render from — plans and packs both
        /// switched off, or an app that sells neither. The root and its hooks still render, and
        /// nothing goes inside them; the alternative would be copy no stack has a label for.
        /// </summary>
        Empty = 3
    }

    /// <summary>
    /// Every decision the pricing view makes, as pure functions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from packages/wildwood-react/src/components/registrationSubscription/views/PricingView.tsx
    /// and its parts, whose react-native twin
    /// (<c>views/pricingViewModel.ts</c>) does the same decomposition for the same reason: the
    /// repo has no bUnit renderer, so a rule that lives inside markup is a rule that cannot be
    /// tested. The view is a thin arrangement of what is here.
    /// </para>
    /// <para>
    /// Two rules bind the whole file, and both are why the component exists. Every price comes off
    /// the live catalog — there is no fallback price, no remembered price and no "from" price
    /// anywhere under this folder, which a source-guard test enforces. And a pack selection never
    /// exceeds <see cref="CatalogHelpers.MaxAddOnSelection"/> and always reads in catalog order
    /// rather than click order, so a basket built by clicking and one built from a link cannot
    /// differ.
    /// </para>
    /// <para>No LINQ: Blazor renders this and LINQ breaks iOS/MAUI at runtime.</para>
    /// </remarks>
    internal static class PricingViewDecisions
    {
        /// <summary>The code the view reports when the catalog could not be read.</summary>
        public const string CatalogErrorCode = "catalog_unavailable";

        /// <summary>How many placeholder cards the skeleton draws by default.</summary>
        public const int SkeletonCards = 3;

        #region Loading, sections and body

        /// <summary>
        /// Whether the view has to ask the server for a catalog at all. A host-supplied catalog is
        /// rendered as it stands — the Blazor analog of JS's <c>initialCatalog</c> — so a
        /// prerendered page paints real prices without a request.
        /// </summary>
        public static bool ShouldLoadCatalog(PublicCatalog? preloadedCatalog, string? appId)
        {
            if (preloadedCatalog is not null) return false;
            return appId is { Length: > 0 };
        }

        /// <summary>Which grids the host asked for.</summary>
        public static bool ShowSection(bool requested)
        {
            return requested;
        }

        /// <summary>
        /// Loading shows shapes, an unreadable catalog shows the unavailable panel, and only a
        /// catalog in hand shows prices.
        /// </summary>
        /// <remarks>
        /// A failure wins even when a catalog is already on screen: the prices go away with the
        /// answer that produced them, rather than standing there as a quote nobody can vouch for
        /// any more.
        /// </remarks>
        public static PricingBodyKind BodyKind(
            PublicCatalog? catalog,
            bool loading,
            string? error,
            bool showPlans,
            bool showAddOns,
            bool offerFreeTierChoice)
        {
            if (error is { Length: > 0 }) return PricingBodyKind.Error;
            if (catalog is null) return loading ? PricingBodyKind.Loading : PricingBodyKind.Error;

            var hasPlans = showPlans && VisibleTiers(catalog, offerFreeTierChoice).Count > 0;
            var hasPacks = showAddOns && catalog.AddOns.Count > 0;
            return hasPlans || hasPacks ? PricingBodyKind.Content : PricingBodyKind.Empty;
        }

        /// <summary>
        /// Whether a failure is news. One report per distinct message: a Retry that fails again is
        /// news, a re-render is not.
        /// </summary>
        public static bool ShouldReportError(string? alreadyReported, string? error)
        {
            if (error is not { Length: > 0 }) return false;
            return !string.Equals(alreadyReported, error, StringComparison.Ordinal);
        }

        #endregion

        #region Catalog reading

        /// <summary>
        /// The currency the surface quotes in: the server names the one its catalog is quoted in,
        /// and the parameter is an override for the rare host that knows better.
        /// </summary>
        public static string ResolveDisplayCurrency(string? currencyOverride, PublicCatalog? catalog)
        {
            if (currencyOverride is { Length: > 0 }) return currencyOverride;
            if (catalog is not null && catalog.Currency is { Length: > 0 }) return catalog.Currency;
            return string.Empty;
        }

        /// <summary>The plans to show: the catalog's active plans, minus the free ones the host does not offer.</summary>
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
        /// The billing frequency <see cref="CatalogHelpers.ResolvePriceOption(AppTierModel, string, string)"/>
        /// is asked for. "Annual" matches "Yearly" and "Annually" too — one cycle to a customer.
        /// </summary>
        public static string BillingFrequency(PricingBilling billing)
        {
            return billing == PricingBilling.Annual ? "Annual" : "Monthly";
        }

        /// <summary>The pricing option a plan is quoted at under the current billing cycle.</summary>
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

        /// <summary>
        /// What one plan saves by the year, as whole percent, or 0 when it is not cheaper annually.
        /// </summary>
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

        #endregion

        #region Packs

        /// <summary>
        /// The per-period suffix on a pack price. An unrecognised frequency contributes nothing
        /// rather than a guess — OneTime and Lifetime amounts stand on their own.
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
        /// What a pack card shows where its price goes. A pack the operator defined but never
        /// priced says so rather than rendering a blank or a zero, either of which a visitor would
        /// read as "free".
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
        /// The packs under the host's headings. Empty groups are dropped; packs matching no group
        /// land in a trailing catch-all. With no groups at all, one untitled group.
        /// </summary>
        /// <remarks>
        /// The stray rule is the important one: a pack the company sells and has priced going
        /// silently missing from the page that sells it is the failure this must not have.
        /// </remarks>
        public static List<PackGroupView> GroupAddOns(
            IReadOnlyList<AppTierAddOnModel>? addOns,
            IReadOnlyList<AddOnGroup>? groups,
            string morePacksTitle)
        {
            var views = new List<PackGroupView>();

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
                if (all.Count > 0) views.Add(new PackGroupView("all", null, null, all));
                return views;
            }

            // Case-SENSITIVE, matching JS: the grouping there is `group.categories.includes(category)`
            // over a Set, so "documents" and "Documents" are two different categories. A pack whose
            // category differs from the host's only in case therefore files as a stray and lands in
            // the catch-all rather than under the heading — visible, which is the rule that matters.
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in groups)
            {
                if (group is null) continue;
                foreach (var category in group.Categories)
                {
                    if (category is not null) known.Add(category);
                }
            }

            foreach (var group in groups)
            {
                if (group is null) continue;

                var filed = new List<AppTierAddOnModel>();
                foreach (var addOn in all)
                {
                    if (ContainsCategory(group.Categories, addOn.Category)) filed.Add(addOn);
                }

                if (filed.Count > 0) views.Add(new PackGroupView(group.Id, group.Title, group.Blurb, filed));
            }

            var orphans = new List<AppTierAddOnModel>();
            foreach (var addOn in all)
            {
                if (!known.Contains(addOn.Category ?? string.Empty)) orphans.Add(addOn);
            }

            if (orphans.Count > 0) views.Add(new PackGroupView("more", morePacksTitle, null, orphans));

            return views;
        }

        /// <summary>
        /// Tick or untick one pack.
        /// </summary>
        /// <remarks>
        /// The answer is in CATALOG order rather than click order, so what comes back reads the
        /// same as the grid it was picked from, and it never grows past
        /// <see cref="CatalogHelpers.MaxAddOnSelection"/> — the cap
        /// <see cref="CatalogHelpers.ParseAddOnIdList(string, PublicCatalog)"/> applies to a
        /// selection arriving in a link. Unticking is always allowed, cap or no cap.
        /// </remarks>
        public static List<string> TogglePackSelection(
            PublicCatalog? catalog,
            IReadOnlyList<string>? current,
            string addOnId)
        {
            var selected = new List<string>();
            if (current is not null)
            {
                foreach (var id in current)
                {
                    if (id is not null) selected.Add(id);
                }
            }

            var isSelected = Contains(selected, addOnId);
            if (!isSelected && selected.Count >= CatalogHelpers.MaxAddOnSelection) return selected;

            if (isSelected) Remove(selected, addOnId);
            else selected.Add(addOnId);

            // Without a catalog there is no order to read the selection in — and no grid it could
            // have been picked from.
            if (catalog is null) return new List<string>();

            var ordered = new List<string>();
            foreach (var pack in CatalogHelpers.SelectPacks(catalog, selected)) ordered.Add(pack.Id);
            return ordered;
        }

        /// <summary>The Continue button's copy, singular when exactly one pack is ticked.</summary>
        public static string ContinueWithPacksLabel(int count, RegistrationSubscriptionLabels labels)
        {
            if (count == 1) return labels.ContinueWithOnePack;
            return RegistrationSubscriptionLabels.Format(labels.ContinueWithPacks, "count", count);
        }

        #endregion

        #region Selection payloads

        /// <summary>
        /// What a plan's call to action hands back: the plan, its option under the current cycle,
        /// and whatever packs are ticked — so one click buys the whole basket.
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

        /// <summary>What the multi-select Continue hands back.</summary>
        public static PricingSelection PacksContinuePayload(
            PricingBilling billing,
            IReadOnlyList<string>? selectedPackIds)
        {
            return new PricingSelection
            {
                Billing = billing,
                AddOnIds = Copy(selectedPackIds)
            };
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

        private static bool Contains(List<string> values, string value)
        {
            foreach (var candidate in values)
            {
                if (string.Equals(candidate, value, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static void Remove(List<string> values, string value)
        {
            for (var index = values.Count - 1; index >= 0; index--)
            {
                if (string.Equals(values[index], value, StringComparison.Ordinal)) values.RemoveAt(index);
            }
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
}
