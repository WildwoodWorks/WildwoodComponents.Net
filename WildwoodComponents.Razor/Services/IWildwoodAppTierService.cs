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
    Task<List<AppTierAddOnModel>> GetAvailableAddOnsAsync(string appId);

    // User subscription
    Task<UserTierSubscriptionModel?> GetMySubscriptionAsync(string appId);
    Task<List<UserAddOnSubscriptionModel>> GetMyAddOnsAsync(string appId);

    // Tier subscription actions
    Task<AppTierChangeResultModel> SubscribeToTierAsync(string appId, string tierId, string? pricingId, string? paymentTransactionId);
    Task<AppTierChangeResultModel> ChangeTierAsync(string appId, string newTierId, string? newPricingId, bool immediate);
    Task<bool> CancelSubscriptionAsync(string appId);

    // Add-on subscription actions
    Task<bool> SubscribeToAddOnAsync(string appId, string addOnId, string? pricingId, string? paymentTransactionId);
    Task<bool> CancelAddOnSubscriptionAsync(string subscriptionId);

    // Feature gating
    Task<Dictionary<string, bool>> GetUserFeaturesAsync(string appId);
    Task<AppFeatureCheckResultModel?> CheckFeatureAsync(string appId, string featureCode);
    Task<AppTierLimitStatusModel?> CheckLimitAsync(string appId, string limitCode);
    Task<AppTierLimitStatusModel?> IncrementUsageAsync(string appId, string limitCode);
}
