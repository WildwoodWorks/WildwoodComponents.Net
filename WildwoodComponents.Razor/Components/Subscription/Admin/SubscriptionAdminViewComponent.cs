using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Components.Subscription.Admin;

/// <summary>
/// Admin ViewComponent that renders a tabbed subscription management UI.
/// Contains panels for status, plans, features, overrides, add-ons, and usage limits.
/// Razor Pages equivalent of WildwoodComponents.Blazor SubscriptionAdminComponent.
/// </summary>
public class SubscriptionAdminViewComponent : ViewComponent
{
    private readonly IWildwoodAppTierService _appTierService;
    private readonly ILogger<SubscriptionAdminViewComponent> _logger;

    public SubscriptionAdminViewComponent(IWildwoodAppTierService appTierService, ILogger<SubscriptionAdminViewComponent> logger)
    {
        _appTierService = appTierService;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync(
        string appId,
        string proxyBaseUrl = "/api/wildwood-app-tiers",
        string? companyId = null,
        string? userId = null,
        SubscriptionDisplayMode displayMode = SubscriptionDisplayMode.All,
        bool isAdmin = false,
        string currency = "USD",
        bool showBillingToggle = true,
        bool showStatusAboveTabs = false)
    {
        var componentId = Guid.NewGuid().ToString("N")[..8];
        var isCompanyMode = false;
        UserTierSubscriptionModel? subscription = null;
        int overrideCount = 0;

        try
        {
            var mode = await _appTierService.GetTrackingModeAsync(appId);
            isCompanyMode = string.Equals(mode, "Company", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tracking mode for app {AppId}", appId);
        }

        var useCompanyScope = isCompanyMode && !string.IsNullOrEmpty(companyId);
        var useUserScope = !isCompanyMode && !string.IsNullOrEmpty(userId);

        try
        {
            if (useCompanyScope)
                subscription = await _appTierService.GetCompanySubscriptionAsync(appId, companyId!);
            else if (useUserScope)
                subscription = await _appTierService.GetUserSubscriptionAsync(appId, userId!);
            else
                subscription = await _appTierService.GetMySubscriptionAsync(appId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load subscription for app {AppId}", appId);
        }

        if (isAdmin)
        {
            try
            {
                var scopeUserId = useUserScope ? userId : null;
                var overrides = await _appTierService.GetFeatureOverridesAsync(appId, scopeUserId);
                overrideCount = overrides.Count;
            }
            catch
            {
                overrideCount = 0;
            }
        }

        var model = new SubscriptionAdminViewModel
        {
            ComponentId = componentId,
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            CompanyId = companyId,
            UserId = userId,
            DisplayMode = displayMode,
            IsAdmin = isAdmin,
            Currency = currency,
            ShowBillingToggle = showBillingToggle,
            ShowStatusAboveTabs = showStatusAboveTabs,
            IsCompanyMode = isCompanyMode,
            Subscription = subscription,
            OverrideCount = overrideCount
        };

        return View(model);
    }
}
