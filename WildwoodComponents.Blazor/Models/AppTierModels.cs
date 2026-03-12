// Shared models are in WildwoodComponents.Shared.Models
// Re-exported here for backward compatibility
global using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Models
{
    #region Event Args (Blazor-only)

    public class AppTierSelectedEventArgs
    {
        public string TierId { get; set; } = string.Empty;
        public string TierName { get; set; } = string.Empty;
        public string PricingId { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string BillingFrequency { get; set; } = string.Empty;
        public bool IsFreeTier { get; set; }
    }

    public class AppTierSubscriptionChangedEventArgs
    {
        public string SubscriptionId { get; set; } = string.Empty;
        public string TierId { get; set; } = string.Empty;
        public string TierName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty; // "subscribed", "changed", "cancelled"
    }

    public class PricingTierSelectedEventArgs : EventArgs
    {
        public AppTierModel Tier { get; set; } = default!;
        public AppTierPricingModel? SelectedPricing { get; set; }
    }

    #endregion
}
