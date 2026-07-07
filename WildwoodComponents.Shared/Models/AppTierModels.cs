using System.Text.Json.Serialization;

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
    public bool ShowSubscribeButton { get; set; } = true;
    public bool ShowContactButton { get; set; }
    public string? ContactButtonUrl { get; set; }
    public bool ShowPrice { get; set; } = true;
    public string? CustomBadgeText { get; set; }
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
    public int? TrialDays { get; set; }
    public bool HasTrial
    {
        get { return TrialDays.HasValue && TrialDays.Value > 0; }
    }

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
    [JsonPropertyName("code")]
    public string FeatureCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string IconClass { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsEnabled { get; set; }
}

#endregion

#region Feature Override Models

public class AppFeatureOverrideModel
{
    public string Id { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string FeatureCode { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string? Reason { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
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

/// <summary>
/// Result of a subscription cancellation. IsScheduled=true means access continues until
/// EffectiveDate (the end of the current billing period); false means access ended immediately.
/// RequiresUserAction is set for store-billed subscriptions (Apple App Store / Google Play):
/// the platform cannot stop the store's billing — show UserActionInstructions/UserActionUrl
/// so the user cancels in their store settings too.
/// </summary>
public class AppTierCancelResultModel
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsScheduled { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public bool RequiresUserAction { get; set; }
    public string? UserActionUrl { get; set; }
    public string? UserActionInstructions { get; set; }
}

public class TierChangePreviewModel
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsUpgrade { get; set; }
    public bool IsDowngrade { get; set; }
    public bool IsBillingFrequencyChange { get; set; }
    public bool PaymentRequired { get; set; }
    public bool PaymentBypassAllowed { get; set; }
    public bool PaymentProviderAvailable { get; set; }
    public string? CurrentTierName { get; set; }
    public decimal? CurrentPrice { get; set; }
    public string? CurrentBillingFrequency { get; set; }
    public string? NewTierName { get; set; }
    public decimal? NewPrice { get; set; }
    public string? NewBillingFrequency { get; set; }
    public decimal? MonthlyEquivalentCurrent { get; set; }
    public decimal? MonthlyEquivalentNew { get; set; }
    public decimal? ProratedChargeToday { get; set; }
    public decimal? CreditAmount { get; set; }
    public decimal? NextBillingAmount { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public List<string> FeaturesGained { get; set; } = new();
    public List<string> FeaturesLost { get; set; } = new();
    public string Currency { get; set; } = "USD";
    public int DaysRemainingInPeriod { get; set; }
    public bool AllowImmediateChange { get; set; } = true;
    public bool AllowScheduledChange { get; set; }
}

#endregion
