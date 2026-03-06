using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Models;

/// <summary>
/// View model for the PaymentViewComponent.
/// Uses shared models from WildwoodComponents.Shared for DTOs.
/// </summary>
public class PaymentViewModel
{
    // Required
    public string AppId { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    // Optional
    public string Currency { get; set; } = "USD";
    public string? Description { get; set; }
    public string? CustomerId { get; set; }
    public string? CustomerEmail { get; set; }
    public string? OrderId { get; set; }
    public string? SubscriptionId { get; set; }
    public string? PricingModelId { get; set; }
    public bool IsSubscription { get; set; }
    public bool ShowAmount { get; set; } = true;
    public bool RequireBillingAddress { get; set; }
    public string? ReturnUrl { get; set; }
    public string? CancelUrl { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }

    // Pre-loaded providers (skip API discovery)
    public List<PaymentProviderDto>? PreloadedProviders { get; set; }
    public string? PreselectedProviderId { get; set; }

    // Proxy base URL for JS AJAX calls
    public string ProxyBaseUrl { get; set; } = string.Empty;

    // Component instance ID for DOM scoping
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}
