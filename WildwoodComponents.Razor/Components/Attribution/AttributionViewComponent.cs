using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Extensions;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Components.Attribution;

/// <summary>
/// ViewComponent that loads the Campaign Attribution engine (attribution.js). Campaign Attribution has no UI: the
/// engine captures the UTM tags, click id and referrer from the landing URL, keeps a first and a last touch (persisted
/// in the browser only once the app's consent category is granted), beacons the landing when the app has the beacon
/// on, and exposes <c>window.wildwoodAttribution</c>, which the registration scripts attach to every signup.
///
/// Razor Pages equivalent of WildwoodComponents.Blazor AttributionBootstrap. Render it once per page, as early as
/// possible, usually in the layout: <c>&lt;vc:attribution app-id="my-app" /&gt;</c>.
/// </summary>
public class AttributionViewComponent : ViewComponent
{
    private readonly WildwoodComponentsRazorOptions _options;
    private readonly ILogger<AttributionViewComponent> _logger;

    public AttributionViewComponent(WildwoodComponentsRazorOptions options, ILogger<AttributionViewComponent> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Renders the engine's script tag.
    /// </summary>
    /// <param name="appId">The app whose attribution config to load. Defaults to the configured AppId.</param>
    /// <param name="baseUrl">Optional API host root override (defaults to the configured WildwoodAPI base).</param>
    public Task<IViewComponentResult> InvokeAsync(string? appId = null, string? baseUrl = null)
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
            BaseUrl = UrlHelpers.StripApiSuffix(string.IsNullOrWhiteSpace(baseUrl) ? _options.BaseUrl : baseUrl)
        };

        return Task.FromResult<IViewComponentResult>(View(model));
    }
}
