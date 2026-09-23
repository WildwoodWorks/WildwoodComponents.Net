using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts
{
    /// <summary>
    /// The packs an app sells, flat or filed under the host's headings, with either a call to
    /// action per pack or a multi-select and one Continue.
    /// </summary>
    /// <remarks>
    /// Ported from packages/wildwood-react/src/components/registrationSubscription/parts/PackGrid.tsx.
    /// The grouping, the price wording and the cap on a selection are all
    /// <see cref="PricingViewDecisions"/>, so they can be tested without a renderer.
    /// </remarks>
    public partial class PackGrid : BaseWildwoodComponent
    {
        /// <summary>Active packs in catalog order.</summary>
        [Parameter] public IReadOnlyList<AppTierAddOnModel> AddOns { get; set; } = new List<AppTierAddOnModel>();

        /// <summary>The currency the grid quotes in; each pack's own currency still wins.</summary>
        [Parameter] public string? Currency { get; set; }

        /// <summary>Headings to file packs under. Omitted, the packs render as one flat grid.</summary>
        [Parameter] public IReadOnlyList<AddOnGroup>? AddOnGroups { get; set; }

        /// <summary>Host copy for a pack, replacing the catalog's own description.</summary>
        [Parameter] public RenderFragment<AppTierAddOnModel>? Describe { get; set; }

        /// <summary>Whether the visitor picks one pack at a time or ticks several.</summary>
        [Parameter] public PricingPackSelection Selection { get; set; } = PricingPackSelection.None;

        /// <summary>The currently selected pack ids (multi-select only).</summary>
        [Parameter] public IReadOnlyList<string> SelectedIds { get; set; } = new List<string>();

        /// <summary>Copy the host may replace.</summary>
        [Parameter] public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

        /// <summary>Toggle one pack (multi-select only).</summary>
        [Parameter] public EventCallback<AppTierAddOnModel> OnToggle { get; set; }

        /// <summary>Take one pack straight through (single-select only).</summary>
        [Parameter] public EventCallback<AppTierAddOnModel> OnChoose { get; set; }

        /// <summary>Continue with everything selected (multi-select only).</summary>
        [Parameter] public EventCallback OnContinue { get; set; }

        private bool IsMultiSelect
        {
            get { return Selection == PricingPackSelection.Multi; }
        }

        private int SelectedCount
        {
            get { return SelectedIds is null ? 0 : SelectedIds.Count; }
        }

        /// <summary>The headings and the packs under them, empty groups already dropped.</summary>
        private List<PackGroupView> Groups
        {
            get { return PricingViewDecisions.GroupAddOns(AddOns, AddOnGroups, Labels.MorePacks); }
        }

        private string ContinueLabel
        {
            get { return PricingViewDecisions.ContinueWithPacksLabel(SelectedCount, Labels); }
        }

        private bool IsSelected(string addOnId)
        {
            if (SelectedIds is null) return false;

            foreach (var id in SelectedIds)
            {
                if (string.Equals(id, addOnId, System.StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private string SelectAriaLabel(AppTierAddOnModel addOn)
        {
            return RegistrationSubscriptionLabels.Format(Labels.PackSelectNamed, "name", addOn.Name);
        }

        private Task HandleToggle(AppTierAddOnModel addOn)
        {
            return OnToggle.HasDelegate ? OnToggle.InvokeAsync(addOn) : Task.CompletedTask;
        }

        private Task HandleChoose(AppTierAddOnModel addOn)
        {
            return OnChoose.HasDelegate ? OnChoose.InvokeAsync(addOn) : Task.CompletedTask;
        }

        private Task HandleContinue()
        {
            return OnContinue.HasDelegate ? OnContinue.InvokeAsync() : Task.CompletedTask;
        }
    }
}
