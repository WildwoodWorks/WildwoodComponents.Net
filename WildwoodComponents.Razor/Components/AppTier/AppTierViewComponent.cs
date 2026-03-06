using Microsoft.AspNetCore.Mvc;
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

    public AppTierViewComponent(IWildwoodAppTierService appTierService)
    {
        _appTierService = appTierService;
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
        catch
        {
            // User may not be authenticated - tiers still show for browsing
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

public class AppTierViewModel
{
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-app-tiers";
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public bool ShowAddOns { get; set; } = true;
    public bool ShowBillingToggle { get; set; } = true;
    public bool ShowCurrentPlan { get; set; } = true;
    public string Currency { get; set; } = "USD";
    public int AnnualDiscount { get; set; } = 20;
    public bool AllowRegistration { get; set; }
    public List<AppTierModel> Tiers { get; set; } = new();
    public UserTierSubscriptionModel? CurrentSubscription { get; set; }
    public List<AppTierAddOnModel> AddOns { get; set; } = new();
    public List<UserAddOnSubscriptionModel> MyAddOns { get; set; } = new();
    public bool HasMonthlyAndAnnual { get; set; }
}
