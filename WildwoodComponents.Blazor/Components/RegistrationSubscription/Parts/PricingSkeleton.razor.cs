using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts
{
    /// <summary>
    /// The placeholder shown while the live catalog loads: empty blocks and an accessible status
    /// message, with no number anywhere.
    /// </summary>
    /// <remarks>
    /// Ported from packages/wildwood-react/src/components/registrationSubscription/parts/PricingSkeleton.tsx.
    /// </remarks>
    public partial class PricingSkeleton : BaseWildwoodComponent
    {
        /// <summary>How many placeholder cards to draw.</summary>
        [Parameter] public int CardCount { get; set; } = PricingViewDecisions.SkeletonCards;

        /// <summary>What a screen reader hears while the prices load.</summary>
        [Parameter] public string Label { get; set; } = RegistrationSubscriptionLabels.Defaults.LoadingPlans;
    }
}
