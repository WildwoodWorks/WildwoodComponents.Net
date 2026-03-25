using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Components.Subscription.Admin;

/// <summary>
/// Displays available tier plans with billing toggle and select buttons.
/// Razor Pages equivalent of WildwoodComponents.Blazor TierPlansPanel.
/// </summary>
public class TierPlansPanelViewComponent : ViewComponent
{
    private readonly IWildwoodAppTierService _appTierService;
    private readonly ILogger<TierPlansPanelViewComponent> _logger;

    public TierPlansPanelViewComponent(IWildwoodAppTierService appTierService, ILogger<TierPlansPanelViewComponent> logger)
    {
        _appTierService = appTierService;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync(
        string componentId,
        string appId,
        string proxyBaseUrl,
        string currency = "USD",
        bool showBillingToggle = true,
        bool isAdmin = false,
        UserTierSubscriptionModel? subscription = null)
    {
        var tiers = new List<AppTierModel>();
        try
        {
            tiers = await _appTierService.GetAvailableTiersAsync(appId);
            tiers.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tiers for app {AppId}", appId);
        }

        var model = new TierPlansPanelViewModel
        {
            ComponentId = componentId,
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl,
            Currency = currency,
            ShowBillingToggle = showBillingToggle,
            IsAdmin = isAdmin,
            Tiers = tiers,
            Subscription = subscription,
            HasMonthlyAndAnnual = HasMonthlyAndAnnualPricing(tiers)
        };

        return View(model);
    }

    private static bool HasMonthlyAndAnnualPricing(List<AppTierModel> tiers)
    {
        foreach (var tier in tiers)
        {
            bool hasMonthly = false, hasAnnual = false;
            foreach (var p in tier.PricingOptions)
            {
                if (string.Equals(p.BillingFrequency, "Monthly", StringComparison.OrdinalIgnoreCase)) hasMonthly = true;
                if (string.Equals(p.BillingFrequency, "Annually", StringComparison.OrdinalIgnoreCase)) hasAnnual = true;
            }
            if (hasMonthly && hasAnnual) return true;
        }
        return false;
    }
}
