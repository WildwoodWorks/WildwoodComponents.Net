using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Components.Usage;

/// <summary>
/// ViewComponent that renders a compact overage summary showing only limits
/// that are at warning threshold or exceeded. Alert banner style with upgrade button.
/// Razor Pages equivalent of WildwoodComponents.Blazor OverageSummaryComponent.
/// </summary>
public class OverageSummaryViewComponent : ViewComponent
{
    private readonly IWildwoodAppTierService _appTierService;
    private readonly ILogger<OverageSummaryViewComponent> _logger;

    public OverageSummaryViewComponent(IWildwoodAppTierService appTierService, ILogger<OverageSummaryViewComponent> logger)
    {
        _appTierService = appTierService;
        _logger = logger;
    }

    /// <summary>
    /// Renders the overage summary component
    /// </summary>
    /// <param name="appId">Required. The application ID to load usage data for.</param>
    /// <param name="proxyBaseUrl">Base URL for the app tier proxy endpoints (default: /api/wildwood-app-tiers)</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string appId,
        string proxyBaseUrl = "/api/wildwood-app-tiers")
    {
        var overages = new List<AppTierLimitStatusModel>();
        UserTierSubscriptionModel? subscription = null;
        try
        {
            var limitStatuses = await _appTierService.GetAllLimitStatusesAsync(appId);
            overages = limitStatuses.Where(l => l.IsExceeded || l.IsAtWarningThreshold).ToList();
            subscription = await _appTierService.GetMySubscriptionAsync(appId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load overage data for app {AppId}", appId);
        }

        var model = new OverageSummaryViewModel
        {
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            OverageLimits = overages,
            Subscription = subscription
        };
        return View(model);
    }
}
