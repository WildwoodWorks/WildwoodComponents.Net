using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Components.Subscription.Admin;

/// <summary>
/// Displays a table of active feature overrides with remove/make-permanent actions.
/// Razor Pages equivalent of WildwoodComponents.Blazor OverridesPanel.
/// </summary>
public class OverridesPanelViewComponent : ViewComponent
{
    private readonly IWildwoodAppTierService _appTierService;
    private readonly ILogger<OverridesPanelViewComponent> _logger;

    public OverridesPanelViewComponent(IWildwoodAppTierService appTierService, ILogger<OverridesPanelViewComponent> logger)
    {
        _appTierService = appTierService;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync(
        string componentId,
        string appId,
        string proxyBaseUrl,
        bool isCompanyMode = false,
        string? userId = null)
    {
        var overrides = new List<AppFeatureOverrideModel>();

        try
        {
            var useUserScope = !isCompanyMode && !string.IsNullOrEmpty(userId);
            var scopeUserId = useUserScope ? userId : null;
            overrides = await _appTierService.GetFeatureOverridesAsync(appId, scopeUserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load overrides for app {AppId}", appId);
        }

        var model = new OverridesPanelViewModel
        {
            ComponentId = componentId,
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl,
            Overrides = overrides
        };

        return View(model);
    }
}
