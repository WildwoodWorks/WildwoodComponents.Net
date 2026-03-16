// Shared models are in WildwoodComponents.Shared.Models
// Re-exported here for backward compatibility
global using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Models;

/// <summary>
/// View model for the AppTierViewComponent
/// </summary>
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
