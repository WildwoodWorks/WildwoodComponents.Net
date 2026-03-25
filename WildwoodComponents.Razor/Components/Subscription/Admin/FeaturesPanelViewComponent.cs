using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Components.Subscription.Admin;

/// <summary>
/// Displays features grouped by category with enabled/locked status and admin toggle.
/// Razor Pages equivalent of WildwoodComponents.Blazor FeaturesPanel.
/// </summary>
public class FeaturesPanelViewComponent : ViewComponent
{
    private readonly IWildwoodAppTierService _appTierService;
    private readonly ILogger<FeaturesPanelViewComponent> _logger;

    public FeaturesPanelViewComponent(IWildwoodAppTierService appTierService, ILogger<FeaturesPanelViewComponent> logger)
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
        var definitions = new List<AppFeatureDefinitionModel>();
        var featureAccess = new Dictionary<string, bool>();
        var overrides = new List<AppFeatureOverrideModel>();

        var useCompanyScope = isCompanyMode && !string.IsNullOrEmpty(companyId);
        var useUserScope = !isCompanyMode && !string.IsNullOrEmpty(userId);

        try
        {
            definitions = await _appTierService.GetFeatureDefinitionsAsync(appId);

            if (useCompanyScope)
                featureAccess = await _appTierService.GetCompanyFeaturesAsync(appId, companyId!);
            else if (useUserScope)
                featureAccess = await _appTierService.GetUserFeaturesAdminAsync(appId, userId!);
            else
                featureAccess = await _appTierService.GetUserFeaturesAsync(appId);

            if (isAdmin)
            {
                var scopeUserId = useUserScope ? userId : null;
                overrides = await _appTierService.GetFeatureOverridesAsync(appId, scopeUserId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load features for app {AppId}", appId);
        }

        // Build grouped view model
        var overrideLookup = overrides.ToDictionary(o => o.FeatureCode, StringComparer.OrdinalIgnoreCase);
        var featureItems = definitions
            .OrderBy(d => d.Category)
            .ThenBy(d => d.DisplayOrder)
            .Select(d =>
            {
                featureAccess.TryGetValue(d.FeatureCode, out var isEnabled);
                overrideLookup.TryGetValue(d.FeatureCode, out var featureOverride);
                return new FeatureItemViewModel
                {
                    FeatureCode = d.FeatureCode,
                    DisplayName = d.DisplayName,
                    Description = d.Description,
                    Category = d.Category,
                    IconClass = d.IconClass,
                    IsEnabled = isEnabled,
                    HasOverride = featureOverride != null,
                    Override = featureOverride
                };
            })
            .ToList();

        var groups = featureItems
            .GroupBy(f => string.IsNullOrEmpty(f.Category) ? "General" : f.Category)
            .Select(g => new FeatureGroupViewModel { Category = g.Key, Features = g.ToList() })
            .ToList();

        var model = new FeaturesPanelViewModel
        {
            ComponentId = componentId,
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl,
            IsAdmin = isAdmin,
            FeatureGroups = groups,
            Overrides = overrides
        };

        return View(model);
    }
}
