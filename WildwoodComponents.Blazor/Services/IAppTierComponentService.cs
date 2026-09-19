using System.Collections.Generic;
using System.Threading.Tasks;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Services
{
    public interface IAppTierComponentService
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

        /// <summary>
        /// Options form of the self-service tier change. ONLY this overload sends
        /// <see cref="SelfChangeTierOptions.SupportsPaymentAction"/>: the positional form posts
        /// exactly what it always did, because an older server rejects an unknown property. A
        /// change whose proration needs 3-D Secure comes back with RequiresAction, a client secret
        /// and a PendingChangeId for <see cref="CompleteTierChangeAsync"/> — Success=false there
        /// means "not yet", not a refusal.
        /// </summary>
        Task<AppTierChangeResultModel> ChangeTierAsync(string appId, SelfChangeTierOptions options);

        /// <summary>
        /// Finish a plan change that came back RequiresAction, once the prorated payment has been
        /// confirmed. Idempotent; the server answers Processing while the payment settles. Never
        /// throws — a refusal arrives as Success=false with ErrorCode/ErrorMessage.
        /// </summary>
        Task<AppTierChangeResultModel> CompleteTierChangeAsync(string appId, string pendingChangeId);

        /// <summary>
        /// Whether this account may still start a free trial — on a tier, and per pack. Never
        /// throws: a failure answers "eligible" with an empty (= unknown) pack map, because the
        /// quote and the payment initiation re-decide authoritatively before any money moves.
        /// </summary>
        Task<TrialEligibilityModel> GetTrialEligibilityAsync(string appId);

        Task<TierChangePreviewModel?> PreviewTierChangeAsync(string appId, string newTierId, string? newPricingId);
        Task<TierChangePreviewModel?> PreviewTierChangeAdminAsync(string appId, string userId, string newTierId, string? newPricingId);

        /// <summary>
        /// Self-service cancellation. Returns whether the cancellation is scheduled for the end
        /// of the billing period (IsScheduled + EffectiveDate) or took effect immediately.
        /// Failures are reported via Success/ErrorMessage instead of being silently swallowed.
        /// </summary>
        Task<AppTierCancelResultModel> CancelSubscriptionAsync(string appId);

        // Add-on subscription actions

        /// <summary>
        /// Deprecated: use <see cref="SubscribeToAddOnDetailedAsync"/>, which reports WHY a
        /// subscription was refused instead of a bare false.
        /// </summary>
        Task<bool> SubscribeToAddOnAsync(string appId, string addOnId, string? pricingId, string? paymentTransactionId);

        /// <summary>
        /// Deprecated: use <see cref="CancelAddOnDetailedAsync"/>. Sends <c>?immediate=false</c>.
        /// </summary>
        Task<bool> CancelAddOnSubscriptionAsync(string subscriptionId);

        /// <summary>
        /// Subscribe to a single pack, reporting the created subscription or a structured refusal
        /// (already owned, bundled in the tier, payment not accepted). Never throws.
        /// </summary>
        Task<AddOnSubscribeResultModel> SubscribeToAddOnDetailedAsync(string appId, string addOnId, string? pricingId = null, string? paymentTransactionId = null);

        /// <summary>
        /// Cancel one of the calling user's own packs. By default access continues to the end of
        /// the period already paid for; <paramref name="immediate"/> ends it now. Never throws.
        /// </summary>
        Task<AddOnSubscriptionCancelResultModel> CancelAddOnDetailedAsync(string subscriptionId, bool immediate = false);

        /// <summary>
        /// Take back a scheduled pack cancellation. Never throws.
        /// </summary>
        Task<AddOnSubscriptionReactivateResultModel> ReactivateAddOnAsync(string subscriptionId);

        // Pack checkout (card once, any number of packs) — none of these throw

        /// <summary>
        /// Price a basket of packs. Server-authoritative; the returned CheckoutId is echoed back on
        /// <see cref="CheckoutAddOnsAsync"/> and is what makes the purchase idempotent.
        /// </summary>
        Task<AddOnCheckoutQuoteModel> QuoteAddOnCheckoutAsync(string appId, IReadOnlyList<AddOnCheckoutItemInput>? items);

        /// <summary>
        /// Start the one-off card entry for an account with no card on file.
        /// </summary>
        Task<AddOnCheckoutPaymentMethodModel> CreateCheckoutPaymentMethodAsync(string appId, string providerId);

        /// <summary>
        /// Buy the basket. One pack failing does not stop the others, so read Results per pack
        /// rather than Success alone.
        /// </summary>
        Task<AddOnCheckoutResultModel> CheckoutAddOnsAsync(string appId, AddOnCheckoutRequestModel request);

        /// <summary>
        /// Finish one pack whose card the customer has just authenticated.
        /// </summary>
        Task<AddOnCheckoutItemResultModel> CompleteAddOnCheckoutAsync(string appId, string paymentTransactionId);

        // Public tier browsing (no auth required)

        /// <summary>
        /// The app's public tiers, swallowing failures into an empty list. Kept for existing
        /// callers; a view that must distinguish "sells no plans" from "pricing unavailable" uses
        /// <see cref="GetPublicTiersOrThrowAsync"/> or <see cref="GetPublicCatalogAsync"/>.
        /// </summary>
        Task<List<AppTierModel>> GetPublicTiersAsync(string appId);

        /// <summary>
        /// The throwing twin of <see cref="GetPublicTiersAsync"/>: failures PROPAGATE, so a pricing
        /// view can tell "this app sells no plans" from "pricing is unavailable right now".
        /// </summary>
        Task<List<AppTierModel>> GetPublicTiersOrThrowAsync(string appId);

        /// <summary>
        /// Everything the app sells — Active tiers and packs in display order under one resolved
        /// currency. Failures propagate.
        /// </summary>
        Task<PublicCatalog> GetPublicCatalogAsync(string appId, string? currencyOverride = null);

        /// <summary>
        /// The app's Active add-ons with their pricing options, via the public endpoint (no
        /// auth required) — the add-on twin of GetPublicTiersAsync, so a public pricing page
        /// can list the packs an app sells alongside its tiers. THROWS on failure, unlike
        /// GetPublicTiersAsync: a public page has to tell "this app sells no packs" apart from
        /// "the catalog failed to load", and an empty list cannot express the second.
        /// </summary>
        Task<List<AppTierAddOnModel>> GetPublicAddOnsAsync(string appId);

        // Usage tracking
        Task<List<AppTierLimitStatusModel>> GetAllLimitStatusesAsync(string appId);

        // Feature gating

        /// <summary>
        /// The user's feature entitlement map. THROWS on transport/HTTP failure: an empty map
        /// is a real "no access" answer, so failures must stay distinguishable from it —
        /// swallowing them would make feature gates lock entitled users out during transient
        /// errors.
        /// </summary>
        Task<Dictionary<string, bool>> GetUserFeaturesAsync(string appId);
        Task<AppFeatureCheckResultModel?> CheckFeatureAsync(string appId, string featureCode);
        Task<AppTierLimitStatusModel?> CheckLimitAsync(string appId, string limitCode);
        Task<AppTierLimitStatusModel?> IncrementUsageAsync(string appId, string limitCode);

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

        // Admin user-scoped queries (for viewing a specific user's data in User tracking mode)
        Task<UserTierSubscriptionModel?> GetUserSubscriptionAsync(string appId, string userId);
        Task<Dictionary<string, bool>> GetUserFeaturesAdminAsync(string appId, string userId);
        Task<List<AppTierLimitStatusModel>> GetUserLimitStatusesAsync(string appId, string userId);
        Task<List<UserAddOnSubscriptionModel>> GetUserAddOnsAsync(string appId, string userId);
        Task<AppTierCancelResultModel> CancelUserSubscriptionAsync(string appId, string userId);

        // Admin user-scoped write actions (for managing a specific user's subscription)
        Task<AppTierChangeResultModel> SubscribeUserToTierAsync(string appId, string userId, string tierId, string? pricingId);
        Task<AppTierChangeResultModel> ChangeUserTierAsync(string appId, string userId, string newTierId, string? newPricingId, bool immediate);
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

        void SetAuthToken(string token);
    }
}
