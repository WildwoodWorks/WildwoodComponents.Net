using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;

namespace WildwoodComponents.Razor.Components.AppTier;

/// <summary>
/// ViewComponent that renders a complete app tier selection and subscription UI.
/// Client-side JavaScript handles state transitions, billing cycle toggles, and AJAX calls.
/// Razor Pages equivalent of WildwoodComponents.Blazor AppTierComponent.
/// </summary>
public class AppTierViewComponent : ViewComponent
{
    private readonly IWildwoodAppTierService _appTierService;
    private readonly ILogger<AppTierViewComponent> _logger;

    public AppTierViewComponent(IWildwoodAppTierService appTierService, ILogger<AppTierViewComponent> logger)
    {
        _appTierService = appTierService;
        _logger = logger;
    }

    /// <summary>
    /// Renders the app tier component
    /// </summary>
    /// <param name="appId">Required. The application ID to load tiers for.</param>
    /// <param name="proxyBaseUrl">Base URL for the app tier proxy endpoints (default: /api/wildwood-app-tiers)</param>
    /// <param name="title">Header title displayed above tiers</param>
    /// <param name="subtitle">Header subtitle</param>
    /// <param name="showAddOns">Whether to show the add-ons section</param>
    /// <param name="showBillingToggle">Whether to show the monthly/annual toggle</param>
    /// <param name="showCurrentPlan">Whether to show the current subscription info</param>
    /// <param name="currency">Currency code for price display (default: USD)</param>
    /// <param name="annualDiscount">Percentage discount for annual billing (default: 20)</param>
    /// <param name="allowRegistration">Whether to allow registration flow before subscription</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string appId,
        string proxyBaseUrl = "/api/wildwood-app-tiers",
        string? title = null,
        string? subtitle = null,
        bool showAddOns = true,
        bool showBillingToggle = true,
        bool showCurrentPlan = true,
        string currency = "USD",
        int annualDiscount = 20,
        bool allowRegistration = false)
    {
        var tiers = await _appTierService.GetAvailableTiersAsync(appId);

        // Sort by display order
        tiers.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));

        UserTierSubscriptionModel? currentSubscription = null;
        List<AppTierAddOnModel> addOns = new();
        List<UserAddOnSubscriptionModel> myAddOns = new();

        try
        {
            currentSubscription = await _appTierService.GetMySubscriptionAsync(appId);

            if (showAddOns)
            {
                addOns = await _appTierService.GetAvailableAddOnsAsync(appId);
                myAddOns = await _appTierService.GetMyAddOnsAsync(appId);
            }
        }
        catch (Exception ex)
        {
            // User may not be authenticated - tiers still show for browsing
            _logger.LogDebug(ex, "Failed to load subscription/add-on data for app {AppId} (user may not be authenticated)", appId);
        }

        var model = new AppTierViewModel
        {
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            Title = title,
            Subtitle = subtitle,
            ShowAddOns = showAddOns,
            ShowBillingToggle = showBillingToggle,
            ShowCurrentPlan = showCurrentPlan,
            Currency = currency,
            AnnualDiscount = annualDiscount,
            AllowRegistration = allowRegistration,
            Tiers = tiers,
            CurrentSubscription = currentSubscription,
            AddOns = addOns,
            MyAddOns = myAddOns,
            HasMonthlyAndAnnual = HasMonthlyAndAnnualPricing(tiers)
        };

        return View(model);
    }

    private static bool HasMonthlyAndAnnualPricing(List<AppTierModel> tiers)
    {
        foreach (var tier in tiers)
        {
            bool hasMonthly = false;
            bool hasAnnual = false;
            foreach (var p in tier.PricingOptions)
            {
                if (string.Equals(p.BillingFrequency, "Monthly", StringComparison.OrdinalIgnoreCase))
                    hasMonthly = true;
                if (string.Equals(p.BillingFrequency, "Annually", StringComparison.OrdinalIgnoreCase))
                    hasAnnual = true;
            }
            if (hasMonthly && hasAnnual) return true;
        }
        return false;
    }
}
