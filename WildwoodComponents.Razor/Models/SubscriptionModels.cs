namespace WildwoodComponents.Razor.Models;

public class PaymentFormViewModel
{
    public string AppId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? Description { get; set; }
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-payment";
    public bool RequireBillingAddress { get; set; }
    public string? MerchantId { get; set; }
    public string? OrderId { get; set; }
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}

public class PricingDisplayViewModel
{
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-app-tiers";
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public bool ShowBillingToggle { get; set; } = true;
    public bool ShowFeatureComparison { get; set; } = true;
    public bool ShowLimits { get; set; } = true;
    public string Currency { get; set; } = "USD";
    public string? EnterpriseContactUrl { get; set; }
    public string? SelectTierUrl { get; set; }
    public string? PreSelectedTierId { get; set; }
    public List<AppTierModel> Tiers { get; set; } = new();
    public bool HasMonthlyAndAnnual { get; set; }
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}

public class SignupWithSubscriptionViewModel
{
    public string AppId { get; set; } = string.Empty;
    public string AuthProxyBaseUrl { get; set; } = "/api/wildwood-auth";
    public string SubscriptionProxyBaseUrl { get; set; } = "/api/wildwood-subscription";
    public string PaymentProxyBaseUrl { get; set; } = "/api/wildwood-payment";
    public string? ReturnUrl { get; set; }
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public bool AllowRegistration { get; set; } = true;
    public string Currency { get; set; } = "USD";
    public List<AppTierModel> Tiers { get; set; } = new();
    public List<string> ExternalProviders { get; set; } = new();
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}

