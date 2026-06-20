using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Server-side consent helper. The consent endpoints (<c>GET /api/consent/config</c> and
/// <c>POST /api/consent/record</c>) are anonymous, and the ConsentBanner's client engine can talk to
/// them directly (like the Blazor component). This service backs the optional same-origin proxy
/// (see the ConsentBanner README) so host apps can avoid cross-origin CORS.
/// </summary>
public interface IWildwoodConsentService
{
    /// <summary>
    /// Fetch the merged consent config as raw JSON (anonymous) for same-origin proxy passthrough.
    /// Returns the body verbatim so the third-party script registry (src/snippet/load options) the
    /// client engine needs is preserved (a typed model would drop it).
    /// </summary>
    Task<string?> GetConfigRawAsync(string appId);

    /// <summary>Record a consent decision (anonymous). Best-effort — never throws.</summary>
    Task RecordDecisionAsync(ConsentRecordModel record);
}
