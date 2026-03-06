using System.Collections.Generic;
using System.Threading.Tasks;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Service for managing payment providers and processing payments
    /// </summary>
    public interface IPaymentProviderService
    {
        /// <summary>
        /// Sets the authentication token for API calls
        /// </summary>
        void SetAuthToken(string token);

        /// <summary>
        /// Sets the API base URL
        /// </summary>
        void SetApiBaseUrl(string apiBaseUrl);

        /// <summary>
        /// Gets the payment configuration for an app
        /// </summary>
        /// <param name="appId">The app ID</param>
        /// <returns>App payment configuration including enabled providers</returns>
        Task<AppPaymentConfigurationDto?> GetAppPaymentConfigurationAsync(string appId);

        /// <summary>
        /// Gets available payment providers for the current platform
        /// </summary>
        /// <param name="appId">The app ID</param>
        /// <returns>Platform-filtered providers</returns>
        Task<PlatformFilteredProvidersDto> GetAvailableProvidersAsync(string appId);

        /// <summary>
        /// Initiates a payment with the specified provider
        /// </summary>
        /// <param name="request">Payment initiation request</param>
        /// <returns>Payment initiation response with client secret or redirect URL</returns>
        Task<InitiatePaymentResponse> InitiatePaymentAsync(InitiatePaymentRequest request);

        /// <summary>
        /// Confirms a payment after client-side processing
        /// </summary>
        /// <param name="paymentIntentId">The payment intent ID</param>
        /// <param name="providerType">The provider type</param>
        /// <param name="confirmationData">Provider-specific confirmation data</param>
        /// <returns>Payment completion result</returns>
        Task<PaymentCompletionResult> ConfirmPaymentAsync(
            string paymentIntentId, 
            PaymentProviderType providerType,
            Dictionary<string, object>? confirmationData = null);

        /// <summary>
        /// Validates an app store receipt (Apple or Google)
        /// </summary>
        /// <param name="appId">The app ID</param>
        /// <param name="receiptData">The receipt data from the app store</param>
        /// <param name="providerType">Apple App Store or Google Play Store</param>
        /// <returns>Payment completion result</returns>
        Task<PaymentCompletionResult> ValidateAppStoreReceiptAsync(
            string appId,
            string receiptData,
            PaymentProviderType providerType);

        /// <summary>
        /// Gets saved payment methods for a customer
        /// </summary>
        /// <param name="customerId">The customer ID</param>
        /// <returns>List of saved payment methods</returns>
        Task<List<SavedPaymentMethodDto>> GetSavedPaymentMethodsAsync(string customerId);

        /// <summary>
        /// Deletes a saved payment method
        /// </summary>
        /// <param name="paymentMethodId">The payment method ID</param>
        /// <returns>True if successful</returns>
        Task<bool> DeleteSavedPaymentMethodAsync(string paymentMethodId);

        /// <summary>
        /// Sets a saved payment method as default
        /// </summary>
        /// <param name="paymentMethodId">The payment method ID</param>
        /// <returns>True if successful</returns>
        Task<bool> SetDefaultPaymentMethodAsync(string paymentMethodId);

        /// <summary>
        /// Requests a refund for a payment
        /// </summary>
        /// <param name="transactionId">The transaction ID</param>
        /// <param name="amount">Optional partial refund amount (full refund if null)</param>
        /// <param name="reason">Optional refund reason</param>
        /// <returns>Refund result</returns>
        Task<PaymentCompletionResult> RequestRefundAsync(
            string transactionId, 
            decimal? amount = null, 
            string? reason = null);

        /// <summary>
        /// Gets the payment status for a transaction
        /// </summary>
        /// <param name="transactionId">The transaction ID</param>
        /// <returns>Payment status</returns>
        Task<PaymentCompletionResult> GetPaymentStatusAsync(string transactionId);

        /// <summary>
        /// Links a payment transaction to a user after registration completes.
        /// Used when payment is collected before the user account is created.
        /// </summary>
        /// <param name="externalTransactionId">The external transaction ID (e.g., Stripe PaymentIntent ID, PayPal Order ID)</param>
        /// <param name="userId">The newly created user ID</param>
        /// <param name="companyClientId">Optional company client ID to associate</param>
        /// <returns>True if the transaction was successfully linked</returns>
        Task<bool> LinkTransactionToUserAsync(string externalTransactionId, string userId, string? companyClientId = null);
    }
}
