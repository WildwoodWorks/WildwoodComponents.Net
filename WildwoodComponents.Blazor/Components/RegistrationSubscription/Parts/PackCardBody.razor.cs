using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts
{
    /// <summary>
    /// The inside of a pack card: name, blurb, live price, trial line and the features the pack
    /// grants.
    /// </summary>
    /// <remarks>
    /// Ported from the <c>PackCardBody</c> function inside
    /// packages/wildwood-react/src/components/registrationSubscription/parts/PackGrid.tsx, and kept
    /// a component of its own for the same reason React kept it a function: the multi-select card
    /// is one big <c>&lt;button&gt;</c>, so everything inside it has to be phrasing content, and
    /// the single-select card renders the identical body above its own call to action.
    /// </remarks>
    public partial class PackCardBody : BaseWildwoodComponent
    {
        /// <summary>The pack, as the live catalog returned it.</summary>
        [Parameter, EditorRequired] public AppTierAddOnModel AddOn { get; set; } = new AppTierAddOnModel();

        /// <summary>The currency the grid is quoted in; the pack's own currency still wins.</summary>
        [Parameter] public string? Currency { get; set; }

        /// <summary>Copy the host may replace.</summary>
        [Parameter] public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

        /// <summary>
        /// Host marketing copy for this pack, replacing the catalog's own description. The Blazor
        /// analog of React's <c>describeAddOn</c>, which returns a node.
        /// </summary>
        [Parameter] public RenderFragment<AppTierAddOnModel>? Describe { get; set; }

        /// <summary>The live price with its per-period suffix, or the "not yet available" wording.</summary>
        private string PriceText
        {
            get { return PricingViewDecisions.PackPriceText(AddOn, Currency, Labels); }
        }

        /// <summary>Whether <see cref="PriceText"/> is a price at all.</summary>
        private bool HasPrice
        {
            get { return PricingViewDecisions.HasPackPrice(AddOn); }
        }

        /// <summary>"14-day free trial", or empty when the pack's pricing starts no trial.</summary>
        private string TrialLabel
        {
            get { return PricingViewDecisions.PackTrialLabel(AddOn); }
        }
    }
}
