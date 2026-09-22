using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts
{
    /// <summary>
    /// The plan grid: a monthly/annual toggle over the platform's tier cards, priced off the live
    /// public catalog.
    /// </summary>
    /// <remarks>
    /// Ported from packages/wildwood-react/src/components/registrationSubscription/parts/PlanGrid.tsx.
    /// The billing cycle is a parameter rather than internal state, because the pricing view owns
    /// it: a pack selection is stamped with the same cycle the plans are being quoted in.
    /// </remarks>
    public partial class PlanGrid : BaseWildwoodComponent
    {
        /// <summary>Active plans in catalog order. Already filtered — this grid renders what it is given.</summary>
        [Parameter] public IReadOnlyList<AppTierModel> Tiers { get; set; } = new List<AppTierModel>();

        /// <summary>The currency the grid quotes in; each plan's own currency still wins.</summary>
        [Parameter] public string? Currency { get; set; }

        /// <summary>The cycle the grid is quoting.</summary>
        [Parameter] public PricingBilling Billing { get; set; } = PricingBilling.Monthly;

        /// <summary>Raised when the visitor flips the toggle.</summary>
        [Parameter] public EventCallback<PricingBilling> OnBillingChange { get; set; }

        /// <summary>
        /// Show the monthly/annual toggle. It still only appears when some plan is actually priced
        /// by the year — a toggle with nothing to switch to is a control that lies.
        /// </summary>
        [Parameter] public bool ShowBillingToggle { get; set; } = true;

        /// <summary>Show each plan's feature list.</summary>
        [Parameter] public bool ShowFeatures { get; set; } = true;

        /// <summary>Show each plan's usage limits.</summary>
        [Parameter] public bool ShowLimits { get; set; } = true;

        /// <summary>Marks one plan as the visitor's current choice. Matched case-insensitively.</summary>
        [Parameter] public string? HighlightTierId { get; set; }

        /// <summary>Where an enterprise plan's "Contact Sales" link points.</summary>
        [Parameter] public string? ContactUrl { get; set; }

        /// <summary>Copy the host may replace.</summary>
        [Parameter] public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

        /// <summary>Raised when the visitor picks a plan.</summary>
        [Parameter] public EventCallback<AppTierModel> OnSelectTier { get; set; }

        private bool IsAnnual
        {
            get { return Billing == PricingBilling.Annual; }
        }

        private bool ShowToggle
        {
            get { return ShowBillingToggle && PricingViewDecisions.HasAnnualPricing(Tiers); }
        }

        private int MaxAnnualDiscount
        {
            get { return PricingViewDecisions.BestAnnualDiscount(Tiers); }
        }

        private string AnnualSavingsLabel
        {
            get { return RegistrationSubscriptionLabels.Format(Labels.AnnualSavings, "percent", MaxAnnualDiscount); }
        }

        /// <summary>
        /// The footer's wording, matching React's shared tier-card footer: a plan the visitor has
        /// already chosen says so, a free plan starts, anything else subscribes. These three
        /// strings are not labels — they belong to every plan grid on the platform.
        /// </summary>
        private static string CallToAction(AppTierModel tier, bool highlighted)
        {
            if (highlighted) return "Continue with This Plan";
            return tier.IsFreeTier ? "Get Started" : "Subscribe";
        }

        /// <summary>
        /// A link that leaves the app opens in a new tab, as every other tier card in the library
        /// does. Blazor omits an attribute whose value is null, so a relative URL gets neither.
        /// </summary>
        /// <remarks>
        /// The test is case-INSENSITIVE where React's <c>startsWith('http')</c> is not. A URL scheme
        /// is case-insensitive, so this is a strict superset of React's rule: every URL React marks
        /// external is marked external here, and a <c>HTTPS://</c> one additionally gets the
        /// <c>rel="noopener noreferrer"</c> it should have had. Nothing a visitor reads changes.
        /// </remarks>
        private static bool LeavesTheApp(string url)
        {
            return url.StartsWith("http", System.StringComparison.OrdinalIgnoreCase);
        }

        private static string? ExternalTarget(string url)
        {
            return LeavesTheApp(url) ? "_blank" : null;
        }

        private static string? ExternalRel(string url)
        {
            return LeavesTheApp(url) ? "noopener noreferrer" : null;
        }

        private Task ToggleBilling()
        {
            var next = IsAnnual ? PricingBilling.Monthly : PricingBilling.Annual;
            return OnBillingChange.HasDelegate ? OnBillingChange.InvokeAsync(next) : Task.CompletedTask;
        }

        private Task HandleSelect(AppTierModel tier)
        {
            return OnSelectTier.HasDelegate ? OnSelectTier.InvokeAsync(tier) : Task.CompletedTask;
        }
    }
}
