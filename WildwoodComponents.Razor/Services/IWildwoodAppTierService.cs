using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// App tier service for communicating with WildwoodAPI app-tier endpoints.
/// Razor Pages equivalent of WildwoodComponents.Blazor.Services.IAppTierComponentService.
/// </summary>
public interface IWildwoodAppTierService
{
    // Tier browsing
    Task<List<AppTierModel>> GetAvailableTiersAsync(string appId);
    Task<AppTierModel?> GetTierAsync(string tierId);
    Task<List<AppTierAddOnModel>> GetAvailableAddOnsAsync(string appId);

    // User subscription

    /// <summary>
    /// The user's active subscription, or null when none exists (204/no-content). THROWS on
    /// transport/HTTP failure so callers can distinguish "no subscription" from a failed
    /// lookup — swallowing both as null made subscribed users look unsubscribed during
    /// transient errors.
    /// </summary>
    Task<UserTierSubscriptionModel?> GetMySubscriptionAsync(string appId);
    Task<List<UserAddOnSubscriptionModel>> GetMyAddOnsAsync(string appId);

    // Tier subscription actions
    Task<AppTierChangeResultModel> SubscribeToTierAsync(string appId, string tierId, string? pricingId, string? paymentTransactionId);
    Task<AppTierChangeResultModel> ChangeTierAsync(string appId, string newTierId, string? newPricingId, bool immediate, string? paymentTransactionId = null);
    Task<TierChangePreviewModel?> PreviewTierChangeAsync(string appId, string newTierId, string? newPricingId);

    /// <summary>
    /// Self-service cancellation. Returns whether the cancellation is scheduled for the end
    /// of the billing period (IsScheduled + EffectiveDate) or took effect immediately.
    /// Failures are reported via Success/ErrorMessage instead of being silently swallowed.
    /// </summary>
    Task<AppTierCancelResultModel> CancelSubscriptionAsync(string appId);

    // Add-on subscription actions
    Task<bool> SubscribeToAddOnAsync(string appId, string addOnId, string? pricingId, string? paymentTransactionId);
    Task<bool> CancelAddOnSubscriptionAsync(string subscriptionId);

    // Usage / limit statuses (bulk)
    Task<List<AppTierLimitStatusModel>> GetAllLimitStatusesAsync(string appId);

    // Feature gating

    /// <summary>
    /// The user's feature entitlement map. THROWS on transport/HTTP failure: an empty map is
    /// a real "no access" answer, so failures must stay distinguishable from it — swallowing
    /// them would make feature gates lock entitled users out during transient errors.
    /// </summary>
    Task<Dictionary<string, bool>> GetUserFeaturesAsync(string appId);

    /// <summary>
    /// Feature-gating check for server-rendered views (use in @if blocks). Fetches the user's
    /// entitlement map ONCE per request (the service is scoped, so the map is cached on the
    /// instance) and looks up the code case-insensitively. FAILS OPEN when entitlements can't
    /// be loaded: gating markup is UX only — the WildwoodAPI enforces the real entitlement —
    /// and wrongly hiding paid features is worse than briefly showing them.
    /// </summary>
    Task<bool> HasFeatureAsync(string appId, string featureCode);

    Task<AppFeatureCheckResultModel?> CheckFeatureAsync(string appId, string featureCode);
    Task<AppTierLimitStatusModel?> CheckLimitAsync(string appId, string limitCode);
    Task<AppTierLimitStatusModel?> IncrementUsageAsync(string appId, string limitCode);

    // Public tier browsing (no auth required)
    Task<List<AppTierModel>> GetPublicTiersAsync(string appId);

    // Company-scoped subscription (admin)
    Task<UserTierSubscriptionModel?> GetCompanySubscriptionAsync(string appId, string companyId);
    Task<List<UserAddOnSubscriptionModel>> GetCompanyAddOnSubscriptionsAsync(string appId, string companyId);
    Task<List<AppTierLimitStatusModel>> GetCompanyLimitStatusesAsync(string appId, string companyId);

    // Company-scoped features
    Task<List<AppFeatureDefinitionModel>> GetFeatureDefinitionsAsync(string appId);
    Task<Dictionary<string, bool>> GetCompanyFeaturesAsync(string appId, string companyId);

    // Company-scoped admin actions
    Task<AppTierChangeResultModel> SubscribeCompanyToTierAsync(string appId, string companyId, string tierId, string? pricingId);
    Task<AppTierChangeResultModel> ChangeCompanyTierAsync(string appId, string companyId, string newTierId, string? pricingId, bool immediate);
    Task<AppTierCancelResultModel> CancelCompanySubscriptionAsync(string appId, string companyId);
    Task<bool> SubscribeCompanyToAddOnAsync(string appId, string companyId, string addOnId);
    Task<bool> CancelCompanyAddOnAsync(string subscriptionId, bool immediate);

    // Admin add-on browsing (all add-ons, not just available)
    Task<List<AppTierAddOnModel>> GetAllAddOnsAsync(string appId);

    // Admin user-scoped queries
    Task<UserTierSubscriptionModel?> GetUserSubscriptionAsync(string appId, string userId);
    Task<Dictionary<string, bool>> GetUserFeaturesAdminAsync(string appId, string userId);
    Task<List<AppTierLimitStatusModel>> GetUserLimitStatusesAsync(string appId, string userId);
    Task<List<UserAddOnSubscriptionModel>> GetUserAddOnsAsync(string appId, string userId);
    Task<AppTierCancelResultModel> CancelUserSubscriptionAsync(string appId, string userId);

    // Admin user-scoped write actions
    Task<AppTierChangeResultModel> SubscribeUserToTierAsync(string appId, string userId, string tierId, string? pricingId);
    Task<AppTierChangeResultModel> ChangeUserTierAsync(string appId, string userId, string newTierId, string? newPricingId, bool immediate);
    Task<TierChangePreviewModel?> PreviewTierChangeAdminAsync(string appId, string userId, string newTierId, string? newPricingId);
    Task<bool> SubscribeUserToAddOnAsync(string appId, string userId, string addOnId);
    Task<bool> CancelUserAddOnAsync(string appId, string subscriptionId);
    Task<bool> ResetUserUsageAsync(string appId, string userId, string limitCode);
    Task<bool> UpdateUserUsageLimitAsync(string appId, string userId, string limitCode, int newMaxValue);

    // Settings
    Task<string> GetTrackingModeAsync(string appId);

    // Admin usage limit overrides
    Task<bool> UpdateUsageLimitAsync(string appId, string limitCode, int newMaxValue);
    Task<bool> ResetUsageAsync(string appId, string limitCode);
    Task<bool> UpdateCompanyUsageLimitAsync(string appId, string companyId, string limitCode, int newMaxValue);
    Task<bool> ResetCompanyUsageAsync(string appId, string companyId, string limitCode);

    // Feature overrides (admin)
    Task<bool> SetFeatureOverrideAsync(string appId, string? userId, string featureCode, bool isEnabled, string? reason = null, DateTime? expiresAt = null);
    Task<bool> RemoveFeatureOverrideAsync(string appId, string? userId, string featureCode);
    Task<List<AppFeatureOverrideModel>> GetFeatureOverridesAsync(string appId, string? userId = null);
}
