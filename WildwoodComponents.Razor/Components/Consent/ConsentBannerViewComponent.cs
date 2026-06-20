using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Extensions;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Components.Consent;

/// <summary>
/// ViewComponent that renders a consent banner + preferences modal with category toggles and CCPA
/// opt-out surfaces. Mirrors the Blazor ConsentBanner. The server renders the shell; client-side
/// JavaScript (consent.js) owns the cookie/GPC/decision/script-injection engine and records the
/// decision against the anonymous consent endpoints. Block-before-consent is enforced in the engine.
///
/// Razor Pages equivalent of WildwoodComponents.Blazor ConsentBanner.
/// Usage: <c>&lt;vc:consent-banner app-id="my-app" /&gt;</c> (direct mode), or
/// <c>&lt;vc:consent-banner app-id="my-app" proxy-base-url="/api/wildwood-consent" /&gt;</c> to route
/// the engine through a same-origin proxy (see README.md) and avoid cross-origin CORS.
/// </summary>
public class ConsentBannerViewComponent : ViewComponent
{
    private readonly WildwoodComponentsRazorOptions _options;
    private readonly ILogger<ConsentBannerViewComponent> _logger;

    public ConsentBannerViewComponent(WildwoodComponentsRazorOptions options, ILogger<ConsentBannerViewComponent> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Renders the consent banner shell.
    /// </summary>
    /// <param name="appId">Required. The app whose consent config + script registry to load.</param>
    /// <param name="baseUrl">Optional API host root override (defaults to the configured WildwoodAPI base). Ignored in proxy mode.</param>
    /// <param name="proxyBaseUrl">Optional same-origin proxy base (e.g. /api/wildwood-consent). When set, the engine calls the proxy instead of the API directly.</param>
    /// <param name="showReopenLink">Render a footer "Privacy choices" reopen link (default: true).</param>
    /// <param name="showFooterOptOut">Render standalone CCPA opt-out footer links (default: true).</param>
    public Task<IViewComponentResult> InvokeAsync(
        string appId,
        string? baseUrl = null,
        string? proxyBaseUrl = null,
        bool showReopenLink = true,
        bool showFooterOptOut = true)
    {
        if (string.IsNullOrEmpty(appId))
        {
            _logger.LogWarning("ConsentBannerViewComponent requires an appId; banner will not initialize.");
        }

        var model = new ConsentViewModel
        {
            AppId = appId ?? string.Empty,
            // The client engine appends /api/consent/..., so hand it the host root (no /api suffix).
            BaseUrl = UrlHelpers.StripApiSuffix(string.IsNullOrEmpty(baseUrl) ? _options.BaseUrl : baseUrl),
            ProxyBaseUrl = (proxyBaseUrl ?? string.Empty).TrimEnd('/'),
            ShowReopenLink = showReopenLink,
            ShowFooterOptOut = showFooterOptOut
        };

        return Task.FromResult<IViewComponentResult>(View(model));
    }
}
