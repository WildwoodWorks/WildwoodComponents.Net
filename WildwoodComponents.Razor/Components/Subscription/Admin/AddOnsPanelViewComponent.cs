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
        UserTierSubscriptionModel? subscription = null,
        bool allowCancel = true,
        bool allowReactivate = true,
        string regSubProxyUrl = "/api/wildwood-regsub",
        bool showAddPacks = false,
        List<UserAddOnSubscriptionModel>? activeAddOnsOverride = null,
        List<AppTierAddOnModel>? availableAddOnsOverride = null)
    {
        var activeAddOns = activeAddOnsOverride ?? new List<UserAddOnSubscriptionModel>();
        var availableAddOns = availableAddOnsOverride ?? new List<AppTierAddOnModel>();

        var useCompanyScope = isCompanyMode && !string.IsNullOrEmpty(companyId);
        var useUserScope = !isCompanyMode && !string.IsNullOrEmpty(userId);

        // A caller that already read these hands them in: the manage view does, so its pack
        // picker and these rows cannot disagree about what the account owns, and one render does
        // not make the same two calls twice.
        if (activeAddOnsOverride is null || availableAddOnsOverride is null)
        {
            try
            {
                if (activeAddOnsOverride is null)
                {
                    if (useCompanyScope)
                        activeAddOns = await _appTierService.GetCompanyAddOnSubscriptionsAsync(appId, companyId!);
                    else if (useUserScope)
                        activeAddOns = await _appTierService.GetUserAddOnsAsync(appId, userId!);
                    else
                        activeAddOns = await _appTierService.GetMyAddOnsAsync(appId);
                }

                if (availableAddOnsOverride is null)
                {
                    availableAddOns = isAdmin
                        ? await _appTierService.GetAllAddOnsAsync(appId)
                        : await _appTierService.GetAvailableAddOnsAsync(appId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load add-ons for app {AppId}", appId);
            }
        }

        var model = new AddOnsPanelViewModel
        {
            ComponentId = componentId,
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl,
            RegSubProxyUrl = regSubProxyUrl,
            IsAdmin = isAdmin,
            Currency = currency,
            CurrentTierId = subscription?.AppTierId,
            ActiveAddOns = activeAddOns,
            AvailableAddOns = availableAddOns,
            AllowCancel = allowCancel,
            // Reactivate is the signed-in user's own action: the shipped proxy route acts as that
            // user and there is no company- or admin-scoped equivalent to offer.
            AllowReactivate = allowReactivate && !useCompanyScope && !useUserScope,
            // "Add packs" opens the manage view's picker. It is the caller's to offer: this panel
            // renders no picker of its own, so a surface without one must not show the button.
            ShowAddPacks = showAddPacks && !useCompanyScope && !useUserScope
        };

        return View(model);
    }
}
