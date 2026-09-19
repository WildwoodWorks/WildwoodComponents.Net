using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace WildwoodComponents.Blazor.Components.Payment;

/// <summary>
/// Stripe-specific half of <see cref="PaymentComponent"/>: card element initialisation and
/// teardown, payment confirmation and the JS-invokable callbacks StripePaymentScript.js raises.
/// </summary>
public partial class PaymentComponent
{
    /// <summary>
    /// Initialises the Stripe card element for the selected provider. Called from the Stripe
    /// branch of InitializeProviderJs, inside that method's try/catch.
    /// </summary>
    private async Task InitializeStripeAsync(PaymentProviderDto provider, string refKey)
    {
        if (!string.IsNullOrEmpty(provider.PublishableKey))
        {
            await LogToConsoleAsync($"[PaymentComponent] Initializing Stripe with key: {provider.PublishableKey.Substring(0, Math.Min(20, provider.PublishableKey.Length))}...");
            _dotNetRef ??= DotNetObjectReference.Create(this);
            await InvokeJSVoidAsync("wildwoodPayment.initStripe", 
                provider.PublishableKey, "stripe-card-element", _dotNetRef, refKey);
            _stripeInitialized = true;
            Logger?.LogInformation("InitializeProviderJs: Stripe initialized");
            await LogToConsoleAsync("[PaymentComponent] Stripe initialized successfully");
        }
        else
        {
            Logger?.LogWarning("InitializeProviderJs: Stripe provider missing PublishableKey");
            await LogToConsoleAsync("[PaymentComponent] ERROR: Stripe provider missing PublishableKey");
            await HandleErrorAsync(new Exception("Stripe is not properly configured (missing PublishableKey). Please contact support."), "Stripe configuration");
        }
    }

    private async Task ConfirmStripePayment(InitiatePaymentResponse initiation)
    {
        try
        {
            var result = await InvokeJSAsync<PaymentJsResult>("wildwoodPayment.confirmStripePayment", initiation.ClientSecret)
                ?? new PaymentJsResult { Success = false, ErrorMessage = "JS call failed" };

            if (result.Success)
            {
                // Confirm with the id the SERVER recorded for this payment (a subscription's first
                // invoice, or the PaymentIntent itself for a one-time payment) so it can verify it
                // with Stripe. The browser's id is the fallback, never an empty string.
                if (PaymentDecisions.ConfirmationId(initiation, result.PaymentIntentId) is not { Length: > 0 } confirmationId)
                {
                    await HandlePaymentError(PaymentDecisions.MissingPaymentIdMessage, null);
                    return;
                }

                await CompletePayment(confirmationId);
            }
            else
            {
                await HandlePaymentError(result.ErrorMessage ?? "Card payment failed", result.ErrorCode);
            }
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex, "Confirming card payment");
        }
    }

    /// <summary>
    /// Confirms a free trial's SetupIntent: nothing is charged now, but the card is saved so Stripe
    /// can charge it when the trial ends. Without this a trial "succeeded" with no card attached
    /// and there was nothing to bill.
    /// </summary>
    private async Task ConfirmStripeSetup(InitiatePaymentResponse initiation)
    {
        try
        {
            var result = await InvokeJSAsync<PaymentJsResult>("wildwoodPayment.confirmStripeSetup", initiation.ClientSecret)
                ?? new PaymentJsResult { Success = false, ErrorMessage = "JS call failed" };

            if (!result.Success || result.SetupIntentId is not { Length: > 0 } setupIntentId)
            {
                await HandlePaymentError(result.ErrorMessage ?? "Card setup failed", result.ErrorCode);
                return;
            }

            var providerType = (PaymentProviderType)(_selectedProvider?.ProviderType ?? (int)PaymentProviderType.Stripe);
            var serverResult = await PaymentProviderService.ConfirmPaymentAsync(setupIntentId, providerType);

            if (!serverResult.Success)
            {
                await HandlePaymentError(
                    serverResult.ErrorMessage ?? "Your card could not be verified. Please try another card.",
                    serverResult.ErrorCode);
                return;
            }

            // The server verifies the SetupIntent but need not echo its ids back, so fall back to
            // what this flow already knows: the intent the browser confirmed and the subscription
            // the initiation created.
            serverResult.PaymentIntentId ??= setupIntentId;
            serverResult.SubscriptionId ??= initiation.SubscriptionId;

            _pendingIntent = null;
            _pendingIntentKey = null;
            _trialEndsAt = initiation.TrialEnd;
            _paymentResult = serverResult;
            _paymentComplete = true;
            await NotifyPaymentSuccess();
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex, "Saving your card for the free trial");
        }
    }

    /// <summary>
    /// Tears the Stripe card element down. Called from DisposeAsync.
    /// </summary>
    private async Task DisposeStripeAsync()
    {
        if (_stripeInitialized)
        {
            try
            {
                await InvokeJSVoidAsync("wildwoodPayment.disposeStripe");
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }

    /// <summary>
    /// Called from JavaScript when card input changes
    /// </summary>
    [JSInvokable]
    public void OnCardChange(bool complete, string? error)
    {
        _cardComplete = complete;
        StateHasChanged();
    }

    /// <summary>
    /// Called from JavaScript when tracking prevention is detected.
    /// Shows a user-friendly warning message about potential payment issues.
    /// </summary>
    [JSInvokable]
    public void OnTrackingPreventionDetected()
    {
        _trackingPreventionDetected = true;
        Logger?.LogWarning("Browser tracking prevention detected - this may affect Stripe fraud detection features");
        StateHasChanged();
    }

    private class PaymentJsResult
    {
        public bool Success { get; set; }
        public string? PaymentIntentId { get; set; }

        /// <summary>The confirmed SetupIntent's id, set by <c>confirmStripeSetup</c> only.</summary>
        public string? SetupIntentId { get; set; }

        public string? ErrorMessage { get; set; }
        public string? ErrorCode { get; set; }
    }
}
