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

        [Parameter]
        public new bool IsLoading { get; set; }

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
            if (!IsLoading)
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
