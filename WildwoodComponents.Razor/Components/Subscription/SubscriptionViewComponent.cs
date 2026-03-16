using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;

namespace WildwoodComponents.Razor.Components.Subscription;

/// <summary>
/// ViewComponent that renders subscription plan selection and management.
/// Client-side JavaScript handles billing cycle toggles, plan selection, and AJAX calls.
/// Razor Pages equivalent of WildwoodComponents.Blazor SubscriptionComponent.
/// </summary>
public class SubscriptionViewComponent : ViewComponent
{
    private readonly IWildwoodSubscriptionService _subscriptionService;
    private readonly ILogger<SubscriptionViewComponent> _logger;

    public SubscriptionViewComponent(IWildwoodSubscriptionService subscriptionService, ILogger<SubscriptionViewComponent> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    /// <summary>
    /// Renders the subscription component
    /// </summary>
    /// <param name="appId">Required. The application ID.</param>
    /// <param name="proxyBaseUrl">Base URL for subscription proxy endpoints (default: /api/wildwood-subscription)</param>
    /// <param name="customerId">Optional customer ID for linking subscriptions</param>
    /// <param name="customerEmail">Optional customer email</param>
    /// <param name="currency">Currency code (default: USD)</param>
    /// <param name="annualDiscount">Percentage discount for annual billing (default: 20)</param>
    /// <param name="requireBillingAddress">Whether billing address is required</param>
    /// <param name="showBillingToggle">Whether to show the monthly/annual toggle (default: true)</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string appId,
        string proxyBaseUrl = "/api/wildwood-subscription",
        string? customerId = null,
        string? customerEmail = null,
        string currency = "USD",
        int annualDiscount = 20,
        bool requireBillingAddress = false,
        bool showBillingToggle = true)
    {
        var plans = await _subscriptionService.GetAvailablePlansAsync();
        SubscriptionDto? currentSub = null;

        try
        {
            currentSub = await _subscriptionService.GetCurrentSubscriptionAsync();
        }
        catch (Exception ex)
        {
            // User may not be authenticated - plans still show for browsing
            _logger.LogDebug(ex, "Could not load current subscription (user may not be authenticated)");
        }

        var model = new SubscriptionViewModel
        {
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            CustomerId = customerId,
            CustomerEmail = customerEmail,
            Currency = currency,
            AnnualDiscount = annualDiscount,
            RequireBillingAddress = requireBillingAddress,
            ShowBillingToggle = showBillingToggle,
            Plans = plans,
            CurrentSubscription = currentSub
        };

        return View(model);
    }
}
