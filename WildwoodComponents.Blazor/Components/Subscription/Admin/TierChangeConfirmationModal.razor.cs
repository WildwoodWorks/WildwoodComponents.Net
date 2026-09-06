using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Components.Subscription.Admin
{
    public partial class TierChangeConfirmationModal : BaseWildwoodComponent
    {
        [Parameter, EditorRequired]
        public TierChangePreviewModel Preview { get; set; } = default!;

        [Parameter]
        public EventCallback<TierChangeConfirmOptions> OnConfirm { get; set; }

        [Parameter]
        public EventCallback OnCancel { get; set; }

        /// <summary>
        /// True while the PARENT is applying the confirmed tier change. Distinct from the base
        /// class's <c>IsLoading</c>, which tracks this component's own async work (nothing here
        /// calls <c>SetLoadingAsync</c>, so that flag is always false).
        /// </summary>
        /// <remarks>
        /// This was previously declared as <c>[Parameter] public new bool IsLoading</c>, hiding the
        /// base property. Blazor tolerates that only because the base member carries no
        /// <c>[Parameter]</c> — had it done so, the duplicate parameter name would have made the
        /// component impossible to render (see ComponentParameterContractTests). It still left the
        /// base's <c>GetRootCssClasses</c> reading one flag while this component's markup read
        /// another, so the two concepts now have two names.
        /// </remarks>
        [Parameter]
        public bool IsProcessing { get; set; }

        private bool _immediate = true;
        private bool _bypassPayment;

        /// <summary>Payment is still required after accounting for the admin bypass toggle.</summary>
        private bool EffectivePaymentRequired => Preview.PaymentRequired && !_bypassPayment;

        /// <summary>Payment is required but cannot be collected (no provider) and cannot be bypassed.</summary>
        private bool ShowNoProviderWarning =>
            EffectivePaymentRequired && !Preview.PaymentProviderAvailable && !Preview.PaymentBypassAllowed;

        protected override void OnInitialized()
        {
            // Downgrades default to end-of-period; upgrades/other default to immediate.
            _immediate = !Preview.IsDowngrade;
        }

        private void SetImmediate(bool value)
        {
            _immediate = value;
            StateHasChanged();
        }

        private void ToggleBypassPayment(ChangeEventArgs e)
        {
            _bypassPayment = e.Value is true;
            StateHasChanged();
        }

        private async Task HandleConfirm()
        {
            if (OnConfirm.HasDelegate)
            {
                var options = new TierChangeConfirmOptions
                {
                    Immediate = _immediate,
                    BypassPayment = _bypassPayment
                };
                await OnConfirm.InvokeAsync(options);
            }
        }

        private async Task HandleCancel()
        {
            if (OnCancel.HasDelegate)
            {
                await OnCancel.InvokeAsync();
            }
        }

        private void HandleOverlayClick()
        {
            if (!IsProcessing)
            {
                _ = HandleCancel();
            }
        }

        private string FormatCurrency(decimal? amount, string currency)
        {
            if (!amount.HasValue)
            {
                return "$0.00";
            }

            var symbol = "$";
            if (string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase))
            {
                symbol = "€";
            }
            else if (string.Equals(currency, "GBP", StringComparison.OrdinalIgnoreCase))
            {
                symbol = "£";
            }

            return symbol + amount.Value.ToString("N2");
        }
    }

    public class TierChangeConfirmOptions
    {
        public bool Immediate { get; set; }
        public bool BypassPayment { get; set; }
    }
}
