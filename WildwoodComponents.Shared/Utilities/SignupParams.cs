using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Everything a signup link can carry, ported from
/// packages/wildwood-core/src/features/signupParams.ts. Every value is trimmed; an empty one
/// becomes null.
/// </summary>
/// <remarks>
/// Pure: the caller supplies the query string (from a request URL, a Razor
/// <c>HttpContext.Request.QueryString</c>, or Blazor's <c>NavigationManager.Uri</c>), so nothing
/// here reads a location itself.
/// </remarks>
public class SignupParams
{
    /// <summary>The tier the visitor arrived wanting (<c>?tier=</c>).</summary>
    public string? TierId { get; set; }

    /// <summary>The pricing option within that tier (<c>?pricing=</c>).</summary>
    public string? PricingId { get; set; }

    /// <summary>Packs to pre-select (<c>?addons=</c>), de-duplicated and capped. Always a list.</summary>
    public List<string> AddOnIds { get; set; } = new();

    /// <summary>A registration token (<c>?token=</c>).</summary>
    public string? Token { get; set; }

    /// <summary>An invitation id (<c>?invite=</c>).</summary>
    public string? Invite { get; set; }

    /// <summary>An email to prefill (<c>?email=</c>).</summary>
    public string? Email { get; set; }

    /// <summary>
    /// Parses a signup URL's query into the selection and identity hints a registration screen
    /// needs. Accepts a full URL, a query string with or without its leading <c>?</c>, or null.
    /// </summary>
    /// <param name="searchOrUrl">The query the visitor arrived with.</param>
    /// <param name="catalog">
    /// Pass the catalog once it has loaded to drop add-on ids the app does not sell — without it
    /// the ids are returned as given, which is what a screen that parses before fetching wants.
    /// </param>
    public static SignupParams Parse(string? searchOrUrl, PublicCatalog? catalog = null)
    {
        return Parse(CatalogHelpers.AsSearchParams(searchOrUrl), catalog);
    }

    /// <summary>
    /// Parses already-parsed parameters, for a caller that has a query collection rather than a
    /// string (JS <c>parseSignupParams</c> takes a <c>URLSearchParams</c> the same way).
    /// </summary>
    public static SignupParams Parse(QueryParameters? parameters, PublicCatalog? catalog = null)
    {
        if (parameters is null) return new SignupParams();

        var selection = CatalogHelpers.DecodeCatalogSelection(parameters, catalog);

        return new SignupParams
        {
            TierId = selection.TierId,
            PricingId = selection.PricingId,
            AddOnIds = selection.AddOnIds,
            Token = ReadParam(parameters, "token"),
            Invite = ReadParam(parameters, "invite"),
            Email = ReadParam(parameters, "email")
        };
    }

    private static string? ReadParam(QueryParameters parameters, string key)
    {
        var value = parameters.Get(key);
        if (value is null) return null;

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
