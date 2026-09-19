using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription
{
    /// <summary>
    /// Puts a card challenge to the customer through the embedded Stripe script.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The web half of <see cref="IPaymentActionAdapter"/>. It confirms a PaymentIntent for a card
    /// ALREADY on file — the 3-D Secure a pack bought against the saved card may still need — so
    /// there is no card element to mount and none is asked for: the intent already carries its
    /// payment method, and the customer is only being asked to authenticate it.
    /// </para>
    /// <para>
    /// The instance key keeps this off whatever card form is on the page. Given a publishable key
    /// the script creates a bare Stripe instance under that key when there is no mounted form at
    /// all, which is the ordinary case here: the saved-card path never renders one.
    /// </para>
    /// <para>
    /// That lazily created instance is THIS object's to clean up — it holds the key, so it owns
    /// what lives under it. <see cref="DisposeAsync"/> drops it, and the owner of this adapter
    /// (<c>PackCheckoutDriver</c>, through <c>PackCheckout</c>) disposes it when the checkout
    /// leaves the page. Without that, every signup that needed 3-D Secure would leave a Stripe SDK
    /// instance behind for the life of the circuit.
    /// </para>
    /// </remarks>
    internal sealed class StripePaymentActions : IPaymentActionAdapter, IAsyncDisposable
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly string _instanceKey;
        private bool _disposed;

        public StripePaymentActions(IJSRuntime jsRuntime, string instanceKey)
        {
            _jsRuntime = jsRuntime;
            _instanceKey = instanceKey;
        }

        /// <summary>The script instance this adapter created and is responsible for.</summary>
        public string InstanceKey
        {
            get { return _instanceKey; }
        }

        /// <inheritdoc />
        public async Task<PaymentActionOutcome> ConfirmPaymentAsync(string clientSecret, string? publishableKey)
        {
            // A challenge that arrives after the checkout left the page has nothing to confirm
            // against: the instance it would use has already been dropped.
            if (_disposed) return PaymentActionOutcome.Failed(null);

            try
            {
                var result = await _jsRuntime.InvokeAsync<StripeActionResult?>(
                    "wildwoodPayment.confirmStripeCardPayment", clientSecret, _instanceKey, publishableKey);

                if (result is not null && result.Success) return PaymentActionOutcome.Success;
                return PaymentActionOutcome.Failed(result?.ErrorMessage);
            }
            catch (Exception ex)
            {
                return PaymentActionOutcome.Failed(ex.Message);
            }
        }

        /// <summary>
        /// Drops the script instance this adapter's key names. Idempotent, and quiet when the
        /// circuit has already gone: a disposal that throws on the way out of a page is noise, not
        /// news.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                await _jsRuntime.InvokeVoidAsync("wildwoodPayment.disposeStripe", _instanceKey);
            }
            catch (JSDisconnectedException)
            {
                // The circuit is gone; so is everything the script was holding.
            }
            catch (OperationCanceledException)
            {
                // The interop call was cut short by the same teardown.
            }
            catch (ObjectDisposedException)
            {
                // The JS runtime went first.
            }
        }

        /// <summary>The shape every confirmation in the script answers with.</summary>
        private sealed class StripeActionResult
        {
            public bool Success { get; set; }

            public string? PaymentIntentId { get; set; }

            public string? ErrorMessage { get; set; }

            public string? ErrorCode { get; set; }
        }
    }
}
