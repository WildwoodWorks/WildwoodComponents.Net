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

    private async Task ConfirmStripePayment(string clientSecret)
    {
        try
        {
            var result = await InvokeJSAsync<PaymentJsResult>("wildwoodPayment.confirmStripePayment", clientSecret) 
                ?? new PaymentJsResult { Success = false, ErrorMessage = "JS call failed" };
            
            if (result.Success)
            {
                await CompletePayment(result.PaymentIntentId!);
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
        public string? ErrorMessage { get; set; }
        public string? ErrorCode { get; set; }
    }
}
