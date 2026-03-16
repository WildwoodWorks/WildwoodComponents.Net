using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Components.Usage;

/// <summary>
/// ViewComponent that renders a usage dashboard showing subscription limits and current usage.
/// Displays progress bars colored by threshold with overage warnings.
/// Razor Pages equivalent of WildwoodComponents.Blazor UsageDashboardComponent.
/// </summary>
public class UsageDashboardViewComponent : ViewComponent
{
    private readonly IWildwoodAppTierService _appTierService;
    private readonly ILogger<UsageDashboardViewComponent> _logger;

    public UsageDashboardViewComponent(IWildwoodAppTierService appTierService, ILogger<UsageDashboardViewComponent> logger)
    {
        _appTierService = appTierService;
        _logger = logger;
    }

    /// <summary>
    /// Renders the usage dashboard component
    /// </summary>
    /// <param name="appId">Required. The application ID to load usage data for.</param>
    /// <param name="proxyBaseUrl">Base URL for the app tier proxy endpoints (default: /api/wildwood-app-tiers)</param>
    /// <param name="title">Header title displayed above the dashboard</param>
    /// <param name="subtitle">Header subtitle</param>
    /// <param name="showOverageInfo">Whether to show overage warnings</param>
    /// <param name="warningThreshold">Percentage threshold for warning state (default: 80)</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string appId,
        string proxyBaseUrl = "/api/wildwood-app-tiers",
        string? title = null,
        string? subtitle = null,
        bool showOverageInfo = true,
        int warningThreshold = 80)
    {
        var limitStatuses = new List<AppTierLimitStatusModel>();
        UserTierSubscriptionModel? subscription = null;
        try
        {
            limitStatuses = await _appTierService.GetAllLimitStatusesAsync(appId);
            subscription = await _appTierService.GetMySubscriptionAsync(appId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load usage data for app {AppId}", appId);
        }

        var model = new UsageDashboardViewModel
        {
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            Title = title,
            Subtitle = subtitle,
            ShowOverageInfo = showOverageInfo,
            WarningThreshold = warningThreshold,
            Subscription = subscription,
            LimitStatuses = limitStatuses
        };
        return View(model);
    }
}
