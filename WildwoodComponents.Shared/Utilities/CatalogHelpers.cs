using System.Globalization;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// The public catalog: what an app sells, as the server last said it. Ported function for function
/// from packages/wildwood-core/src/features/catalog.ts.
/// </summary>
/// <remarks>
/// Every member is pure: nothing here reads a browser, a session, a clock other than
/// <see cref="DateTimeOffset.UtcNow"/>, or any storage, so the same helpers build a catalog during
/// a Razor server render and inside a Blazor circuit. Prices are only ever read off the live public
/// responses the caller passes in; nothing caches a price or falls back to one. No LINQ — Blazor
/// consumes this and LINQ breaks iOS/MAUI at runtime.
/// </remarks>
public static class CatalogHelpers
{
    /// <summary>The <c>TierStatus</c> the server publishes for a tier or pack that is on sale.</summary>
    public const string ActiveTierStatus = "Active";

    /// <summary>How many packs one selection may carry. A signup link is not a shopping cart.</summary>
    public const int MaxAddOnSelection = 25;

    /// <summary>
    /// The currency used when neither the server nor the caller named one. Every price stored on
    /// the platform is in US dollars, so this matches the server's own fallback.
    /// </summary>
    private const string FallbackCurrency = "USD";

    /// <summary>The query keys a catalog selection travels under. These are the keys the live sites use.</summary>
    public static class QueryKeys
    {
        public const string Tier = "tier";
        public const string Pricing = "pricing";
        public const string AddOns = "addons";
    }

    /// <summary>
    /// Normalises anything URL-shaped into parameters: a full URL, a query string with or without
    /// its leading <c>?</c>, or an empty one. Mirrors JS <c>asSearchParams</c>.
    /// </summary>
    public static QueryParameters AsSearchParams(string? value)
    {
        return QueryParameters.Parse(value);
    }

    /// <summary>
    /// Builds the catalog a pricing, signup or upgrade screen renders from.
    /// </summary>
    /// <remarks>
    /// The currency is the first non-empty one the server sent on any tier or pack — it is an
    /// app-level setting, so every item carries the same value — then
    /// <paramref name="currencyOverride"/>, then USD. Only items the server marks
    /// <see cref="ActiveTierStatus"/> survive, in <c>DisplayOrder</c>. The lists handed in are
    /// never mutated.
    /// </remarks>
    /// <param name="appId">The app the catalog belongs to.</param>
    /// <param name="tiers">The response from the public tiers endpoint.</param>
    /// <param name="addOns">The response from the public add-ons endpoint.</param>
    /// <param name="currencyOverride">
    /// Used only when the responses carry no currency — an older server that does not send one.
    /// </param>
    public static PublicCatalog BuildPublicCatalog(
        string appId,
        IReadOnlyList<AppTierModel>? tiers = null,
        IReadOnlyList<AppTierAddOnModel>? addOns = null,
        string? currencyOverride = null)
    {
        var currency = FirstCurrency(tiers, addOns) ?? TrimmedOrNull(currencyOverride) ?? FallbackCurrency;

        var activeTiers = new List<AppTierModel>();
        if (tiers is not null)
        {
            foreach (var tier in tiers)
            {
                if (tier is not null && IsActive(tier.Status)) activeTiers.Add(tier);
            }
        }

        var activeAddOns = new List<AppTierAddOnModel>();
        if (addOns is not null)
        {
            foreach (var addOn in addOns)
            {
                if (addOn is not null && IsActive(addOn.Status)) activeAddOns.Add(addOn);
            }
        }

        return new PublicCatalog
        {
            AppId = appId,
            Currency = currency,
            Tiers = SortByDisplayOrder(activeTiers, tier => tier.DisplayOrder),
            AddOns = SortByDisplayOrder(activeAddOns, addOn => addOn.DisplayOrder),
            FetchedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// The price option to charge for a tier: the one named by <paramref name="pricingId"/>, else
    /// the one matching <paramref name="billing"/>, else the tier's default, else the first in
    /// display order. Null when the tier sells no pricing at all (a "contact us" tier).
    /// </summary>
    public static AppTierPricingModel? ResolvePriceOption(
        AppTierModel? tier,
        string? pricingId = null,
        string? billing = null)
    {
        return ResolveOption(tier?.PricingOptions, pricingId, billing);
    }

    /// <summary>
    /// The price option to charge for a pack, by the same rule as the tier overload.
    /// </summary>
    public static AppTierAddOnPricingModel? ResolvePriceOption(
        AppTierAddOnModel? addOn,
        string? pricingId = null,
        string? billing = null)
    {
        return ResolveOption(addOn?.PricingOptions, pricingId, billing);
    }

    /// <summary>
    /// The currency a tier's prices are quoted in: the tier's own, then the catalog's (or the
    /// component's fallback), then null — which <see cref="FormatHelpers.FormatMoney"/> reads as
    /// USD, exactly as the JS <c>formatMoney</c> does. The pack overload lives on
    /// <see cref="AddOnRowRules.ResolveCurrency"/>; both follow the one precedence rule.
    /// </summary>
    public static string? ResolveCurrency(AppTierModel? tier, string? catalogCurrency)
    {
        var own = tier?.Currency;
        if (own is not null && own.Trim().Length > 0) return own;
        return catalogCurrency;
    }

    /// <summary>
    /// A tier pricing option's price in the tier's own currency. One call so no component has to
    /// keep a symbol table of its own — the private per-component copies priced a CHF or SEK plan
    /// in dollars.
    /// </summary>
    public static string FormatPrice(AppTierModel? tier, decimal amount, string? catalogCurrency)
    {
        return FormatHelpers.FormatMoney(amount, ResolveCurrency(tier, catalogCurrency));
    }

    /// <summary>
    /// The tier card's trial line, matching React's <c>TierCardHeader</c>: shown only when the card
    /// is showing a price, the tier is neither enterprise nor free, the option costs something and
    /// it starts a trial. Empty string otherwise, so the caller renders nothing.
    /// </summary>
    /// <remarks>
    /// A free plan and a "contact us" tier both advertise no trial — there is nothing to try before
    /// paying — and a zero-priced option is the same case even on a paid tier.
    /// </remarks>
    public static string TierCardTrialLabel(
        AppTierModel? tier,
        AppTierPricingModel? pricing,
        bool showPrice = true)
    {
        if (tier is null || pricing is null || !showPrice) return string.Empty;
        if (tier.IsFreeTier) return string.Empty;
        // Enterprise = a paid tier that sells no pricing option at all; one was resolved here, so
        // this tier is not enterprise. Guard anyway for callers passing a foreign option.
        if (tier.PricingOptions.Count == 0) return string.Empty;
        if (pricing.Price <= 0m) return string.Empty;
        return TrialLabel(pricing.TrialDays);
    }

    /// <summary>
    /// "14-day free trial", or an empty string when the pricing starts no trial. Exact wording —
    /// it is a cross-stack contract.
    /// </summary>
    public static string TrialLabel(int? trialDays)
    {
        if (trialDays is null || trialDays.Value <= 0) return string.Empty;
        return trialDays.Value.ToString(CultureInfo.InvariantCulture) + "-day free trial";
    }

    /// <summary>
    /// schema.org <c>Offer</c> objects for everything in the catalog that has a live price.
    /// </summary>
    /// <remarks>
    /// No price is invented: an item with no pricing option (a "contact us" tier) and a tier whose
    /// operator turned <c>ShowPrice</c> off are both left out rather than published at zero.
    /// </remarks>
    /// <param name="catalog">The catalog to publish.</param>
    /// <param name="url">Canonical URL of the page the offers appear on, applied to every offer as given.</param>
    public static List<JsonLdOffer> CatalogToJsonLdOffers(PublicCatalog? catalog, string? url = null)
    {
        var offers = new List<JsonLdOffer>();
        if (catalog is null) return offers;

        foreach (var tier in catalog.Tiers)
        {
            if (tier is null || !tier.ShowPrice) continue;
            var pricing = ResolvePriceOption(tier);
            if (pricing is null) continue;
            offers.Add(ToOffer(tier.Name, pricing.Price, catalog.Currency, url));
        }

        foreach (var addOn in catalog.AddOns)
        {
            if (addOn is null) continue;
            var pricing = ResolvePriceOption(addOn);
            if (pricing is null) continue;
            offers.Add(ToOffer(addOn.Name, pricing.Price, catalog.Currency, url));
        }

        return offers;
    }

    /// <summary>
    /// Reads a list of add-on ids out of a query value: comma- or whitespace-separated, trimmed,
    /// de-duplicated and capped at <see cref="MaxAddOnSelection"/>. Pass a catalog to keep only ids
    /// the app actually sells, so a hand-edited link cannot smuggle an unknown pack into a checkout.
    /// </summary>
    public static List<string> ParseAddOnIdList(string? value, PublicCatalog? catalog = null)
    {
        return ParseAddOnIdList(new[] { value }, catalog);
    }

    /// <summary>
    /// The repeated-parameter form (<c>?addons=a&amp;addons=b</c>), read by the same rules as the
    /// single-value overload.
    /// </summary>
    public static List<string> ParseAddOnIdList(IReadOnlyList<string?>? values, PublicCatalog? catalog = null)
    {
        var ids = new List<string>();
        if (values is null) return ids;

        HashSet<string>? known = null;
        if (catalog is not null)
        {
            known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var addOn in catalog.AddOns)
            {
                if (addOn is not null) known.Add(addOn.Id);
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            if (value is null) continue;

            foreach (var candidate in SplitOnCommasAndWhitespace(value))
            {
                if (candidate.Length == 0 || seen.Contains(candidate)) continue;
                if (known is not null && !known.Contains(candidate)) continue;

                seen.Add(candidate);
                ids.Add(candidate);
                if (ids.Count >= MaxAddOnSelection) return ids;
            }
        }

        return ids;
    }

    /// <summary>
    /// The packs named by <paramref name="addOnIds"/>, in catalog order rather than the order they
    /// were asked for.
    /// </summary>
    public static List<AppTierAddOnModel> SelectPacks(PublicCatalog? catalog, IReadOnlyList<string>? addOnIds)
    {
        var packs = new List<AppTierAddOnModel>();
        if (catalog is null || addOnIds is null || addOnIds.Count == 0) return packs;

        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in addOnIds)
        {
            if (id is not null) wanted.Add(id);
        }
        if (wanted.Count == 0) return packs;

        foreach (var addOn in catalog.AddOns)
        {
            if (addOn is not null && wanted.Contains(addOn.Id)) packs.Add(addOn);
        }

        return packs;
    }

    /// <summary>
    /// Encodes a selection as a query string (no leading <c>?</c>), under <see cref="QueryKeys"/>.
    /// </summary>
    public static string EncodeCatalogSelection(CatalogSelection? selection)
    {
        var parameters = new QueryParameters();
        if (selection is null) return parameters.ToString();

        var tierId = TrimmedOrNull(selection.TierId);
        if (tierId is not null) parameters.Set(QueryKeys.Tier, tierId);

        var pricingId = TrimmedOrNull(selection.PricingId);
        if (pricingId is not null) parameters.Set(QueryKeys.Pricing, pricingId);

        var addOnIds = ParseAddOnIdList(ToNullableList(selection.AddOnIds));
        if (addOnIds.Count > 0) parameters.Set(QueryKeys.AddOns, string.Join(",", addOnIds.ToArray()));

        return parameters.ToString();
    }

    /// <summary>
    /// Reads a selection back out of a URL or a query string. Pass a catalog to drop add-on ids the
    /// app does not sell.
    /// </summary>
    public static DecodedCatalogSelection DecodeCatalogSelection(string? searchOrUrl, PublicCatalog? catalog = null)
    {
        return DecodeCatalogSelection(AsSearchParams(searchOrUrl), catalog);
    }

    /// <summary>
    /// Reads a selection out of already-parsed parameters.
    /// </summary>
    public static DecodedCatalogSelection DecodeCatalogSelection(QueryParameters? parameters, PublicCatalog? catalog = null)
    {
        if (parameters is null) return new DecodedCatalogSelection();

        var addOnValues = parameters.GetAll(QueryKeys.AddOns);
        var nullable = new string?[addOnValues.Count];
        for (var i = 0; i < addOnValues.Count; i++) nullable[i] = addOnValues[i];

        return new DecodedCatalogSelection
        {
            TierId = TrimmedOrNull(parameters.Get(QueryKeys.Tier)),
            PricingId = TrimmedOrNull(parameters.Get(QueryKeys.Pricing)),
            AddOnIds = ParseAddOnIdList(nullable, catalog)
        };
    }

    private static JsonLdOffer ToOffer(string name, decimal price, string currency, string? url)
    {
        return new JsonLdOffer
        {
            Type = "Offer",
            Name = name,
            Price = price.ToString("0.00", CultureInfo.InvariantCulture),
            PriceCurrency = currency,
            Url = url is { Length: > 0 } ? url : null
        };
    }

    private static T? ResolveOption<T>(List<T>? options, string? pricingId, string? billing)
        where T : class, ICatalogPriceOption
    {
        if (options is null || options.Count == 0) return null;

        if (pricingId is { Length: > 0 })
        {
            foreach (var option in options)
            {
                if (option is not null && string.Equals(option.Id, pricingId, StringComparison.Ordinal)) return option;
            }
        }

        if (billing is { Length: > 0 })
        {
            foreach (var option in options)
            {
                if (option is not null && MatchesBilling(option.BillingFrequency, billing)) return option;
            }
        }

        foreach (var option in options)
        {
            if (option is not null && option.IsDefault) return option;
        }

        var ordered = SortByDisplayOrder(options, option => option.DisplayOrder);
        return ordered.Count > 0 ? ordered[0] : null;
    }

    /// <summary>
    /// "Yearly", "Annual" and "Annually" are the same cycle to a customer, so a request for one
    /// matches an option priced under any of them.
    /// </summary>
    private static bool MatchesBilling(string? candidate, string wanted)
    {
        if (FormatHelpers.IsAnnualFrequency(wanted)) return FormatHelpers.IsAnnualFrequency(candidate);

        var left = candidate is null ? string.Empty : candidate.Trim();
        return string.Equals(left, wanted.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstCurrency(IReadOnlyList<AppTierModel>? tiers, IReadOnlyList<AppTierAddOnModel>? addOns)
    {
        if (tiers is not null)
        {
            foreach (var tier in tiers)
            {
                var currency = tier is null ? null : TrimmedOrNull(tier.Currency);
                if (currency is not null) return currency;
            }
        }

        if (addOns is not null)
        {
            foreach (var addOn in addOns)
            {
                var currency = addOn is null ? null : TrimmedOrNull(addOn.Currency);
                if (currency is not null) return currency;
            }
        }

        return null;
    }

    private static bool IsActive(string? status)
    {
        var trimmed = status is null ? string.Empty : status.Trim();
        return string.Equals(trimmed, ActiveTierStatus, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TrimmedOrNull(string? value)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string?[] ToNullableList(List<string>? values)
    {
        if (values is null) return new string?[0];

        var copy = new string?[values.Count];
        for (var i = 0; i < values.Count; i++) copy[i] = values[i];
        return copy;
    }

    private static List<string> SplitOnCommasAndWhitespace(string value)
    {
        // JS splits on /[\s,]+/ and trims each part; the parts are already trimmed here because
        // every whitespace character is itself a separator.
        var parts = new List<string>();
        var start = -1;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == ',' || char.IsWhiteSpace(c))
            {
                if (start >= 0)
                {
                    parts.Add(value.Substring(start, i - start));
                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = i;
            }
        }

        if (start >= 0) parts.Add(value.Substring(start));
        return parts;
    }

    /// <summary>
    /// Ascending by display order, stable — ties keep the order the server sent, matching
    /// <c>Array.prototype.sort</c>. <see cref="List{T}.Sort(Comparison{T})"/> is not stable, so the
    /// sort is written out; these lists hold a pricing page's worth of items.
    /// </summary>
    private static List<T> SortByDisplayOrder<T>(IReadOnlyList<T> items, Func<T, int> displayOrder)
    {
        var sorted = new List<T>(items.Count);
        foreach (var item in items) sorted.Add(item);

        for (var i = 1; i < sorted.Count; i++)
        {
            var item = sorted[i];
            var order = displayOrder(item);
            var j = i - 1;
            while (j >= 0 && displayOrder(sorted[j]) > order)
            {
                sorted[j + 1] = sorted[j];
                j--;
            }
            sorted[j + 1] = item;
        }

        return sorted;
    }
}
