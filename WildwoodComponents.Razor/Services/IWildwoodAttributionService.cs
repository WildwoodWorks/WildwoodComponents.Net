using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Campaign Attribution claim for a signed-in session: the server-side half of the /api/wildwood-attribution proxy.
/// attribution.js holds the captured touches in the browser while a Razor app keeps the user's JWT in the server
/// session, so the browser posts its payload to the proxy and this service forwards it to WildwoodAPI with the
/// session token. That is how a sign-in provider signup, which has no registration request, is attributed.
/// </summary>
public interface IWildwoodAttributionService
{
    /// <summary>True when the last <see cref="ClaimAsync"/> was rejected by WildwoodAPI with a 401 (a dead session token).</summary>
    bool LastResponseUnauthorized { get; }

    /// <summary>
    /// Claims <paramref name="payload"/> for the signed-in user via <c>POST attribution/claim?appId=</c>, always with the
    /// configured app id (never one supplied by the browser). Returns null without a request when the session is not
    /// authenticated, no app id is configured, or the payload has no touch; null as well for an error status or a
    /// failed request. Never throws.
    /// </summary>
    Task<AttributionClaimResultModel?> ClaimAsync(AttributionPayloadModel? payload, CancellationToken cancellationToken = default);
}
