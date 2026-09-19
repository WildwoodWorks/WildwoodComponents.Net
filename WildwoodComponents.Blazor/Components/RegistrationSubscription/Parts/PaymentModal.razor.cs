using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Components.Subscription.Admin;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts
{
    /// <summary>
    /// The built-in card modal a plan change opens when the host brought no modal of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from
    /// packages/wildwood-react/src/components/registrationSubscription/parts/PaymentModal.tsx. It
    /// answers <see cref="OnSettled"/> exactly once — a payment SDK that calls back twice, or a
    /// close after a success, cannot cancel a charge that has already gone through — and a refused
    /// card is not the end of it: <c>PaymentComponent</c> shows its own message and stays mounted,
    /// which is why no failure callback is wired here.
    /// </para>
    /// <para>
    /// The transaction is attributed to the signed-in customer afterwards, best effort: the change
    /// is settled first so the attribution can never gate it, and a link that fails is logged
    /// rather than surfaced.
    /// </para>
    /// <para>
    /// Failures are reported through the INHERITED <see cref="BaseWildwoodComponent.OnError"/>
    /// callback, with the JS error code as the <c>Context</c>. It is never redeclared here: two
    /// <c>[Parameter]</c> properties of one name make a component impossible to render at all.
    /// </para>
    /// </remarks>
    public partial class PaymentModal : BaseWildwoodComponent
    {
        /// <summary>The code a payment that moved money with no id to complete the change is reported under.</summary>
        public const string PaymentUnconfirmedCode = "payment_unconfirmed";

        [Inject] private IPaymentProviderService PaymentProviderService { get; set; } = default!;

        [Inject] private IWildwoodSessionManager SessionManager { get; set; } = default!;

        /// <summary>The app the payment belongs to.</summary>
        [Parameter, EditorRequired] public string AppId { get; set; } = string.Empty;

        /// <summary>What the plan change needs paying for: the plan, its pricing model, price and trial.</summary>
        [Parameter, EditorRequired] public PaymentRequiredArgs Request { get; set; } = new PaymentRequiredArgs();

        /// <summary>The currency the plan is priced in.</summary>
        [Parameter] public string? Currency { get; set; }

        /// <summary>Copy the host may replace.</summary>
        [Parameter] public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

        /// <summary>
        /// Where a redirect-flow provider should come back to. Carried, never navigated to.
        /// </summary>
        [Parameter] public string? ReturnUrl { get; set; }

        /// <summary>Ask for a billing address before anything is charged.</summary>
        [Parameter] public bool RequireBillingAddress { get; set; }

        /// <summary>Render above another modal — a confirmation still on screen.</summary>
        [Parameter] public bool Stacked { get; set; }

        /// <summary>
        /// Called exactly once: the transaction id to complete the change with, or null when
        /// nothing was paid.
        /// </summary>
        [Parameter] public EventCallback<string?> OnSettled { get; set; }

        /// <summary>Latched, so nothing can answer twice.</summary>
        private bool _settled;

        private string Title
        {
            get { return RegistrationSubscriptionLabels.Format(Labels.UpgradeToPlan, "tier", Request.TierName); }
        }

        private decimal Amount
        {
            get { return Request.Price; }
        }

        private string PaymentCurrency
        {
            get { return Currency is { Length: > 0 } ? Currency : "USD"; }
        }

        private string? CustomerId
        {
            get { return SessionManager.UserId; }
        }

        private string? CustomerEmail
        {
            get { return SessionManager.UserEmail; }
        }

        private string OverlayCssClass
        {
            get
            {
                var overlay = Stacked ? "ww-modal-overlay ww-modal-overlay--stacked" : "ww-modal-overlay";
                return CssClass is { Length: > 0 } ? overlay + " " + CssClass : overlay;
            }
        }

        /// <summary>
        /// The card went through. A success with nothing to complete the change with is money that
        /// moved and a change that cannot be applied, so it is reported rather than swallowed.
        /// </summary>
        private async Task HandlePaymentSuccessAsync(PaymentSuccessEventArgs args)
        {
            var paymentTransactionId = args.TransactionId ?? args.PaymentIntentId;

            if (!(paymentTransactionId is { Length: > 0 }))
            {
                await InvokeOnErrorAsync(
                    new InvalidOperationException(Labels.PaymentUnconfirmed), PaymentUnconfirmedCode);
                await SettleAsync(null);
                return;
            }

            // Complete the change first: attribution must not gate it.
            await SettleAsync(paymentTransactionId);

            // The server looks a transaction up by the provider's own id when there is one.
            await LinkTransactionAsync(args.PaymentIntentId ?? paymentTransactionId);
        }

        private Task HandleCancelAsync()
        {
            return SettleAsync(null);
        }

        private async Task SettleAsync(string? paymentTransactionId)
        {
            if (_settled) return;
            _settled = true;

            if (OnSettled.HasDelegate) await OnSettled.InvokeAsync(paymentTransactionId);
        }

        /// <summary>
        /// Attributes the payment to the signed-in customer. Best effort on purpose: the plan has
        /// already changed, and an unattributed transaction is a reporting problem, not a billing
        /// one.
        /// </summary>
        private async Task LinkTransactionAsync(string externalTransactionId)
        {
            var userId = SessionManager.UserId;
            if (!(userId is { Length: > 0 })) return;

            try
            {
                await PaymentProviderService.LinkTransactionToUserAsync(externalTransactionId, userId);
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Could not attribute the plan change's payment to the signed-in user");
            }
        }
    }
}
