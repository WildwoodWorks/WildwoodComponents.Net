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

    #region Tier Browsing

    public async Task<List<AppTierModel>> GetAvailableTiersAsync(string appId)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"api/app-tiers/{appId}");

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
            using var response = await _httpClient.GetAsync($"api/app-tier-addons/{appId}/available");

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
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/my-subscription");

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
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/app-tier-addons/{appId}/my-addons");

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
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var body = new
            {
                AppId = appId,
                AppTierId = tierId,
                AppTierPricingId = pricingId,
                PaymentTransactionId = paymentTransactionId
            };

            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync($"api/app-tiers/{appId}/subscribe", content);

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
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var body = new
            {
                AppId = appId,
                NewAppTierId = newTierId,
                NewAppTierPricingId = newPricingId,
                Immediate = immediate
            };

            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync($"api/app-tiers/{appId}/change-tier", content);

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
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/app-tiers/{appId}/cancel-subscription", null);
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
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var body = new
            {
                AppId = appId,
                AppTierAddOnId = addOnId,
                AppTierAddOnPricingId = pricingId,
                PaymentTransactionId = paymentTransactionId
            };

            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync($"api/app-tier-addons/{appId}/subscribe", content);
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
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/app-tier-addons/subscriptions/{subscriptionId}/cancel", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling add-on subscription {SubscriptionId}", subscriptionId);
            return false;
        }
    }

    #endregion

    #region Usage / Limit Statuses

    public async Task<List<AppTierLimitStatusModel>> GetAllLimitStatusesAsync(string appId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/limit-statuses");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<AppTierLimitStatusModel>>(JsonOptions);
                return result ?? new List<AppTierLimitStatusModel>();
            }

            _logger.LogWarning("Failed to get limit statuses for app {AppId}: {StatusCode}", appId, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting limit statuses for app {AppId}", appId);
        }

        return new List<AppTierLimitStatusModel>();
    }

    #endregion

    #region Public Tier Browsing

    public async Task<List<AppTierModel>> GetPublicTiersAsync(string appId)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/public");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<AppTierModel>>(JsonOptions);
                return result ?? new List<AppTierModel>();
            }

            _logger.LogWarning("Failed to get public tiers for app {AppId}: {StatusCode}", appId, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting public tiers for app {AppId}", appId);
        }

        return new List<AppTierModel>();
    }

    #endregion

    #region Company-Scoped Subscription (Admin)

    public async Task<UserTierSubscriptionModel?> GetCompanySubscriptionAsync(string appId, string companyId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/subscription/company/{companyId}");

            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<UserTierSubscriptionModel>(JsonOptions);

            if ((int)response.StatusCode == 404)
                return null;

            _logger.LogWarning("Failed to get company subscription: {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting company {CompanyId} subscription for app {AppId}", companyId, appId);
        }

        return null;
    }

    public async Task<List<UserAddOnSubscriptionModel>> GetCompanyAddOnSubscriptionsAsync(string appId, string companyId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/app-tier-addons/{appId}/company/{companyId}/addon-subscriptions");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<UserAddOnSubscriptionModel>>(JsonOptions);
                return result ?? new List<UserAddOnSubscriptionModel>();
            }

            _logger.LogWarning("Failed to get company add-on subscriptions: {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting company {CompanyId} add-on subscriptions for app {AppId}", companyId, appId);
        }

        return new List<UserAddOnSubscriptionModel>();
    }

    public async Task<List<AppTierLimitStatusModel>> GetCompanyLimitStatusesAsync(string appId, string companyId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/limits/company/{companyId}");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<AppTierLimitStatusModel>>(JsonOptions);
                return result ?? new List<AppTierLimitStatusModel>();
            }

            _logger.LogWarning("Failed to get company limit statuses: {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting company {CompanyId} limit statuses for app {AppId}", companyId, appId);
        }

        return new List<AppTierLimitStatusModel>();
    }

    #endregion

    #region Company-Scoped Features

    public async Task<List<AppFeatureDefinitionModel>> GetFeatureDefinitionsAsync(string appId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/app-feature-definitions/{appId}?activeOnly=true");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<AppFeatureDefinitionModel>>(JsonOptions);
                return result ?? new List<AppFeatureDefinitionModel>();
            }

            _logger.LogWarning("Failed to get feature definitions for app {AppId}: {StatusCode}", appId, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting feature definitions for app {AppId}", appId);
        }

        return new List<AppFeatureDefinitionModel>();
    }

    public async Task<Dictionary<string, bool>> GetCompanyFeaturesAsync(string appId, string companyId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/companies/{companyId}/features");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>(JsonOptions);
                return result ?? new Dictionary<string, bool>();
            }

            _logger.LogWarning("Failed to get company features: {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting company {CompanyId} features", companyId);
        }

        return new Dictionary<string, bool>();
    }

    #endregion

    #region Company-Scoped Admin Actions

    public async Task<AppTierChangeResultModel> SubscribeCompanyToTierAsync(string appId, string companyId, string tierId, string? pricingId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var body = new { CompanyId = companyId, AppTierId = tierId, AppTierPricingId = pricingId };
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync($"api/app-tiers/{appId}/subscribe/company", content);

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
            _logger.LogError(ex, "Error subscribing company {CompanyId} to tier {TierId}", companyId, tierId);
            return new AppTierChangeResultModel { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<AppTierChangeResultModel> ChangeCompanyTierAsync(string appId, string companyId, string newTierId, string? pricingId, bool immediate)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var body = new { CompanyId = companyId, NewAppTierId = newTierId, NewAppTierPricingId = pricingId, Immediate = immediate };
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync($"api/app-tiers/{appId}/change-tier/company", content);

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
            _logger.LogError(ex, "Error changing company {CompanyId} tier", companyId);
            return new AppTierChangeResultModel { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<bool> CancelCompanySubscriptionAsync(string appId, string companyId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/app-tiers/{appId}/cancel/company/{companyId}", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling company {CompanyId} subscription", companyId);
            return false;
        }
    }

    public async Task<bool> SubscribeCompanyToAddOnAsync(string appId, string companyId, string addOnId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var body = new { CompanyId = companyId, AppTierAddOnId = addOnId };
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync($"api/app-tier-addons/{appId}/subscribe/company", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing company {CompanyId} to add-on {AddOnId}", companyId, addOnId);
            return false;
        }
    }

    public async Task<bool> CancelCompanyAddOnAsync(string subscriptionId, bool immediate)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/app-tier-addons/subscriptions/{subscriptionId}/cancel?immediate={immediate}", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling company add-on subscription {SubscriptionId}", subscriptionId);
            return false;
        }
    }

    #endregion

    #region Admin Add-On Browsing

    public async Task<List<AppTierAddOnModel>> GetAllAddOnsAsync(string appId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/app-tier-addons/{appId}");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<AppTierAddOnModel>>(JsonOptions);
                return result ?? new List<AppTierAddOnModel>();
            }

            _logger.LogWarning("Failed to get all add-ons for app {AppId}: {StatusCode}", appId, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all add-ons for app {AppId}", appId);
        }

        return new List<AppTierAddOnModel>();
    }

    #endregion

    #region Admin User-Scoped Queries

    public async Task<UserTierSubscriptionModel?> GetUserSubscriptionAsync(string appId, string userId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/subscriptions/{userId}");

            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<UserTierSubscriptionModel>(JsonOptions);

            if ((int)response.StatusCode == 404)
                return null;

            _logger.LogWarning("Failed to get user subscription: {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user {UserId} subscription for app {AppId}", userId, appId);
        }

        return null;
    }

    public async Task<Dictionary<string, bool>> GetUserFeaturesAdminAsync(string appId, string userId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/admin/user-features/{userId}");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>(JsonOptions);
                return result ?? new Dictionary<string, bool>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting admin user features for user {UserId} in app {AppId}", userId, appId);
        }

        return new Dictionary<string, bool>();
    }

    public async Task<List<AppTierLimitStatusModel>> GetUserLimitStatusesAsync(string appId, string userId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/admin/user-limits/{userId}");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<AppTierLimitStatusModel>>(JsonOptions);
                return result ?? new List<AppTierLimitStatusModel>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user {UserId} limit statuses for app {AppId}", userId, appId);
        }

        return new List<AppTierLimitStatusModel>();
    }

    public async Task<List<UserAddOnSubscriptionModel>> GetUserAddOnsAsync(string appId, string userId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/app-tier-addons/{appId}/admin/user-addons/{userId}");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<UserAddOnSubscriptionModel>>(JsonOptions);
                return result ?? new List<UserAddOnSubscriptionModel>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user {UserId} add-ons for app {AppId}", userId, appId);
        }

        return new List<UserAddOnSubscriptionModel>();
    }

    public async Task<bool> CancelUserSubscriptionAsync(string appId, string userId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/app-tiers/{appId}/cancel/{userId}", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling subscription for user {UserId} in app {AppId}", userId, appId);
            return false;
        }
    }

    #endregion

    #region Admin User-Scoped Write Actions

    public async Task<AppTierChangeResultModel> SubscribeUserToTierAsync(string appId, string userId, string tierId, string? pricingId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var body = new { UserId = userId, AppId = appId, AppTierId = tierId, AppTierPricingId = pricingId };
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync("api/app-tiers/subscribe", content);

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
            _logger.LogError(ex, "Error subscribing user {UserId} to tier {TierId} for app {AppId}", userId, tierId, appId);
            return new AppTierChangeResultModel { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<AppTierChangeResultModel> ChangeUserTierAsync(string appId, string userId, string newTierId, string? newPricingId, bool immediate)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var body = new { UserId = userId, AppId = appId, NewAppTierId = newTierId, NewAppTierPricingId = newPricingId, Immediate = immediate };
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync("api/app-tiers/change-tier", content);

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
            _logger.LogError(ex, "Error changing tier for user {UserId} in app {AppId}", userId, appId);
            return new AppTierChangeResultModel { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<bool> SubscribeUserToAddOnAsync(string appId, string userId, string addOnId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var body = new { AppTierAddOnId = addOnId };
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync($"api/app-tier-addons/{appId}/admin/subscribe-user/{userId}", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing user {UserId} to add-on {AddOnId} for app {AppId}", userId, addOnId, appId);
            return false;
        }
    }

    public async Task<bool> CancelUserAddOnAsync(string appId, string subscriptionId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/app-tier-addons/{appId}/admin/cancel-user-addon/{subscriptionId}", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling user add-on subscription {SubscriptionId} for app {AppId}", subscriptionId, appId);
            return false;
        }
    }

    public async Task<bool> ResetUserUsageAsync(string appId, string userId, string limitCode)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/app-tiers/{appId}/admin/usage-limits/user/{userId}/{limitCode}/reset", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting usage for user {UserId} limit {LimitCode} in app {AppId}", userId, limitCode, appId);
            return false;
        }
    }

    public async Task<bool> UpdateUserUsageLimitAsync(string appId, string userId, string limitCode, int newMaxValue)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var content = JsonContent.Create(new { MaxValue = newMaxValue });
            using var response = await _httpClient.PutAsync($"api/app-tiers/{appId}/admin/usage-limits/user/{userId}/{limitCode}", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating usage limit for user {UserId} limit {LimitCode} in app {AppId}", userId, limitCode, appId);
            return false;
        }
    }

    #endregion

    #region Settings

    public async Task<string> GetTrackingModeAsync(string appId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/settings/tracking-mode");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<TrackingModeResponse>(JsonOptions);
                return result?.Mode ?? "User";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tracking mode for app {AppId}", appId);
        }

        return "User";
    }

    private class TrackingModeResponse
    {
        public string Mode { get; set; } = "User";
    }

    #endregion

    #region Admin Usage Limit Overrides

    public async Task<bool> UpdateUsageLimitAsync(string appId, string limitCode, int newMaxValue)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var body = new { MaxValue = newMaxValue };
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PutAsync($"api/app-tiers/{appId}/admin/usage-limits/{limitCode}", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating usage limit {LimitCode} for app {AppId}", limitCode, appId);
            return false;
        }
    }

    public async Task<bool> ResetUsageAsync(string appId, string limitCode)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/app-tiers/{appId}/admin/usage-limits/{limitCode}/reset", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting usage {LimitCode} for app {AppId}", limitCode, appId);
            return false;
        }
    }

    public async Task<bool> UpdateCompanyUsageLimitAsync(string appId, string companyId, string limitCode, int newMaxValue)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var body = new { MaxValue = newMaxValue };
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PutAsync($"api/app-tiers/{appId}/admin/usage-limits/company/{companyId}/{limitCode}", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating company {CompanyId} usage limit {LimitCode} for app {AppId}", companyId, limitCode, appId);
            return false;
        }
    }

    public async Task<bool> ResetCompanyUsageAsync(string appId, string companyId, string limitCode)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/app-tiers/{appId}/admin/usage-limits/company/{companyId}/{limitCode}/reset", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting company {CompanyId} usage {LimitCode} for app {AppId}", companyId, limitCode, appId);
            return false;
        }
    }

    #endregion

    #region Feature Overrides (Admin)

    public async Task<bool> SetFeatureOverrideAsync(string appId, string? userId, string featureCode, bool isEnabled, string? reason = null, DateTime? expiresAt = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var body = new { UserId = userId, FeatureCode = featureCode, IsEnabled = isEnabled, Reason = reason, ExpiresAt = expiresAt };
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync($"api/app-tiers/{appId}/admin/feature-overrides", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting feature override {FeatureCode} for app {AppId}", featureCode, appId);
            return false;
        }
    }

    public async Task<bool> RemoveFeatureOverrideAsync(string appId, string? userId, string featureCode)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var userQuery = !string.IsNullOrEmpty(userId) ? $"?userId={userId}" : "";
            using var response = await _httpClient.DeleteAsync($"api/app-tiers/{appId}/admin/feature-overrides/{featureCode}{userQuery}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing feature override {FeatureCode} for app {AppId}", featureCode, appId);
            return false;
        }
    }

    public async Task<List<AppFeatureOverrideModel>> GetFeatureOverridesAsync(string appId, string? userId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var userQuery = !string.IsNullOrEmpty(userId) ? $"?userId={userId}" : "";
            using var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/admin/feature-overrides{userQuery}");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<AppFeatureOverrideModel>>(JsonOptions);
                return result ?? new List<AppFeatureOverrideModel>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting feature overrides for app {AppId}", appId);
        }

        return new List<AppFeatureOverrideModel>();
    }

    #endregion

    #region Feature Gating

    public async Task<Dictionary<string, bool>> GetUserFeaturesAsync(string appId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/user-features");

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
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/check-feature/{featureCode}");

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
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/app-tiers/{appId}/check-limit/{limitCode}");

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
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/app-tiers/{appId}/increment-usage/{limitCode}", null);

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
