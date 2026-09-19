using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts
{
    /// <summary>
    /// A modal that sells packs to an account that already exists: pick, pay once, see what
    /// happened.
    /// </summary>
    /// <remarks>
    /// Ported from
    /// packages/wildwood-react/src/components/registrationSubscription/parts/PackPicker.tsx. It
    /// reuses the signup flow's grid and its checkout whole, because a pack bought a month after
    /// signing up is the same transaction as one bought during it. The overlay closes only BEFORE
    /// the checkout starts or AFTER the outcomes are in: a click must not abandon a purchase that
    /// is charging a card.
    /// </remarks>
    public partial class PackPicker : BaseWildwoodComponent
    {
        /// <summary>The app the packs belong to.</summary>
        [Parameter, EditorRequired] public string AppId { get; set; } = string.Empty;

        /// <summary>Everything the app sells that this account does not already have.</summary>
        [Parameter] public IReadOnlyList<AppTierAddOnModel> AddOns { get; set; } = new List<AppTierAddOnModel>();

        /// <summary>The currency every price in the grid is quoted in.</summary>
        [Parameter] public string? Currency { get; set; }

        /// <summary>Copy the host may replace.</summary>
        [Parameter] public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

        /// <summary>
        /// Raised as soon as the purchase finishes, with one outcome per pack that was asked for.
        /// The picker stays on screen showing them — the host refreshes behind it.
        /// </summary>
        [Parameter] public EventCallback<IReadOnlyList<SignupPackOutcome>> OnFinished { get; set; }

        /// <summary>The customer closed the picker, whether or not anything was bought.</summary>
        [Parameter] public EventCallback OnClose { get; set; }

        private List<string> _selectedIds = new List<string>();
        private List<AddOnCheckoutItemInput>? _items;
        private IReadOnlyList<SignupPackOutcome>? _outcomes;

        /// <summary>Pack names for the outcome list, so a pack the quote never priced still reads.</summary>
        private Dictionary<string, string> PackNames
        {
            get
            {
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var addOn in AddOns)
                {
                    if (addOn is not null) names[addOn.Id] = addOn.Name;
                }

                return names;
            }
        }

        private Task HandleToggleAsync(AppTierAddOnModel addOn)
        {
            var kept = new List<string>(_selectedIds.Count + 1);
            var removed = false;

            foreach (var id in _selectedIds)
            {
                if (string.Equals(id, addOn.Id, StringComparison.Ordinal)) { removed = true; continue; }
                kept.Add(id);
            }

            if (!removed) kept.Add(addOn.Id);
            _selectedIds = kept;
            StateHasChanged();
            return Task.CompletedTask;
        }

        private Task HandleChooseAsync(AppTierAddOnModel addOn)
        {
            _items = new List<AddOnCheckoutItemInput> { new AddOnCheckoutItemInput { AddOnId = addOn.Id } };
            StateHasChanged();
            return Task.CompletedTask;
        }

        private Task HandleContinueAsync()
        {
            _items = SignupViewDecisions.CheckoutItems(_selectedIds);
            StateHasChanged();
            return Task.CompletedTask;
        }

        private async Task HandleFinishedAsync(IReadOnlyList<SignupPackOutcome> outcomes)
        {
            _outcomes = outcomes;
            StateHasChanged();

            if (OnFinished.HasDelegate) await OnFinished.InvokeAsync(outcomes);
        }

        /// <summary>
        /// A click on the backdrop closes the picker only when nothing is in flight: before the
        /// checkout starts, or once the outcomes are in.
        /// </summary>
        private Task HandleOverlayClickAsync()
        {
            if (_outcomes is null && _items is not null) return Task.CompletedTask;
            return HandleCloseAsync();
        }

        private Task HandleCloseAsync()
        {
            return OnClose.HasDelegate ? OnClose.InvokeAsync() : Task.CompletedTask;
        }
    }
}
