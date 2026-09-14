using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Extensions;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Components.Attribution;

/// <summary>
/// ViewComponent that loads the Campaign Attribution engine (attribution.js). Campaign Attribution has no UI: the
/// engine captures the UTM tags, click id and referrer from the landing URL, keeps a first and a last touch (persisted
/// in the browser only once the app's consent category is granted), beacons the landing when the app has the beacon
/// on, and exposes <c>window.wildwoodAttribution</c>, which the registration scripts attach to every signup. For a
/// signed-in session it also hands the engine the claim proxy route, so a sign-in provider signup is attributed.
///
/// Razor Pages equivalent of WildwoodComponents.Blazor AttributionBootstrap. Render it once per page, as early as
/// possible, usually in the layout: <c>&lt;vc:attribution app-id="my-app" /&gt;</c>.
/// </summary>
public class AttributionViewComponent : ViewComponent
{
    /// <summary>The route <c>WildwoodAttributionProxyController</c> serves.</summary>
    public const string DefaultClaimUrl = "/api/wildwood-attribution/claim";

    private readonly WildwoodComponentsRazorOptions _options;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<AttributionViewComponent> _logger;

    public AttributionViewComponent(
        WildwoodComponentsRazorOptions options,
        IWildwoodSessionManager sessionManager,
        ILogger<AttributionViewComponent> logger)
    {
        _options = options;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Renders the engine's script tag.
    /// </summary>
    /// <param name="appId">The app whose attribution config to load. Defaults to the configured AppId.</param>
    /// <param name="baseUrl">Optional API host root override (defaults to the configured WildwoodAPI base).</param>
    /// <param name="enableClaim">For a signed-in session, let the engine claim its touches through the proxy (default: true).</param>
    /// <param name="claimUrl">Optional claim proxy route (defaults to <see cref="DefaultClaimUrl"/>).</param>
    public Task<IViewComponentResult> InvokeAsync(
        string? appId = null,
        string? baseUrl = null,
        bool enableClaim = true,
        string? claimUrl = null)
    {
        var resolvedAppId = string.IsNullOrWhiteSpace(appId) ? _options.AppId : appId;
        if (string.IsNullOrWhiteSpace(resolvedAppId))
        {
            _logger.LogWarning("AttributionViewComponent has no appId (attribute or configured AppId); attribution capture will not start.");
        }

        var model = new AttributionViewModel
        {
            AppId = resolvedAppId?.Trim() ?? string.Empty,
            // The engine appends /api/attribution/..., so hand it the host root (no /api suffix).
            BaseUrl = UrlHelpers.StripApiSuffix(string.IsNullOrWhiteSpace(baseUrl) ? _options.BaseUrl : baseUrl),
            // Only a signed-in session has a token for the proxy to forward; anyone else would just get a 401.
            ClaimUrl = enableClaim && IsSignedIn()
                ? (string.IsNullOrWhiteSpace(claimUrl) ? DefaultClaimUrl : claimUrl.Trim())
                : null
        };

        return Task.FromResult<IViewComponentResult>(View(model));
    }

    private bool IsSignedIn()
    {
        try
        {
            return _sessionManager.IsAuthenticated;
        }
        catch (Exception ex)
        {
            // A host without server-side session: there is no token to forward, and capture must still render.
            _logger.LogDebug(ex, "AttributionViewComponent could not read the session; the claim is off for this page.");
            return false;
        }
    }
}
