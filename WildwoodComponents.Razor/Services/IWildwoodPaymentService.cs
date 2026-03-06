using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Payment service interface for WildwoodComponents.Razor.
/// Razor Pages equivalent of WildwoodComponents.Blazor's IPaymentProviderService.
/// Uses server-side session for JWT token management via IWildwoodSessionManager.
/// </summary>
public interface IWildwoodPaymentService
{
    /// <summary>
    /// Gets the payment configuration for an app
    /// </summary>
    Task<AppPaymentConfigurationDto?> GetAppPaymentConfigurationAsync(string appId);

    /// <summary>
    /// Gets available payment providers for the app (web platform)
    /// </summary>
    Task<PlatformFilteredProvidersDto?> GetAvailableProvidersAsync(string appId);

    /// <summary>
    /// Initiates a payment with the specified provider
    /// </summary>
    Task<InitiatePaymentResponse> InitiatePaymentAsync(InitiatePaymentRequest request);

    /// <summary>
    /// Confirms a payment after client-side processing (Stripe, etc.)
    /// </summary>
    Task<PaymentCompletionResult> ConfirmPaymentAsync(string paymentIntentId, PaymentProviderType providerType);

    /// <summary>
    /// Gets saved payment methods for a customer
    /// </summary>
    Task<List<SavedPaymentMethodDto>> GetSavedPaymentMethodsAsync(string customerId);

    /// <summary>
    /// Deletes a saved payment method
    /// </summary>
    Task<bool> DeleteSavedPaymentMethodAsync(string paymentMethodId);

    /// <summary>
    /// Sets a saved payment method as default
    /// </summary>
    Task<bool> SetDefaultPaymentMethodAsync(string paymentMethodId);

    /// <summary>
    /// Gets the payment status for a transaction
    /// </summary>
    Task<PaymentCompletionResult?> GetPaymentStatusAsync(string transactionId);

    /// <summary>
    /// Links a payment transaction to a user after registration
    /// </summary>
    Task<bool> LinkTransactionToUserAsync(string externalTransactionId, string userId, string? companyClientId = null);
}
