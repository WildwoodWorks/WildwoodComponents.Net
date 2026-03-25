using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Components.Subscription.Admin;

/// <summary>
/// Displays usage limits with progress bars and admin edit/reset actions.
/// Razor Pages equivalent of WildwoodComponents.Blazor UsageLimitsPanel.
/// </summary>
public class UsageLimitsPanelViewComponent : ViewComponent
{
    private readonly IWildwoodAppTierService _appTierService;
    private readonly ILogger<UsageLimitsPanelViewComponent> _logger;

    public UsageLimitsPanelViewComponent(IWildwoodAppTierService appTierService, ILogger<UsageLimitsPanelViewComponent> logger)
    {
        _appTierService = appTierService;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync(
        string componentId,
        string appId,
        string proxyBaseUrl,
        bool isAdmin = false,
        bool isCompanyMode = false,
        string? companyId = null,
        string? userId = null)
    {
        var limitStatuses = new List<AppTierLimitStatusModel>();

        var useCompanyScope = isCompanyMode && !string.IsNullOrEmpty(companyId);
        var useUserScope = !isCompanyMode && !string.IsNullOrEmpty(userId);

        try
        {
            if (useCompanyScope)
                limitStatuses = await _appTierService.GetCompanyLimitStatusesAsync(appId, companyId!);
            else if (useUserScope)
                limitStatuses = await _appTierService.GetUserLimitStatusesAsync(appId, userId!);
            else
                limitStatuses = await _appTierService.GetAllLimitStatusesAsync(appId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load limit statuses for app {AppId}", appId);
        }

        var model = new UsageLimitsPanelViewModel
        {
            ComponentId = componentId,
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl,
            IsAdmin = isAdmin,
            LimitStatuses = limitStatuses
        };

        return View(model);
    }
}
