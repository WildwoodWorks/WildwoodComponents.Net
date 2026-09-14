namespace WildwoodComponents.Razor.Models;

/// <summary>
/// View model for the AttributionViewComponent (Campaign Attribution capture). Campaign Attribution has no UI: the
/// view only loads attribution.js with these values, and the engine starts itself from them.
/// </summary>
public class AttributionViewModel
{
    /// <summary>The app whose attribution config the engine loads. Empty leaves the engine uninitialized.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>WildwoodAPI host root without the <c>/api</c> suffix (the engine appends <c>/api/attribution/...</c>).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Same-origin claim proxy route, set only for a signed-in session. Null renders no <c>data-claim-url</c>, so the
    /// engine never claims.
    /// </summary>
    public string? ClaimUrl { get; set; }
}
