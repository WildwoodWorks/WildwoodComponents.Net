using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Payment service implementation that calls WildwoodAPI payment endpoints.
/// Razor Pages equivalent of WildwoodComponents.Blazor's PaymentProviderService.
/// Uses server-side session for JWT token management via IWildwoodSessionManager.
/// </summary>
public class WildwoodPaymentService : IWildwoodPaymentService
{
    private readonly HttpClient _httpClient;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodPaymentService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WildwoodPaymentService(
        HttpClient httpClient,
        IWildwoodSessionManager sessionManager,
        ILogger<WildwoodPaymentService> logger)
    {
        _httpClient = httpClient;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    #region Provider Discovery

    public async Task<AppPaymentConfigurationDto?> GetAppPaymentConfigurationAsync(string appId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/payment/configuration/{appId}");

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<AppPaymentConfigurationDto>(JsonOptions);
            }

            _logger.LogWarning("Failed to get payment configuration for app {AppId}: {StatusCode}", appId, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment configuration for app {AppId}", appId);
        }

        return null;
    }

    public async Task<PlatformFilteredProvidersDto?> GetAvailableProvidersAsync(string appId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            // Web platform = 1
            using var response = await _httpClient.GetAsync($"api/payment/providers/{appId}?platform=Web");

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<PlatformFilteredProvidersDto>(JsonOptions);
            }

            _logger.LogWarning("Failed to get available providers for app {AppId}: {StatusCode}", appId, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available providers for app {AppId}", appId);
        }

        return null;
    }

    #endregion

    #region Payment Processing

    public async Task<InitiatePaymentResponse> InitiatePaymentAsync(InitiatePaymentRequest request)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync("api/payment/initiate", content);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<InitiatePaymentResponse>(JsonOptions);
                return result ?? new InitiatePaymentResponse { Success = false, ErrorMessage = "Empty response" };
            }

            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to initiate payment: {StatusCode} - {Body}", response.StatusCode, errorBody);
            return new InitiatePaymentResponse { Success = false, ErrorMessage = errorBody };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating payment for app {AppId}", request.AppId);
            return new InitiatePaymentResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<PaymentCompletionResult> ConfirmPaymentAsync(string paymentIntentId, PaymentProviderType providerType)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var body = new { PaymentIntentId = paymentIntentId, ProviderType = providerType };
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync("api/payment/confirm", content);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<PaymentCompletionResult>(JsonOptions);
                return result ?? new PaymentCompletionResult { Success = false, ErrorMessage = "Empty response" };
            }

            var errorBody = await response.Content.ReadAsStringAsync();
            return new PaymentCompletionResult { Success = false, ErrorMessage = errorBody };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming payment {PaymentIntentId}", paymentIntentId);
            return new PaymentCompletionResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    #endregion

    #region Saved Payment Methods

    public async Task<List<SavedPaymentMethodDto>> GetSavedPaymentMethodsAsync(string customerId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/payment/methods/{customerId}");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<SavedPaymentMethodDto>>(JsonOptions);
                return result ?? new List<SavedPaymentMethodDto>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting saved payment methods for customer {CustomerId}", customerId);
        }

        return new List<SavedPaymentMethodDto>();
    }

    public async Task<bool> DeleteSavedPaymentMethodAsync(string paymentMethodId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.DeleteAsync($"api/payment/methods/{paymentMethodId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting payment method {PaymentMethodId}", paymentMethodId);
            return false;
        }
    }

    public async Task<bool> SetDefaultPaymentMethodAsync(string paymentMethodId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/payment/methods/{paymentMethodId}/default", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting default payment method {PaymentMethodId}", paymentMethodId);
            return false;
        }
    }

    #endregion

    #region Status & Linking

    public async Task<PaymentCompletionResult?> GetPaymentStatusAsync(string transactionId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/payment/status/{transactionId}");

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<PaymentCompletionResult>(JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment status for transaction {TransactionId}", transactionId);
        }

        return null;
    }

    public async Task<bool> LinkTransactionToUserAsync(string externalTransactionId, string userId, string? companyClientId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var body = new
            {
                ExternalTransactionId = externalTransactionId,
                UserId = userId,
                CompanyClientId = companyClientId
            };
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync("api/payment/link-transaction", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error linking transaction {TransactionId} to user {UserId}", externalTransactionId, userId);
            return false;
        }
    }

    #endregion
}
