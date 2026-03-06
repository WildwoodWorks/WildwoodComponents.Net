using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Implementation of the payment provider service
    /// </summary>
    public class PaymentProviderService : IPaymentProviderService
    {
        private readonly HttpClient _httpClient;
        private readonly IPlatformDetectionService _platformService;
        private readonly ILogger<PaymentProviderService> _logger;
        private string _authToken = string.Empty;
        private string _apiBaseUrl = string.Empty; // Must be configured via SetApiBaseUrl - no hardcoded default

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public PaymentProviderService(
            HttpClient httpClient,
            IPlatformDetectionService platformService,
            ILogger<PaymentProviderService> logger)
        {
            _httpClient = httpClient;
            _platformService = platformService;
            _logger = logger;
        }

        public void SetAuthToken(string token)
        {
            _authToken = token ?? string.Empty;
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", token);
            
            // Debug logging
            var tokenPreview = token?.Length > 20 ? token.Substring(0, 20) + "..." : token ?? "(null)";
            Console.WriteLine($"[PaymentProviderService] SetAuthToken called - Token: {tokenPreview}, Length: {token?.Length ?? 0}");
            Console.WriteLine($"[PaymentProviderService] Authorization header set: {_httpClient.DefaultRequestHeaders.Authorization}");
        }

        /// <summary>
        /// Sets the API key header for authenticating requests to the WildwoodAPI.
        /// </summary>
        /// <param name="apiKey">The API key value</param>
        public void SetApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine("[PaymentProviderService] SetApiKey called with null/empty key");
                return;
            }

            // Remove existing header if present
            _httpClient.DefaultRequestHeaders.Remove("X-API-Key");
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            
            var keyPreview = apiKey.Length > 8 ? apiKey.Substring(0, 8) + "..." : apiKey;
            Console.WriteLine($"[PaymentProviderService] SetApiKey called - Key: {keyPreview}");
        }

        public void SetApiBaseUrl(string apiBaseUrl)
        {
            _apiBaseUrl = apiBaseUrl ?? string.Empty;
            Console.WriteLine($"[PaymentProviderService] SetApiBaseUrl: {apiBaseUrl}");
            _logger.LogInformation("?? PaymentProviderService: API base URL set to: {ApiBaseUrl}", _apiBaseUrl);
        }

        public async Task<AppPaymentConfigurationDto?> GetAppPaymentConfigurationAsync(string appId)
        {
            try
            {
                var url = $"{_apiBaseUrl}/payment/configuration/{appId}";
                Console.WriteLine($"[PaymentProviderService] GetAppPaymentConfigurationAsync - URL: {url}");
                
                var response = await _httpClient.GetAsync(url);
                
                Console.WriteLine($"[PaymentProviderService] GetAppPaymentConfigurationAsync - Response: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[PaymentProviderService] GetAppPaymentConfigurationAsync - Raw JSON: {content.Substring(0, Math.Min(1000, content.Length))}...");
                    
                    var config = await response.Content.ReadFromJsonAsync<AppPaymentConfigurationDto>(JsonOptions);
                    
                    if (config != null)
                    {
                        Console.WriteLine($"[PaymentProviderService] Loaded {config.Providers.Count} providers for app {appId}");
                        foreach (var provider in config.Providers)
                        {
                            Console.WriteLine($"[PaymentProviderService]   - Provider: {provider.Name} (Type: {provider.ProviderType})");
                            Console.WriteLine($"[PaymentProviderService]     ClientId: {(string.IsNullOrEmpty(provider.ClientId) ? "EMPTY" : provider.ClientId.Substring(0, Math.Min(20, provider.ClientId.Length)) + "...")}" );
                            Console.WriteLine($"[PaymentProviderService]     PublishableKey: {(string.IsNullOrEmpty(provider.PublishableKey) ? "EMPTY" : "present")}");
                        }
                    }
                    
                    return config;
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[PaymentProviderService] GetAppPaymentConfigurationAsync - Error: {errorContent}");
                _logger.LogWarning("Failed to get payment configuration for app {AppId}: {StatusCode}",
                    appId, response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PaymentProviderService] GetAppPaymentConfigurationAsync - Exception: {ex.Message}");
                _logger.LogError(ex, "Error getting payment configuration for app {AppId}", appId);
                return null;
            }
        }

        public async Task<PlatformFilteredProvidersDto> GetAvailableProvidersAsync(string appId)
        {
            try
            {
                // Get the full configuration
                var config = await GetAppPaymentConfigurationAsync(appId);
                
                if (config == null)
                {
                    Console.WriteLine($"[PaymentProviderService] GetAvailableProvidersAsync - No config returned for appId: {appId}");
                    return new PlatformFilteredProvidersDto
                    {
                        AppId = appId,
                        Platform = _platformService.CurrentPlatform.ToString(),
                        AvailableProviders = new List<PaymentProviderDto>()
                    };
                }

                var platformFlags = _platformService.GetPlatformFlags();
                var requiresAppStore = _platformService.RequiresAppStorePayment;
                var requiredProviderType = _platformService.RequiredAppStoreProviderType;

                Console.WriteLine($"[PaymentProviderService] GetAvailableProvidersAsync - Platform: {_platformService.CurrentPlatform}, Flags: {platformFlags}, RequiresAppStore: {requiresAppStore}");
                Console.WriteLine($"[PaymentProviderService] GetAvailableProvidersAsync - Received {config.Providers.Count} providers from API (BEFORE client-side filtering)");

                // Log all providers from API before filtering
                if (config.Providers.Count > 0)
                {
                    Console.WriteLine($"[PaymentProviderService] === PROVIDERS FROM API (before filtering) ===");
                    foreach (var p in config.Providers)
                    {
                        Console.WriteLine($"[PaymentProviderService]   {p.Name}: Type={p.ProviderType}, Enabled={p.IsEnabled}, AllowedPlatforms={p.AllowedPlatforms}, HasKey={!string.IsNullOrEmpty(p.PublishableKey) || !string.IsNullOrEmpty(p.ClientId)}");
                    }
                }

                var filteredProviders = new List<PaymentProviderDto>();
                PaymentProviderDto? defaultProvider = null;
                string? requiredProviderId = null;

                // Filter providers based on platform
                foreach (var provider in config.Providers)
                {
                    Console.WriteLine($"[PaymentProviderService] Checking provider: {provider.Name} (Type: {provider.ProviderType})");
                    Console.WriteLine($"[PaymentProviderService]   IsEnabled: {provider.IsEnabled}, AllowedPlatforms: {provider.AllowedPlatforms} (need flag {platformFlags})");
                    
                    if (!provider.IsEnabled)
                    {
                        Console.WriteLine($"[PaymentProviderService]   SKIPPED: Not enabled");
                        continue;
                    }

                    // Check platform compatibility
                    var platformMatch = (provider.AllowedPlatforms & platformFlags) != 0;
                    Console.WriteLine($"[PaymentProviderService]   Platform check: ({provider.AllowedPlatforms} & {platformFlags}) = {provider.AllowedPlatforms & platformFlags}, match: {platformMatch}");
                    
                    if (!platformMatch)
                    {
                        Console.WriteLine($"[PaymentProviderService]   SKIPPED: Platform {_platformService.CurrentPlatform} (flag {platformFlags}) not in AllowedPlatforms {provider.AllowedPlatforms}");
                        Console.WriteLine($"[PaymentProviderService]   FIX: Update AllowedPlatforms to include Web (1). Current value: {provider.AllowedPlatforms}. To include Web, set to: {provider.AllowedPlatforms | 1}");
                        continue;
                    }

                    // If app store payment is required, only allow the required provider
                    if (requiresAppStore && provider.IsAppStoreExclusive)
                    {
                        if (requiredProviderType.HasValue && 
                            provider.ProviderType == requiredProviderType.Value)
                        {
                            filteredProviders.Clear();
                            filteredProviders.Add(provider);
                            requiredProviderId = provider.Id;
                            defaultProvider = provider;
                            Console.WriteLine($"[PaymentProviderService]   SELECTED: Required app store provider");
                            break;
                        }
                        Console.WriteLine($"[PaymentProviderService]   SKIPPED: App store exclusive but not required type");
                        continue;
                    }

                    // Skip app store providers on non-required platforms
                    if (IsAppStoreProvider((PaymentProviderType)provider.ProviderType) && !requiresAppStore)
                    {
                        Console.WriteLine($"[PaymentProviderService]   SKIPPED: App store provider on non-app-store platform");
                        continue;
                    }

                    // Check if this provider is available on current platform
                    if (!_platformService.IsProviderAvailable(provider.ProviderType))
                    {
                        Console.WriteLine($"[PaymentProviderService]   SKIPPED: Provider not available on platform (IsProviderAvailable returned false)");
                        continue;
                    }

                    Console.WriteLine($"[PaymentProviderService]   ADDED: Provider passed all checks");
                    filteredProviders.Add(provider);

                    if (provider.IsDefault && defaultProvider == null)
                        defaultProvider = provider;
                }

                // Sort by display order
                filteredProviders.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));

                // Set default to first provider if none specified
                if (defaultProvider == null && filteredProviders.Count > 0)
                    defaultProvider = filteredProviders[0];

                Console.WriteLine($"[PaymentProviderService] GetAvailableProvidersAsync - Final count: {filteredProviders.Count} providers AFTER client-side filtering");
                Console.WriteLine($"[PaymentProviderService] === SUMMARY: API returned {config.Providers.Count} providers, {filteredProviders.Count} passed client-side filtering ===");

                return new PlatformFilteredProvidersDto
                {
                    AppId = appId,
                    Platform = _platformService.CurrentPlatform.ToString(),
                    RequiresAppStorePayment = requiresAppStore,
                    RequiredProviderId = requiredProviderId,
                    AvailableProviders = filteredProviders,
                    DefaultProvider = defaultProvider
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PaymentProviderService] GetAvailableProvidersAsync - Exception: {ex.Message}");
                _logger.LogError(ex, "Error getting available providers for app {AppId}", appId);
                return new PlatformFilteredProvidersDto
                {
                    AppId = appId,
                    Platform = _platformService.CurrentPlatform.ToString(),
                    AvailableProviders = new List<PaymentProviderDto>()
                };
            }
        }

        public async Task<InitiatePaymentResponse> InitiatePaymentAsync(InitiatePaymentRequest request)
        {
            try
            {
                var url = $"{_apiBaseUrl}/payment/initiate";
                Console.WriteLine($"[PaymentProviderService] InitiatePaymentAsync - URL: {url}");
                Console.WriteLine($"[PaymentProviderService] InitiatePaymentAsync - ProviderId: {request.ProviderId}, Amount: {request.Amount}");
                Console.WriteLine($"[PaymentProviderService] InitiatePaymentAsync - Auth header: {_httpClient.DefaultRequestHeaders.Authorization}");
                
                _logger.LogInformation("Initiating payment for provider {ProviderId}, amount {Amount} {Currency}",
                    request.ProviderId, request.Amount, request.Currency);

                var json = JsonSerializer.Serialize(request, JsonOptions);
                Console.WriteLine($"[PaymentProviderService] InitiatePaymentAsync - Request JSON: {json.Substring(0, Math.Min(500, json.Length))}...");
                
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                
                Console.WriteLine($"[PaymentProviderService] InitiatePaymentAsync - Response: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<InitiatePaymentResponse>(JsonOptions);
                    Console.WriteLine($"[PaymentProviderService] InitiatePaymentAsync - Success! PaymentIntentId: {result?.PaymentIntentId}");
                    return result ?? new InitiatePaymentResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid response from server"
                    };
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[PaymentProviderService] InitiatePaymentAsync - Error response: {errorContent}");
                _logger.LogWarning("Payment initiation failed: {StatusCode} - {Error}",
                    response.StatusCode, errorContent);

                return new InitiatePaymentResponse
                {
                    Success = false,
                    ErrorMessage = $"Payment initiation failed: {response.StatusCode}",
                    ErrorCode = response.StatusCode.ToString()
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PaymentProviderService] InitiatePaymentAsync - Exception: {ex.Message}");
                _logger.LogError(ex, "Error initiating payment");
                return new InitiatePaymentResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<PaymentCompletionResult> ConfirmPaymentAsync(
            string paymentIntentId,
            PaymentProviderType providerType,
            Dictionary<string, object>? confirmationData = null)
        {
            try
            {
                var request = new
                {
                    PaymentIntentId = paymentIntentId,
                    ProviderType = providerType,
                    ConfirmationData = confirmationData
                };

                var json = JsonSerializer.Serialize(request, JsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(
                    $"{_apiBaseUrl}/payment/confirm", content);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<PaymentCompletionResult>(JsonOptions)
                        ?? new PaymentCompletionResult { Success = false, ErrorMessage = "Invalid response" };
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Payment confirmation failed: {StatusCode} - {Error}",
                    response.StatusCode, errorContent);

                return new PaymentCompletionResult
                {
                    Success = false,
                    ErrorMessage = $"Payment confirmation failed: {response.StatusCode}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error confirming payment {PaymentIntentId}", paymentIntentId);
                return new PaymentCompletionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<PaymentCompletionResult> ValidateAppStoreReceiptAsync(
            string appId,
            string receiptData,
            PaymentProviderType providerType)
        {
            try
            {
                var request = new
                {
                    AppId = appId,
                    ReceiptData = receiptData,
                    ProviderType = providerType
                };

                var json = JsonSerializer.Serialize(request, JsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var endpoint = providerType == PaymentProviderType.AppleAppStore
                    ? "payment/validate-apple-receipt"
                    : "payment/validate-google-receipt";

                var response = await _httpClient.PostAsync(
                    $"{_apiBaseUrl}/{endpoint}", content);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<PaymentCompletionResult>(JsonOptions)
                        ?? new PaymentCompletionResult { Success = false, ErrorMessage = "Invalid response" };
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Receipt validation failed: {StatusCode} - {Error}",
                    response.StatusCode, errorContent);

                return new PaymentCompletionResult
                {
                    Success = false,
                    ErrorMessage = $"Receipt validation failed: {response.StatusCode}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating app store receipt");
                return new PaymentCompletionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<List<SavedPaymentMethodDto>> GetSavedPaymentMethodsAsync(string customerId)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_apiBaseUrl}/payment/methods/{customerId}");

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<List<SavedPaymentMethodDto>>(JsonOptions)
                        ?? new List<SavedPaymentMethodDto>();
                }

                _logger.LogWarning("Failed to get saved payment methods: {StatusCode}", response.StatusCode);
                return new List<SavedPaymentMethodDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting saved payment methods for customer {CustomerId}", customerId);
                return new List<SavedPaymentMethodDto>();
            }
        }

        public async Task<bool> DeleteSavedPaymentMethodAsync(string paymentMethodId)
        {
            try
            {
                var response = await _httpClient.DeleteAsync(
                    $"{_apiBaseUrl}/payment/methods/{paymentMethodId}");

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
                var response = await _httpClient.PostAsync(
                    $"{_apiBaseUrl}/payment/methods/{paymentMethodId}/default", null);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting default payment method {PaymentMethodId}", paymentMethodId);
                return false;
            }
        }

        public async Task<PaymentCompletionResult> RequestRefundAsync(
            string transactionId,
            decimal? amount = null,
            string? reason = null)
        {
            try
            {
                var request = new
                {
                    TransactionId = transactionId,
                    Amount = amount,
                    Reason = reason
                };

                var json = JsonSerializer.Serialize(request, JsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(
                    $"{_apiBaseUrl}/payment/refund", content);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<PaymentCompletionResult>(JsonOptions)
                        ?? new PaymentCompletionResult { Success = false, ErrorMessage = "Invalid response" };
                }

                return new PaymentCompletionResult
                {
                    Success = false,
                    ErrorMessage = $"Refund request failed: {response.StatusCode}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting refund for transaction {TransactionId}", transactionId);
                return new PaymentCompletionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<PaymentCompletionResult> GetPaymentStatusAsync(string transactionId)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_apiBaseUrl}/payment/status/{transactionId}");

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<PaymentCompletionResult>(JsonOptions)
                        ?? new PaymentCompletionResult { Success = false, ErrorMessage = "Invalid response" };
                }

                return new PaymentCompletionResult
                {
                    Success = false,
                    ErrorMessage = $"Status check failed: {response.StatusCode}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting payment status for transaction {TransactionId}", transactionId);
                return new PaymentCompletionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <inheritdoc />
        public async Task<bool> LinkTransactionToUserAsync(string externalTransactionId, string userId, string? companyClientId = null)
        {
            try
            {
                Console.WriteLine($"[PaymentProviderService] LinkTransactionToUserAsync - ExternalId: {externalTransactionId}, UserId: {userId}");
                _logger.LogInformation("Linking transaction {ExternalTransactionId} to user {UserId}", 
                    externalTransactionId, userId);

                var request = new
                {
                    ExternalTransactionId = externalTransactionId,
                    UserId = userId,
                    CompanyClientId = companyClientId
                };

                var json = JsonSerializer.Serialize(request, JsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(
                    $"{_apiBaseUrl}/paymenttransactions/link-by-external-id", content);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[PaymentProviderService] LinkTransactionToUserAsync - Success");
                    _logger.LogInformation("Successfully linked transaction {ExternalTransactionId} to user {UserId}",
                        externalTransactionId, userId);
                    return true;
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[PaymentProviderService] LinkTransactionToUserAsync - Failed: {response.StatusCode} - {errorContent}");
                _logger.LogWarning("Failed to link transaction {ExternalTransactionId} to user {UserId}: {StatusCode} - {Error}",
                    externalTransactionId, userId, response.StatusCode, errorContent);
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PaymentProviderService] LinkTransactionToUserAsync - Exception: {ex.Message}");
                _logger.LogError(ex, "Error linking transaction {ExternalTransactionId} to user {UserId}",
                    externalTransactionId, userId);
                return false;
            }
        }

        private static bool IsAppStoreProvider(PaymentProviderType providerType)
        {
            return providerType == PaymentProviderType.AppleAppStore ||
                   providerType == PaymentProviderType.GooglePlayStore;
        }
    }
}
