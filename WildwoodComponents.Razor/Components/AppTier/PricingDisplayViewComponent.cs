using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Components.AppTier;

/// <summary>
/// ViewComponent that renders a read-only pricing/tier display (no purchase flow).
/// Shows tier cards with pricing, features, and limits but no subscribe buttons.
/// Client-side JavaScript handles billing cycle toggle.
/// Razor Pages equivalent of WildwoodComponents.Blazor PricingDisplayComponent.
/// </summary>
public class PricingDisplayViewComponent : ViewComponent
{
    private readonly IWildwoodAppTierService _appTierService;
    private readonly ILogger<PricingDisplayViewComponent> _logger;

    public PricingDisplayViewComponent(IWildwoodAppTierService appTierService, ILogger<PricingDisplayViewComponent> logger)
    {
        _appTierService = appTierService;
        _logger = logger;
    }

    /// <summary>
    /// Renders the pricing display component
    /// </summary>
    /// <param name="appId">Required. The application ID to load tiers for.</param>
    /// <param name="proxyBaseUrl">Base URL for app tier proxy endpoints (default: /api/wildwood-app-tiers)</param>
    /// <param name="title">Header title displayed above tiers</param>
    /// <param name="subtitle">Header subtitle</param>
    /// <param name="showBillingToggle">Whether to show the monthly/annual toggle (default: true)</param>
    /// <param name="showFeatureComparison">Whether to show the feature comparison matrix (default: true)</param>
    /// <param name="showLimits">Whether to show tier limits (default: true)</param>
    /// <param name="currency">Currency code for price display (default: USD)</param>
    /// <param name="enterpriseContactUrl">URL for enterprise contact link</param>
    /// <param name="selectTierUrl">URL for tier selection CTA (renders as link). If null, dispatches CustomEvent instead.</param>
    /// <param name="preSelectedTierId">Pre-selected tier to highlight</param>
    /// <param name="preloadedTiers">Pre-loaded tiers (skip API call)</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string appId,
        string proxyBaseUrl = "/api/wildwood-app-tiers",
        string? title = null,
        string? subtitle = null,
        bool showBillingToggle = true,
        bool showFeatureComparison = true,
        bool showLimits = true,
        string currency = "USD",
        string? enterpriseContactUrl = null,
        string? selectTierUrl = null,
        string? preSelectedTierId = null,
        List<AppTierModel>? preloadedTiers = null)
    {
        List<AppTierModel> tiers;

        if (preloadedTiers != null && preloadedTiers.Count > 0)
        {
            tiers = preloadedTiers;
        }
        else
        {
            try
            {
                tiers = await _appTierService.GetAvailableTiersAsync(appId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load tiers for app {AppId}", appId);
                tiers = new();
            }
        }

        tiers = tiers.OrderBy(t => t.DisplayOrder).ToList();

        var model = new PricingDisplayViewModel
        {
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            Title = title,
            Subtitle = subtitle,
            ShowBillingToggle = showBillingToggle,
            ShowFeatureComparison = showFeatureComparison,
            ShowLimits = showLimits,
            Currency = currency,
            EnterpriseContactUrl = enterpriseContactUrl,
            SelectTierUrl = selectTierUrl,
            PreSelectedTierId = preSelectedTierId,
            Tiers = tiers,
            HasMonthlyAndAnnual = HasBothBillingCycles(tiers)
        };

        return View(model);
    }

    private static bool HasBothBillingCycles(List<AppTierModel> tiers)
    {
        foreach (var tier in tiers)
        {
            bool hasMonthly = false, hasAnnual = false;
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
