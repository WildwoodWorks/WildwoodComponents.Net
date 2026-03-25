using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Components.Subscription.Admin;

/// <summary>
/// Displays active and available add-on subscriptions with subscribe/cancel actions.
/// Razor Pages equivalent of WildwoodComponents.Blazor AddOnsPanel.
/// </summary>
public class AddOnsPanelViewComponent : ViewComponent
{
    private readonly IWildwoodAppTierService _appTierService;
    private readonly ILogger<AddOnsPanelViewComponent> _logger;

    public AddOnsPanelViewComponent(IWildwoodAppTierService appTierService, ILogger<AddOnsPanelViewComponent> logger)
    {
        _appTierService = appTierService;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync(
        string componentId,
        string appId,
        string proxyBaseUrl,
        bool isAdmin = false,
        string currency = "USD",
        bool isCompanyMode = false,
        string? companyId = null,
        string? userId = null,
        UserTierSubscriptionModel? subscription = null)
    {
        var activeAddOns = new List<UserAddOnSubscriptionModel>();
        var availableAddOns = new List<AppTierAddOnModel>();

        var useCompanyScope = isCompanyMode && !string.IsNullOrEmpty(companyId);
        var useUserScope = !isCompanyMode && !string.IsNullOrEmpty(userId);

        try
        {
            if (useCompanyScope)
                activeAddOns = await _appTierService.GetCompanyAddOnSubscriptionsAsync(appId, companyId!);
            else if (useUserScope)
                activeAddOns = await _appTierService.GetUserAddOnsAsync(appId, userId!);
            else
                activeAddOns = await _appTierService.GetMyAddOnsAsync(appId);

            availableAddOns = isAdmin
                ? await _appTierService.GetAllAddOnsAsync(appId)
                : await _appTierService.GetAvailableAddOnsAsync(appId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load add-ons for app {AppId}", appId);
        }

        var model = new AddOnsPanelViewModel
        {
            ComponentId = componentId,
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl,
            IsAdmin = isAdmin,
            Currency = currency,
            CurrentTierId = subscription?.AppTierId,
            ActiveAddOns = activeAddOns,
            AvailableAddOns = availableAddOns
        };

        return View(model);
    }
}
