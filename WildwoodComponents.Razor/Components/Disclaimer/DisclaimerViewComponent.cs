using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Components.Disclaimer;

/// <summary>
/// ViewComponent that renders a disclaimer acceptance UI for pending disclaimers.
/// Shows disclaimer content with accept checkboxes, supports HTML content format,
/// and handles bulk acceptance. Client-side JavaScript handles AJAX calls.
/// Razor Pages equivalent of WildwoodComponents.Blazor DisclaimerComponent.
/// </summary>
public class DisclaimerViewComponent : ViewComponent
{
    private readonly IWildwoodDisclaimerService _disclaimerService;
    private readonly ILogger<DisclaimerViewComponent> _logger;

    public DisclaimerViewComponent(IWildwoodDisclaimerService disclaimerService, ILogger<DisclaimerViewComponent> logger)
    {
        _disclaimerService = disclaimerService;
        _logger = logger;
    }

    /// <summary>
    /// Renders the disclaimer component
    /// </summary>
    /// <param name="appId">Required. The application ID to load disclaimers for.</param>
    /// <param name="userId">Optional user ID for user-specific disclaimers.</param>
    /// <param name="proxyBaseUrl">Base URL for the disclaimer proxy endpoints (default: /api/wildwood-disclaimer)</param>
    /// <param name="mode">Display mode: "login" or "settings" (default: login)</param>
    /// <param name="showCancelButton">Whether to show the cancel button (default: true)</param>
    /// <param name="preloadedDisclaimers">Pre-loaded disclaimers to skip the API call</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string appId,
        string? userId = null,
        string proxyBaseUrl = "/api/wildwood-disclaimer",
        string mode = "login",
        bool showCancelButton = true,
        List<PendingDisclaimerModel>? preloadedDisclaimers = null)
    {
        List<PendingDisclaimerModel> disclaimers;
        if (preloadedDisclaimers != null && preloadedDisclaimers.Count > 0)
        {
            disclaimers = preloadedDisclaimers;
        }
        else
        {
            try
            {
                var result = await _disclaimerService.GetPendingDisclaimersAsync(appId, userId, mode);
                disclaimers = result?.Disclaimers ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load pending disclaimers for app {AppId}", appId);
                disclaimers = new();
            }
        }

        var model = new DisclaimerViewModel
        {
            AppId = appId,
            UserId = userId,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            Mode = mode,
            ShowCancelButton = showCancelButton,
            Disclaimers = disclaimers
        };
        return View(model);
    }
}
