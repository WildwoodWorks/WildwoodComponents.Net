using System.Text.Json.Serialization;

namespace WildwoodComponents.Shared.Models;

// The public catalog: what an app sells, as the server last said it. Ported from
// packages/wildwood-core/src/features/catalog.ts — the models only; the rules live in
// WildwoodComponents.Shared.Utilities.CatalogHelpers.
//
// Nothing here is a wire model. A catalog is built from the live public tier/add-on responses
// and never persisted, so no price is ever read back out of storage.

/// <summary>
/// The shape <see cref="Utilities.CatalogHelpers.ResolvePriceOption(AppTierModel, string, string)"/>
/// needs. Both tier pricing and add-on pricing satisfy it, which is how one resolution rule serves
/// plans and packs alike (JS gets this structurally from <c>CatalogPriceOption</c>).
/// </summary>
public interface ICatalogPriceOption
{
    string Id { get; }
    decimal Price { get; }
    string BillingFrequency { get; }
    bool IsDefault { get; }

    /// <summary>
    /// Add-on pricing carries no display order, so it reports 0 — the same value JS's
    /// <c>displayOrder ?? 0</c> gives it.
    /// </summary>
    int DisplayOrder { get; }

    int? TrialDays { get; }
}

/// <summary>
/// What an app sells, in display order, labelled with one currency.
/// </summary>
public class PublicCatalog
{
    public string AppId { get; set; } = string.Empty;

    /// <summary>ISO code every price in this catalog is quoted in.</summary>
    public string Currency { get; set; } = "USD";

    public List<AppTierModel> Tiers { get; set; } = new();

    public List<AppTierAddOnModel> AddOns { get; set; } = new();

    /// <summary>
    /// When the catalog was built, for staleness checks by the caller. JS carries the same moment
    /// as epoch milliseconds (<c>Date.now()</c>); .NET keeps it as a <see cref="DateTimeOffset"/>
    /// because nothing serialises a catalog.
    /// </summary>
    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>A tier/pack selection, as it travels in a URL.</summary>
public class CatalogSelection
{
    public string? TierId { get; set; }
    public string? PricingId { get; set; }
    public List<string> AddOnIds { get; set; } = new();
}

/// <summary>
/// A selection read back out of a URL. <see cref="AddOnIds"/> is always a list, possibly empty.
/// </summary>
public class DecodedCatalogSelection
{
    public string? TierId { get; set; }
    public string? PricingId { get; set; }
    public List<string> AddOnIds { get; set; } = new();
}

/// <summary>
/// A schema.org <c>Offer</c>, ready to drop into a JSON-LD graph. Razor writes the graph into the
/// page server-side and Blazor into a script tag, so this stays framework-neutral: a plain object
/// graph any serialiser can take.
/// </summary>
public class JsonLdOffer
{
    [JsonPropertyName("@type")]
    public string Type { get; set; } = "Offer";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The live price as invariant text, per schema.org — never a symbol, never a formatted amount.
    /// </summary>
    [JsonPropertyName("price")]
    public string Price { get; set; } = string.Empty;

    [JsonPropertyName("priceCurrency")]
    public string PriceCurrency { get; set; } = string.Empty;

    /// <summary>Canonical URL of the page the offer appears on; omitted when none was given.</summary>
    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }
}
