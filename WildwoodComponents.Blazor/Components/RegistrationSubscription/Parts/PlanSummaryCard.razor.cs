using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts
{
    /// <summary>
    /// The plan the signup is carrying, above the registration form: its name, its live price, its
    /// trial, and a way back to the grid.
    /// </summary>
    /// <remarks>
    /// Ported from
    /// packages/wildwood-react/src/components/registrationSubscription/parts/PlanSummaryCard.tsx.
    /// The "Change plan" affordance renders only when the host wired
    /// <see cref="OnChangePlan"/> — in a skip flow the plan is not the visitor's to change, so the
    /// card is read-only.
    /// </remarks>
    public partial class PlanSummaryCard : BaseWildwoodComponent
    {
        /// <summary>The chosen plan, from the live catalog.</summary>
        [Parameter] public AppTierModel? Tier { get; set; }

        /// <summary>The option being bought, or null for a plan with no price at all.</summary>
        [Parameter] public AppTierPricingModel? Pricing { get; set; }

        /// <summary>The currency the catalog is quoted in; the plan's own still wins.</summary>
        [Parameter] public string? Currency { get; set; }

        /// <summary>Copy the host may replace.</summary>
        [Parameter] public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

        /// <summary>Offers "Change plan". Unwired, the card is read-only.</summary>
        [Parameter] public EventCallback OnChangePlan { get; set; }

        /// <summary>A free plan advertises no trial: there is nothing to try before paying.</summary>
        private string Trial
        {
            get
            {
                if (Tier is null || Tier.IsFreeTier) return string.Empty;
                return CatalogHelpers.TrialLabel(Pricing?.TrialDays);
            }
        }

        private string Frequency
        {
            get
            {
                var frequency = Pricing?.BillingFrequency;
                return frequency is { Length: > 0 } ? frequency.ToLowerInvariant() : string.Empty;
            }
        }

        private Task HandleChangePlan()
        {
            return OnChangePlan.HasDelegate ? OnChangePlan.InvokeAsync() : Task.CompletedTask;
        }
    }
}
