using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Models;

/// <summary>
/// View model for the SubscriptionAdminViewComponent (tabbed container)
/// </summary>
public class SubscriptionAdminViewModel
{
    public string ComponentId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-app-tiers";
    public string? CompanyId { get; set; }
    public string? UserId { get; set; }
    public SubscriptionDisplayMode DisplayMode { get; set; } = SubscriptionDisplayMode.All;
    public bool IsAdmin { get; set; }
    public string Currency { get; set; } = "USD";
    public bool ShowBillingToggle { get; set; } = true;
    public bool ShowStatusAboveTabs { get; set; }
    public bool IsCompanyMode { get; set; }
    public UserTierSubscriptionModel? Subscription { get; set; }
    public int OverrideCount { get; set; }
}

/// <summary>
/// View model for the SubscriptionStatusPanel
/// </summary>
public class SubscriptionStatusPanelViewModel
{
    public string ComponentId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public UserTierSubscriptionModel? Subscription { get; set; }

    public string StatusBadgeClass
    {
        get
        {
            if (Subscription == null) return "bg-secondary";
            return Subscription.Status?.ToLower() switch
            {
                "active" => "bg-success",
                "trialing" => "bg-info",
                "pastdue" => "bg-warning",
                "cancelled" => "bg-danger",
                "expired" => "bg-secondary",
                _ => "bg-secondary"
            };
        }
    }
}

/// <summary>
/// View model for the TierPlansPanel
/// </summary>
public class TierPlansPanelViewModel
{
    public string ComponentId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public bool ShowBillingToggle { get; set; } = true;
    public bool IsAdmin { get; set; }
    public List<AppTierModel> Tiers { get; set; } = new();
    public UserTierSubscriptionModel? Subscription { get; set; }
    public bool HasMonthlyAndAnnual { get; set; }
}

/// <summary>
/// View model for the FeaturesPanel
/// </summary>
public class FeaturesPanelViewModel
{
    public string ComponentId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public List<FeatureGroupViewModel> FeatureGroups { get; set; } = new();
    public List<AppFeatureOverrideModel> Overrides { get; set; } = new();
}

public class FeatureGroupViewModel
{
    public string Category { get; set; } = string.Empty;
    public List<FeatureItemViewModel> Features { get; set; } = new();
}

public class FeatureItemViewModel
{
    public string FeatureCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string IconClass { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool HasOverride { get; set; }
    public AppFeatureOverrideModel? Override { get; set; }
}

/// <summary>
/// View model for the OverridesPanel
/// </summary>
public class OverridesPanelViewModel
{
    public string ComponentId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = string.Empty;
    public List<AppFeatureOverrideModel> Overrides { get; set; } = new();
}

/// <summary>
/// View model for the AddOnsPanel
/// </summary>
public class AddOnsPanelViewModel
{
    public string ComponentId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public string Currency { get; set; } = "USD";
    public string? CurrentTierId { get; set; }
    public List<UserAddOnSubscriptionModel> ActiveAddOns { get; set; } = new();
    public List<AppTierAddOnModel> AvailableAddOns { get; set; } = new();
}

/// <summary>
/// View model for the UsageLimitsPanel
/// </summary>
public class UsageLimitsPanelViewModel
{
    public string ComponentId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public List<AppTierLimitStatusModel> LimitStatuses { get; set; } = new();
}
