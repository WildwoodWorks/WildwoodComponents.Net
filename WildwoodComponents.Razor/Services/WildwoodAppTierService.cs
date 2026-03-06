using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// App tier service implementation that calls WildwoodAPI app-tier endpoints.
/// Razor Pages equivalent of WildwoodComponents.Blazor.Services.AppTierComponentService.
/// Uses server-side session for JWT token management via IWildwoodSessionManager.
/// </summary>
public class WildwoodAppTierService : IWildwoodAppTierService
{
    private readonly HttpClient _httpClient;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodAppTierService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WildwoodAppTierService(
        HttpClient httpClient,
        IWildwoodSessionManager sessionManager,
        ILogger<WildwoodAppTierService> logger)
    {
        _httpClient = httpClient;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    private void SetAuthHeader()
    {
        var token = _sessionManager.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    }

    #region Tier Browsing

    public async Task<List<AppTierModel>> GetAvailableTiersAsync(string appId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"api/app-tiers/{appId}");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<AppTierModel>>(JsonOptions);
                return result ?? new List<AppTierModel>();
            }

            _logger.LogWarning("Failed to get available tiers for app {AppId}: {StatusCode}", appId, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available tiers for app {AppId}", appId);
        }

        return new List<AppTierModel>();
    }

    public async Task<List<AppTierAddOnModel>> GetAvailableAddOnsAsync(string appId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"api/app-tier-addons/{appId}/available");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<AppTierAddOnModel>>(JsonOptions);
                return result ?? new List<AppTierAddOnModel>();
            }

            _logger.LogWarning("Failed to get available add-ons for app {AppId}: {StatusCode}", appId, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available add-ons for app {AppId}", appId);
        }

        return new List<AppTierAddOnModel>();
    }

    #endregion

    #region User Subscription

    public async Task<UserTierSubscriptionModel?> GetMySubscriptionAsync(string appId)
    {
        try
        {
            SetAuthHeader();
            var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/my-subscription");

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<UserTierSubscriptionModel>(JsonOptions);
            }

            if ((int)response.StatusCode == 404)
                return null;

            _logger.LogWarning("Failed to get subscription for app {AppId}: {StatusCode}", appId, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription for app {AppId}", appId);
        }

        return null;
    }

    public async Task<List<UserAddOnSubscriptionModel>> GetMyAddOnsAsync(string appId)
    {
        try
        {
            SetAuthHeader();
            var response = await _httpClient.GetAsync($"api/app-tier-addons/{appId}/my-addons");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<UserAddOnSubscriptionModel>>(JsonOptions);
                return result ?? new List<UserAddOnSubscriptionModel>();
            }

            _logger.LogWarning("Failed to get add-on subscriptions for app {AppId}: {StatusCode}", appId, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting add-on subscriptions for app {AppId}", appId);
        }

        return new List<UserAddOnSubscriptionModel>();
    }

    #endregion

    #region Tier Subscription Actions

    public async Task<AppTierChangeResultModel> SubscribeToTierAsync(string appId, string tierId, string? pricingId, string? paymentTransactionId)
    {
        try
        {
            SetAuthHeader();
            var body = new
            {
                AppId = appId,
                AppTierId = tierId,
                AppTierPricingId = pricingId,
                PaymentTransactionId = paymentTransactionId
            };

            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"api/app-tiers/{appId}/subscribe", content);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AppTierChangeResultModel>(JsonOptions);
                return result ?? new AppTierChangeResultModel { Success = false, ErrorMessage = "Empty response" };
            }

            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to subscribe to tier: {StatusCode} - {Body}", response.StatusCode, errorBody);
            return new AppTierChangeResultModel { Success = false, ErrorMessage = errorBody };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to tier {TierId} for app {AppId}", tierId, appId);
            return new AppTierChangeResultModel { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<AppTierChangeResultModel> ChangeTierAsync(string appId, string newTierId, string? newPricingId, bool immediate)
    {
        try
        {
            SetAuthHeader();
            var body = new
            {
                AppId = appId,
                NewAppTierId = newTierId,
                NewAppTierPricingId = newPricingId,
                Immediate = immediate
            };

            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"api/app-tiers/{appId}/change-tier", content);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AppTierChangeResultModel>(JsonOptions);
                return result ?? new AppTierChangeResultModel { Success = false, ErrorMessage = "Empty response" };
            }

            var errorBody = await response.Content.ReadAsStringAsync();
            return new AppTierChangeResultModel { Success = false, ErrorMessage = errorBody };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing tier for app {AppId}", appId);
            return new AppTierChangeResultModel { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<bool> CancelSubscriptionAsync(string appId)
    {
        try
        {
            SetAuthHeader();
            var response = await _httpClient.PostAsync($"api/app-tiers/{appId}/cancel-subscription", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling subscription for app {AppId}", appId);
            return false;
        }
    }

    #endregion

    #region Add-On Subscription Actions

    public async Task<bool> SubscribeToAddOnAsync(string appId, string addOnId, string? pricingId, string? paymentTransactionId)
    {
        try
        {
            SetAuthHeader();
            var body = new
            {
                AppId = appId,
                AppTierAddOnId = addOnId,
                AppTierAddOnPricingId = pricingId,
                PaymentTransactionId = paymentTransactionId
            };

            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"api/app-tier-addons/{appId}/subscribe", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to add-on {AddOnId} for app {AppId}", addOnId, appId);
            return false;
        }
    }

    public async Task<bool> CancelAddOnSubscriptionAsync(string subscriptionId)
    {
        try
        {
            SetAuthHeader();
            var response = await _httpClient.PostAsync($"api/app-tier-addons/subscriptions/{subscriptionId}/cancel", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling add-on subscription {SubscriptionId}", subscriptionId);
            return false;
        }
    }

    #endregion

    #region Feature Gating

    public async Task<Dictionary<string, bool>> GetUserFeaturesAsync(string appId)
    {
        try
        {
            SetAuthHeader();
            var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/user-features");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>(JsonOptions);
                return result ?? new Dictionary<string, bool>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user features for app {AppId}", appId);
        }

        return new Dictionary<string, bool>();
    }

    public async Task<AppFeatureCheckResultModel?> CheckFeatureAsync(string appId, string featureCode)
    {
        try
        {
            SetAuthHeader();
            var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/check-feature/{featureCode}");

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<AppFeatureCheckResultModel>(JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking feature {FeatureCode} for app {AppId}", featureCode, appId);
        }

        return null;
    }

    public async Task<AppTierLimitStatusModel?> CheckLimitAsync(string appId, string limitCode)
    {
        try
        {
            SetAuthHeader();
            var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/check-limit/{limitCode}");

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<AppTierLimitStatusModel>(JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking limit {LimitCode} for app {AppId}", limitCode, appId);
        }

        return null;
    }

    public async Task<AppTierLimitStatusModel?> IncrementUsageAsync(string appId, string limitCode)
    {
        try
        {
            SetAuthHeader();
            var response = await _httpClient.PostAsync($"api/app-tiers/{appId}/increment-usage/{limitCode}", null);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<AppTierLimitStatusModel>(JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error incrementing usage for limit {LimitCode} in app {AppId}", limitCode, appId);
        }

        return null;
    }

    #endregion
}
