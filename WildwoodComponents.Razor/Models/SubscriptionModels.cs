namespace WildwoodComponents.Razor.Models;

public class SubscriptionViewModel
{
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-subscription";
    public string? CustomerId { get; set; }
    public string? CustomerEmail { get; set; }
    public string Currency { get; set; } = "USD";
    public int AnnualDiscount { get; set; } = 20;
    public bool RequireBillingAddress { get; set; }
    public bool ShowBillingToggle { get; set; } = true;
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    public SubscriptionDto? CurrentSubscription { get; set; }
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}

public class SubscriptionManagerViewModel
{
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-subscription";
    public SubscriptionDto? CurrentSubscription { get; set; }
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    public List<InvoiceDto> Invoices { get; set; } = new();
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}

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

public class SubscriptionPlanDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal? MonthlyEquivalent { get; set; }
    public string BillingFrequency { get; set; } = "Monthly";
    public string? BillingFrequencyLabel { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public bool IsFree { get; set; }
    public bool IsRecommended { get; set; }
    public int TrialDays { get; set; }
    public List<string> Features { get; set; } = new();
    public Dictionary<string, string>? Limitations { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public class SubscriptionDto
{
    public string Id { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? PlanId { get; set; }
    public string? PlanName { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public decimal Price { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? BillingFrequency { get; set; }
}

public class InvoiceDto
{
    public string Id { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = string.Empty;
    public DateTime? InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? PaidDate { get; set; }
    public string? InvoiceUrl { get; set; }
}
