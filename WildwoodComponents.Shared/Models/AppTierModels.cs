namespace WildwoodComponents.Shared.Models;

#region App Tier Models

public class AppTierModel
{
    public string Id { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsFreeTier { get; set; }
    public bool AllowUpgrades { get; set; }
    public bool AllowDowngrades { get; set; }
    public string Status { get; set; } = string.Empty;
    public string BadgeColor { get; set; } = string.Empty;
    public string IconClass { get; set; } = string.Empty;
    public List<AppTierPricingModel> PricingOptions { get; set; } = new();
    public List<AppTierFeatureModel> Features { get; set; } = new();
    public List<AppTierLimitModel> Limits { get; set; } = new();
}

public class AppTierPricingModel
{
    public string Id { get; set; } = string.Empty;
    public string AppTierId { get; set; } = string.Empty;
    public string PricingModelId { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public int DisplayOrder { get; set; }
    public string PricingModelName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string BillingFrequency { get; set; } = string.Empty;

    public string BillingFrequencyLabel
    {
        get
        {
            if (string.Equals(BillingFrequency, "OneTime", StringComparison.OrdinalIgnoreCase)) return "One-Time";
            if (string.Equals(BillingFrequency, "Monthly", StringComparison.OrdinalIgnoreCase)) return "Monthly";
            if (string.Equals(BillingFrequency, "Quarterly", StringComparison.OrdinalIgnoreCase)) return "Quarterly";
            if (string.Equals(BillingFrequency, "Annually", StringComparison.OrdinalIgnoreCase)) return "Annual";
            return BillingFrequency;
        }
    }
}

public class AppTierFeatureModel
{
    public string Id { get; set; } = string.Empty;
    public string FeatureCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string Category { get; set; } = string.Empty;
}

public class AppTierLimitModel
{
    public string Id { get; set; } = string.Empty;
    public string LimitCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long MaxValue { get; set; }
    public string LimitType { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public bool IsUnlimited => MaxValue == -1;
    public string MaxValueDisplay => MaxValue == -1 ? "Unlimited" : MaxValue.ToString("N0");
}

#endregion

#region App Tier Add-On Models

public class AppTierAddOnModel
{
    public string Id { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public string IconClass { get; set; } = string.Empty;
    public string BadgeColor { get; set; } = string.Empty;
    public int? TrialDays { get; set; }
    public List<AppTierAddOnFeatureModel> Features { get; set; } = new();
    public List<AppTierAddOnPricingModel> PricingOptions { get; set; } = new();
    public List<string> BundledInTierIds { get; set; } = new();
}

public class AppTierAddOnFeatureModel
{
    public string Id { get; set; } = string.Empty;
    public string FeatureCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class AppTierAddOnPricingModel
{
    public string Id { get; set; } = string.Empty;
    public string PricingModelId { get; set; } = string.Empty;
    public string PricingModelName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string BillingFrequency { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

#endregion

#region User Subscription Models

public class UserTierSubscriptionModel
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string AppTierId { get; set; } = string.Empty;
    public string? AppTierPricingId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PaymentTransactionId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public DateTime? TrialEndDate { get; set; }
    public DateTime? GracePeriodEndDate { get; set; }
    public string? PendingTierId { get; set; }
    public string TierName { get; set; } = string.Empty;
    public string TierDescription { get; set; } = string.Empty;
    public bool IsFreeTier { get; set; }
    public string PendingTierName { get; set; } = string.Empty;
    public DateTime? PendingChangeDate { get; set; }

    // Company context (for admin/company-scoped subscriptions)
    public string? CompanyId { get; set; }
    public string? CompanyName { get; set; }

    public bool IsActive
    {
        get
        {
            return string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Status, "Trialing", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Status, "PastDue", StringComparison.OrdinalIgnoreCase);
        }
    }
}

public class UserAddOnSubscriptionModel
{
    public string Id { get; set; } = string.Empty;
    public string? CompanyId { get; set; }
    public string AppTierAddOnId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string AddOnName { get; set; } = string.Empty;
    public string AddOnDescription { get; set; } = string.Empty;
    public bool IsBundled { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public DateTime? TrialEndDate { get; set; }
    public DateTime? GracePeriodEndDate { get; set; }
}

#endregion

#region Feature Check Models

public class AppFeatureCheckResultModel
{
    public string FeatureCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool HasAccess { get; set; }
    public string CurrentTierName { get; set; } = string.Empty;
    public string RequiredTierName { get; set; } = string.Empty;
    public string UpgradeMessage { get; set; } = string.Empty;
    public bool AvailableAsAddOn { get; set; }
    public string AddOnId { get; set; } = string.Empty;
    public string AddOnName { get; set; } = string.Empty;
    public decimal? AddOnPrice { get; set; }
}

public class AppTierLimitStatusModel
{
    public string LimitCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long CurrentUsage { get; set; }
    public long MaxValue { get; set; }
    public bool IsUnlimited { get; set; }
    public double UsagePercent { get; set; }
    public bool IsAtWarningThreshold { get; set; }
    public bool IsExceeded { get; set; }
    public bool IsHardBlocked { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
}

#endregion

#region Feature Definition Models

public class AppFeatureDefinitionModel
{
    public string FeatureCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string IconClass { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsEnabled { get; set; }
}

#endregion

#region Display Mode

public enum SubscriptionDisplayMode
{
    All,
    Subscription,
    Plans,
    Features,
    AddOns,
    Usage
}

#endregion

#region Tier Change Models

public class AppTierChangeResultModel
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public UserTierSubscriptionModel? Subscription { get; set; }
    public bool IsScheduled { get; set; }
    public DateTime? EffectiveDate { get; set; }
}

#endregion
