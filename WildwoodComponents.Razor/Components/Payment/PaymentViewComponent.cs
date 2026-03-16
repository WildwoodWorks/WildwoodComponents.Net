using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Components.Payment;

/// <summary>
/// ViewComponent that renders a complete payment form with provider selection.
/// Supports Stripe, PayPal, Apple Pay, Google Pay, BNPL, and more.
/// Client-side JavaScript handles provider initialization, payment processing, and callbacks.
/// Razor Pages equivalent of WildwoodComponents.Blazor PaymentComponent.
/// </summary>
public class PaymentViewComponent : ViewComponent
{
    private readonly IWildwoodPaymentService _paymentService;
    private readonly ILogger<PaymentViewComponent> _logger;

    public PaymentViewComponent(IWildwoodPaymentService paymentService, ILogger<PaymentViewComponent> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    /// <summary>
    /// Renders the payment component
    /// </summary>
    /// <param name="appId">Required. The application ID.</param>
    /// <param name="amount">Required. The payment amount.</param>
    /// <param name="proxyBaseUrl">Base URL for payment proxy endpoints (default: /api/wildwood-payment)</param>
    /// <param name="currency">Currency code (default: USD)</param>
    /// <param name="description">Payment description shown to user</param>
    /// <param name="customerId">Optional customer ID for saved payment methods</param>
    /// <param name="customerEmail">Optional customer email for receipts</param>
    /// <param name="orderId">Optional order ID to associate with payment</param>
    /// <param name="subscriptionId">Optional subscription ID for recurring payments</param>
    /// <param name="pricingModelId">Optional pricing model ID</param>
    /// <param name="isSubscription">Whether this is a subscription payment</param>
    /// <param name="showAmount">Whether to display the amount (default: true)</param>
    /// <param name="requireBillingAddress">Whether billing address is required</param>
    /// <param name="returnUrl">Return URL for redirect-based providers (BNPL)</param>
    /// <param name="cancelUrl">Cancel URL for redirect-based providers</param>
    /// <param name="metadata">Optional metadata dictionary to attach to the payment</param>
    /// <param name="preloadedProviders">Pre-loaded providers to skip API discovery call</param>
    /// <param name="preselectedProviderId">Provider ID to pre-select as default</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string appId,
        decimal amount,
        string proxyBaseUrl = "/api/wildwood-payment",
        string currency = "USD",
        string? description = null,
        string? customerId = null,
        string? customerEmail = null,
        string? orderId = null,
        string? subscriptionId = null,
        string? pricingModelId = null,
        bool isSubscription = false,
        bool showAmount = true,
        bool requireBillingAddress = false,
        string? returnUrl = null,
        string? cancelUrl = null,
        Dictionary<string, string>? metadata = null,
        List<PaymentProviderDto>? preloadedProviders = null,
        string? preselectedProviderId = null)
    {
        List<PaymentProviderDto> providers;
        PaymentProviderDto? defaultProvider = null;

        if (preloadedProviders != null && preloadedProviders.Count > 0)
        {
            providers = preloadedProviders;
            if (!string.IsNullOrEmpty(preselectedProviderId))
            {
                foreach (var p in providers)
                {
                    if (p.Id == preselectedProviderId)
                    {
                        defaultProvider = p;
                        break;
                    }
                }
            }
        }
        else
        {
            try
            {
                var platformInfo = await _paymentService.GetAvailableProvidersAsync(appId);
                providers = platformInfo?.AvailableProviders ?? new List<PaymentProviderDto>();
                defaultProvider = platformInfo?.DefaultProvider;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load payment providers for app {AppId}", appId);
                providers = new List<PaymentProviderDto>();
            }
        }

        if (defaultProvider == null && providers.Count > 0)
        {
            defaultProvider = providers[0];
        }

        var model = new PaymentViewModel
        {
            AppId = appId,
            Amount = amount,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            Currency = currency,
            Description = description,
            CustomerId = customerId,
            CustomerEmail = customerEmail,
            OrderId = orderId,
            SubscriptionId = subscriptionId,
            PricingModelId = pricingModelId,
            IsSubscription = isSubscription,
            ShowAmount = showAmount,
            RequireBillingAddress = requireBillingAddress,
            ReturnUrl = returnUrl,
            CancelUrl = cancelUrl,
            Metadata = metadata,
            PreloadedProviders = providers,
            PreselectedProviderId = defaultProvider?.Id
        };

        return View(model);
    }
}
