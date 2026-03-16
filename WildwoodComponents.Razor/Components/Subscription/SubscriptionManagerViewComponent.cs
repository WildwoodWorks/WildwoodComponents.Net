using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;

namespace WildwoodComponents.Razor.Components.Subscription;

/// <summary>
/// ViewComponent for managing an existing subscription (upgrade, cancel, view invoices).
/// Client-side JavaScript handles subscription actions and AJAX calls.
/// Razor Pages equivalent of WildwoodComponents.Blazor SubscriptionManagerComponent.
/// </summary>
public class SubscriptionManagerViewComponent : ViewComponent
{
    private readonly IWildwoodSubscriptionService _subscriptionService;
    private readonly ILogger<SubscriptionManagerViewComponent> _logger;

    public SubscriptionManagerViewComponent(IWildwoodSubscriptionService subscriptionService, ILogger<SubscriptionManagerViewComponent> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    /// <summary>
    /// Renders the subscription manager component
    /// </summary>
    /// <param name="appId">Required. The application ID.</param>
    /// <param name="proxyBaseUrl">Base URL for subscription proxy endpoints (default: /api/wildwood-subscription)</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string appId,
        string proxyBaseUrl = "/api/wildwood-subscription")
    {
        SubscriptionDto? currentSub = null;
        List<SubscriptionPlanDto> plans = new();
        List<InvoiceDto> invoices = new();

        try
        {
            currentSub = await _subscriptionService.GetCurrentSubscriptionAsync();
            plans = await _subscriptionService.GetAvailablePlansAsync();

            if (currentSub != null)
                invoices = await _subscriptionService.GetInvoicesAsync(currentSub.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load subscription data (user may not be authenticated)");
        }

        var model = new SubscriptionManagerViewModel
        {
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            CurrentSubscription = currentSub,
            Plans = plans,
            Invoices = invoices
        };

        return View(model);
    }
}
