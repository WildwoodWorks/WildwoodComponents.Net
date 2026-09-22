using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.TestHelpers;

/// <summary>
/// An <see cref="IPaymentProviderService"/> that records the two calls the registration +
/// subscription flows make of it — linking a transaction to a new user, and reading the app's
/// payment configuration for a publishable key — and refuses everything else.
/// </summary>
public class FakePaymentProviderService : IPaymentProviderService
{
    /// <summary>Every (externalTransactionId, userId) pair a signup linked.</summary>
    public List<(string ExternalTransactionId, string UserId)> Links { get; } = new();

    /// <summary>What <see cref="GetAppPaymentConfigurationAsync"/> answers.</summary>
    public AppPaymentConfigurationDto? Configuration { get; set; }

    /// <summary>How many times the configuration was read. The key is looked up once.</summary>
    public int ConfigurationReads { get; private set; }

    /// <summary>Set to make the link call fail, which a signup must survive.</summary>
    public Exception? LinkThrows { get; set; }

    public void SetAuthToken(string token)
    {
    }

    public void SetApiBaseUrl(string apiBaseUrl)
    {
    }

    public Task<AppPaymentConfigurationDto?> GetAppPaymentConfigurationAsync(string appId)
    {
        ConfigurationReads++;
        return Task.FromResult(Configuration);
    }

    public Task<PlatformFilteredProvidersDto> GetAvailableProvidersAsync(string appId)
        => Task.FromResult(new PlatformFilteredProvidersDto());

    public Task<InitiatePaymentResponse> InitiatePaymentAsync(InitiatePaymentRequest request)
        => Task.FromResult(new InitiatePaymentResponse());

    public Task<PaymentCompletionResult> ConfirmPaymentAsync(
        string paymentIntentId,
        PaymentProviderType providerType,
        Dictionary<string, object>? confirmationData = null)
        => Task.FromResult(new PaymentCompletionResult());

    [Obsolete("Mirrors the interface; the signup flows never call it.")]
    public Task<PaymentCompletionResult> ValidateAppStoreReceiptAsync(
        string appId,
        string receiptData,
        PaymentProviderType providerType)
        => Task.FromResult(new PaymentCompletionResult());

    public Task<PaymentCompletionResult> ValidateStorePurchaseAsync(string appId, StorePurchase purchase)
        => Task.FromResult(new PaymentCompletionResult());

    public Task<List<SavedPaymentMethodDto>> GetSavedPaymentMethodsAsync(string customerId)
        => Task.FromResult(new List<SavedPaymentMethodDto>());

    public Task<bool> DeleteSavedPaymentMethodAsync(string paymentMethodId) => Task.FromResult(true);

    public Task<bool> SetDefaultPaymentMethodAsync(string paymentMethodId) => Task.FromResult(true);

    public Task<PaymentCompletionResult> RequestRefundAsync(
        string transactionId,
        decimal? amount = null,
        string? reason = null)
        => Task.FromResult(new PaymentCompletionResult());

    public Task<PaymentCompletionResult> GetPaymentStatusAsync(string transactionId)
        => Task.FromResult(new PaymentCompletionResult());

    public Task<bool> LinkTransactionToUserAsync(
        string externalTransactionId,
        string userId,
        string? companyClientId = null)
    {
        if (LinkThrows is not null) throw LinkThrows;

        Links.Add((externalTransactionId, userId));
        return Task.FromResult(true);
    }
}
