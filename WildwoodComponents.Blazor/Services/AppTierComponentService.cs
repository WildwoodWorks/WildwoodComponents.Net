using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Services
{
    public class AppTierComponentService : IAppTierComponentService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AppTierComponentService> _logger;
        private string _apiBaseUrl = string.Empty;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        public AppTierComponentService(HttpClient httpClient, ILogger<AppTierComponentService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public void SetApiBaseUrl(string apiBaseUrl)
        {
            _apiBaseUrl = apiBaseUrl?.TrimEnd('/') ?? string.Empty;
        }

        public void SetAuthToken(string token)
        {
            if (!string.IsNullOrEmpty(token))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        private string BuildUrl(string path)
        {
            if (!string.IsNullOrEmpty(_apiBaseUrl))
                return $"{_apiBaseUrl}/api/{path.TrimStart('/')}";
            return $"/api/{path.TrimStart('/')}";
        }

        private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = string.Empty;
                try { body = await response.Content.ReadAsStringAsync(); } catch { }
                var message = $"{operation} failed: HTTP {(int)response.StatusCode} {response.StatusCode}";
                if (!string.IsNullOrEmpty(body))
                    message += $" - {body}";
                _logger.LogWarning("{Message}", message);
                throw new HttpRequestException(message);
            }
        }

        #region Tier Browsing

        public async Task<List<AppTierModel>> GetAvailableTiersAsync(string appId)
        {
            var url = BuildUrl($"app-tiers/{appId}");
            var response = await _httpClient.GetAsync(url);
            await EnsureSuccessAsync(response, $"GetAvailableTiers({appId})");
            var result = await response.Content.ReadFromJsonAsync<List<AppTierModel>>(JsonOptions);
            return result ?? new List<AppTierModel>();
        }

        public async Task<List<AppTierAddOnModel>> GetAvailableAddOnsAsync(string appId)
        {
            var url = BuildUrl($"app-tier-addons/{appId}/available");
            var response = await _httpClient.GetAsync(url);
            await EnsureSuccessAsync(response, $"GetAvailableAddOns({appId})");
            var result = await response.Content.ReadFromJsonAsync<List<AppTierAddOnModel>>(JsonOptions);
            return result ?? new List<AppTierAddOnModel>();
        }

        public async Task<List<AppTierModel>> GetPublicTiersAsync(string appId)
        {
            try
            {
                var url = BuildUrl($"app-tiers/{appId}/public");
                var response = await _httpClient.GetAsync(url);

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

        #region Usage Tracking

        public async Task<List<AppTierLimitStatusModel>> GetAllLimitStatusesAsync(string appId)
        {
            var url = BuildUrl($"app-tiers/{appId}/limit-statuses");
            var response = await _httpClient.GetAsync(url);
            await EnsureSuccessAsync(response, $"GetAllLimitStatuses({appId})");
            var result = await response.Content.ReadFromJsonAsync<List<AppTierLimitStatusModel>>(JsonOptions);
            return result ?? new List<AppTierLimitStatusModel>();
        }

        #endregion

        #region User Subscription

        public async Task<UserTierSubscriptionModel?> GetMySubscriptionAsync(string appId)
        {
            var url = BuildUrl($"app-tiers/{appId}/my-subscription");
            var response = await _httpClient.GetAsync(url);
            if ((int)response.StatusCode == 404)
                return null;
            await EnsureSuccessAsync(response, $"GetMySubscription({appId})");
            return await response.Content.ReadFromJsonAsync<UserTierSubscriptionModel>(JsonOptions);
        }

        public async Task<List<UserAddOnSubscriptionModel>> GetMyAddOnsAsync(string appId)
        {
            var url = BuildUrl($"app-tier-addons/{appId}/my-addons");
            var response = await _httpClient.GetAsync(url);
            await EnsureSuccessAsync(response, $"GetMyAddOns({appId})");
            var result = await response.Content.ReadFromJsonAsync<List<UserAddOnSubscriptionModel>>(JsonOptions);
            return result ?? new List<UserAddOnSubscriptionModel>();
        }

        #endregion

        #region Tier Subscription Actions

        public async Task<AppTierChangeResultModel> SubscribeToTierAsync(string appId, string tierId, string? pricingId, string? paymentTransactionId)
        {
            try
            {
                var url = BuildUrl($"app-tiers/{appId}/my-subscription");
                var body = new
                {
                    AppTierId = tierId,
                    AppTierPricingId = pricingId,
                    PaymentTransactionId = paymentTransactionId
                };

                var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

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
                _logger.LogError(ex, "[AppTierService] Error subscribing to tier {TierId} for app {AppId}", tierId, appId);
                return new AppTierChangeResultModel { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<AppTierChangeResultModel> ChangeTierAsync(string appId, string newTierId, string? newPricingId, bool immediate)
        {
            try
            {
                var url = BuildUrl($"app-tiers/{appId}/my-subscription/change");
                var body = new
                {
                    NewAppTierId = newTierId,
                    NewAppTierPricingId = newPricingId,
                    Immediate = immediate
                };

                var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

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
                var url = BuildUrl($"app-tiers/{appId}/my-subscription/cancel");
                var response = await _httpClient.PostAsync(url, null);
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
                var url = BuildUrl($"app-tier-addons/{appId}/subscribe");
                var body = new
                {
                    AppId = appId,
                    AppTierAddOnId = addOnId,
                    AppTierAddOnPricingId = pricingId,
                    PaymentTransactionId = paymentTransactionId
                };

                var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
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
                var url = BuildUrl($"app-tier-addons/subscriptions/{subscriptionId}/cancel");
                var response = await _httpClient.PostAsync(url, null);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling add-on subscription {SubscriptionId}", subscriptionId);
                return false;
            }
        }

        #endregion

        #region Company-Scoped Subscription (Admin)

        public async Task<UserTierSubscriptionModel?> GetCompanySubscriptionAsync(string appId, string companyId)
        {
            var url = BuildUrl($"app-tiers/{appId}/subscription/company/{companyId}");
            var response = await _httpClient.GetAsync(url);
            if ((int)response.StatusCode == 404)
                return null;
            await EnsureSuccessAsync(response, $"GetCompanySubscription({companyId})");
            return await response.Content.ReadFromJsonAsync<UserTierSubscriptionModel>(JsonOptions);
        }

        public async Task<List<UserAddOnSubscriptionModel>> GetCompanyAddOnSubscriptionsAsync(string appId, string companyId)
        {
            var url = BuildUrl($"app-tier-addons/{appId}/company/{companyId}/addon-subscriptions");
            var response = await _httpClient.GetAsync(url);
            await EnsureSuccessAsync(response, $"GetCompanyAddOnSubscriptions({companyId})");
            var result = await response.Content.ReadFromJsonAsync<List<UserAddOnSubscriptionModel>>(JsonOptions);
            return result ?? new List<UserAddOnSubscriptionModel>();
        }

        public async Task<List<AppTierLimitStatusModel>> GetCompanyLimitStatusesAsync(string appId, string companyId)
        {
            var url = BuildUrl($"app-tiers/{appId}/limits/company/{companyId}");
            var response = await _httpClient.GetAsync(url);
            await EnsureSuccessAsync(response, $"GetCompanyLimitStatuses({companyId})");
            var result = await response.Content.ReadFromJsonAsync<List<AppTierLimitStatusModel>>(JsonOptions);
            return result ?? new List<AppTierLimitStatusModel>();
        }

        #endregion

        #region Company-Scoped Features

        public async Task<List<AppFeatureDefinitionModel>> GetFeatureDefinitionsAsync(string appId)
        {
            var url = BuildUrl($"app-feature-definitions/{appId}?activeOnly=true");
            var response = await _httpClient.GetAsync(url);
            await EnsureSuccessAsync(response, $"GetFeatureDefinitions({appId})");
            var result = await response.Content.ReadFromJsonAsync<List<AppFeatureDefinitionModel>>(JsonOptions);
            return result ?? new List<AppFeatureDefinitionModel>();
        }

        public async Task<Dictionary<string, bool>> GetCompanyFeaturesAsync(string appId, string companyId)
        {
            var url = BuildUrl($"companies/{companyId}/features");
            var response = await _httpClient.GetAsync(url);
            await EnsureSuccessAsync(response, $"GetCompanyFeatures({companyId})");
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>(JsonOptions);
            return result ?? new Dictionary<string, bool>();
        }

        #endregion

        #region Company-Scoped Admin Actions

        public async Task<AppTierChangeResultModel> SubscribeCompanyToTierAsync(string appId, string companyId, string tierId, string? pricingId)
        {
            try
            {
                var url = BuildUrl($"app-tiers/{appId}/subscribe/company");
                var body = new
                {
                    CompanyId = companyId,
                    AppTierId = tierId,
                    AppTierPricingId = pricingId
                };

                var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

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
                _logger.LogError(ex, "[AppTierService] Error subscribing company {CompanyId} to tier {TierId}", companyId, tierId);
                return new AppTierChangeResultModel { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<AppTierChangeResultModel> ChangeCompanyTierAsync(string appId, string companyId, string newTierId, string? pricingId, bool immediate)
        {
            try
            {
                var url = BuildUrl($"app-tiers/{appId}/change-tier/company");
                var body = new
                {
                    CompanyId = companyId,
                    NewAppTierId = newTierId,
                    NewAppTierPricingId = pricingId,
                    Immediate = immediate
                };

                var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

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
                var url = BuildUrl($"app-tiers/{appId}/cancel/company/{companyId}");
                var response = await _httpClient.PostAsync(url, null);
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
                var url = BuildUrl($"app-tier-addons/{appId}/subscribe/company");
                var body = new
                {
                    CompanyId = companyId,
                    AppTierAddOnId = addOnId
                };

                var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
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
                var url = BuildUrl($"app-tier-addons/subscriptions/{subscriptionId}/cancel?immediate={immediate}");
                var response = await _httpClient.PostAsync(url, null);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling add-on subscription {SubscriptionId}", subscriptionId);
                return false;
            }
        }

        public async Task<List<AppTierAddOnModel>> GetAllAddOnsAsync(string appId)
        {
            var url = BuildUrl($"app-tier-addons/{appId}");
            var response = await _httpClient.GetAsync(url);
            await EnsureSuccessAsync(response, $"GetAllAddOns({appId})");
            var result = await response.Content.ReadFromJsonAsync<List<AppTierAddOnModel>>(JsonOptions);
            return result ?? new List<AppTierAddOnModel>();
        }

        #endregion

        #region Admin User-Scoped Queries

        public async Task<UserTierSubscriptionModel?> GetUserSubscriptionAsync(string appId, string userId)
        {
            var url = BuildUrl($"app-tiers/{appId}/subscriptions/{userId}");
            var response = await _httpClient.GetAsync(url);
            if ((int)response.StatusCode == 404)
                return null;
            await EnsureSuccessAsync(response, $"GetUserSubscription({userId})");
            return await response.Content.ReadFromJsonAsync<UserTierSubscriptionModel>(JsonOptions);
        }

        public async Task<Dictionary<string, bool>> GetUserFeaturesAdminAsync(string appId, string userId)
        {
            var url = BuildUrl($"app-tiers/{appId}/admin/user-features/{userId}");
            var response = await _httpClient.GetAsync(url);
            await EnsureSuccessAsync(response, $"GetUserFeaturesAdmin({userId})");
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>(JsonOptions);
            return result ?? new Dictionary<string, bool>();
        }

        public async Task<List<AppTierLimitStatusModel>> GetUserLimitStatusesAsync(string appId, string userId)
        {
            var url = BuildUrl($"app-tiers/{appId}/admin/user-limits/{userId}");
            var response = await _httpClient.GetAsync(url);
            await EnsureSuccessAsync(response, $"GetUserLimitStatuses({userId})");
            var result = await response.Content.ReadFromJsonAsync<List<AppTierLimitStatusModel>>(JsonOptions);
            return result ?? new List<AppTierLimitStatusModel>();
        }

        public async Task<List<UserAddOnSubscriptionModel>> GetUserAddOnsAsync(string appId, string userId)
        {
            var url = BuildUrl($"app-tier-addons/{appId}/admin/user-addons/{userId}");
            var response = await _httpClient.GetAsync(url);
            await EnsureSuccessAsync(response, $"GetUserAddOns({userId})");
            var result = await response.Content.ReadFromJsonAsync<List<UserAddOnSubscriptionModel>>(JsonOptions);
            return result ?? new List<UserAddOnSubscriptionModel>();
        }

        public async Task<bool> CancelUserSubscriptionAsync(string appId, string userId)
        {
            try
            {
                var url = BuildUrl($"app-tiers/{appId}/cancel/{userId}");
                var response = await _httpClient.PostAsync(url, null);
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
                var url = BuildUrl($"app-tiers/subscribe");
                var body = new
                {
                    UserId = userId,
                    AppId = appId,
                    AppTierId = tierId,
                    AppTierPricingId = pricingId
                };

                var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

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
                _logger.LogError(ex, "[AppTierService] Error subscribing user {UserId} to tier {TierId} for app {AppId}", userId, tierId, appId);
                return new AppTierChangeResultModel { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<AppTierChangeResultModel> ChangeUserTierAsync(string appId, string userId, string newTierId, string? newPricingId, bool immediate)
        {
            try
            {
                var url = BuildUrl($"app-tiers/change-tier");
                var body = new
                {
                    UserId = userId,
                    AppId = appId,
                    NewAppTierId = newTierId,
                    NewAppTierPricingId = newPricingId,
                    Immediate = immediate
                };

                var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

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
                var url = BuildUrl($"app-tier-addons/{appId}/admin/subscribe-user/{userId}");
                var body = new { AppTierAddOnId = addOnId };
                var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
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
                var url = BuildUrl($"app-tier-addons/{appId}/admin/cancel-user-addon/{subscriptionId}");
                var response = await _httpClient.PostAsync(url, null);
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
                var url = BuildUrl($"app-tiers/{appId}/admin/usage-limits/user/{userId}/{limitCode}/reset");
                var response = await _httpClient.PostAsync(url, null);
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
                var url = BuildUrl($"app-tiers/{appId}/admin/usage-limits/user/{userId}/{limitCode}");
                var content = JsonContent.Create(new { MaxValue = newMaxValue });
                var response = await _httpClient.PutAsync(url, content);
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
            var url = BuildUrl($"app-tiers/{appId}/settings/tracking-mode");
            var response = await _httpClient.GetAsync(url);
            await EnsureSuccessAsync(response, $"GetTrackingMode({appId})");
            var result = await response.Content.ReadFromJsonAsync<TrackingModeResponse>(JsonOptions);
            return result?.Mode ?? "User";
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
                var url = BuildUrl($"app-tiers/{appId}/admin/usage-limits/{limitCode}");
                var body = new { MaxValue = newMaxValue };
                var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
                var response = await _httpClient.PutAsync(url, content);
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
                var url = BuildUrl($"app-tiers/{appId}/admin/usage-limits/{limitCode}/reset");
                var response = await _httpClient.PostAsync(url, null);
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
                var url = BuildUrl($"app-tiers/{appId}/admin/usage-limits/company/{companyId}/{limitCode}");
                var body = new { MaxValue = newMaxValue };
                var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
                var response = await _httpClient.PutAsync(url, content);
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
                var url = BuildUrl($"app-tiers/{appId}/admin/usage-limits/company/{companyId}/{limitCode}/reset");
                var response = await _httpClient.PostAsync(url, null);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting company {CompanyId} usage {LimitCode} for app {AppId}", companyId, limitCode, appId);
                return false;
            }
        }

        #endregion

        #region Feature Gating

        public async Task<Dictionary<string, bool>> GetUserFeaturesAsync(string appId)
        {
            var url = BuildUrl($"app-tiers/{appId}/user-features");
            var response = await _httpClient.GetAsync(url);
            await EnsureSuccessAsync(response, $"GetUserFeatures({appId})");
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>(JsonOptions);
            return result ?? new Dictionary<string, bool>();
        }

        public async Task<AppFeatureCheckResultModel?> CheckFeatureAsync(string appId, string featureCode)
        {
            try
            {
                var url = BuildUrl($"app-tiers/{appId}/check-feature/{featureCode}");
                var response = await _httpClient.GetAsync(url);

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
                var url = BuildUrl($"app-tiers/{appId}/check-limit/{limitCode}");
                var response = await _httpClient.GetAsync(url);

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
                var url = BuildUrl($"app-tiers/{appId}/increment-usage/{limitCode}");
                var response = await _httpClient.PostAsync(url, null);

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

        #region Feature Overrides

        public async Task<bool> SetFeatureOverrideAsync(string appId, string? userId, string featureCode, bool isEnabled, string? reason = null, DateTime? expiresAt = null)
        {
            var url = BuildUrl($"app-tiers/{appId}/admin/feature-overrides");
            var body = new
            {
                UserId = userId,
                FeatureCode = featureCode,
                IsEnabled = isEnabled,
                Reason = reason,
                ExpiresAt = expiresAt
            };
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            await EnsureSuccessAsync(response, $"SetFeatureOverride({appId}, {featureCode})");
            return true;
        }

        public async Task<bool> RemoveFeatureOverrideAsync(string appId, string? userId, string featureCode)
        {
            try
            {
                var userQuery = !string.IsNullOrEmpty(userId) ? $"?userId={userId}" : "";
                var url = BuildUrl($"app-tiers/{appId}/admin/feature-overrides/{featureCode}{userQuery}");
                var response = await _httpClient.DeleteAsync(url);
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
                var userQuery = !string.IsNullOrEmpty(userId) ? $"?userId={userId}" : "";
                var url = BuildUrl($"app-tiers/{appId}/admin/feature-overrides{userQuery}");
                var response = await _httpClient.GetAsync(url);
                await EnsureSuccessAsync(response, "Getting feature overrides");
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<AppFeatureOverrideModel>>(json, JsonOptions)
                    ?? new List<AppFeatureOverrideModel>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting feature overrides for app {AppId}", appId);
                return new List<AppFeatureOverrideModel>();
            }
        }

        #endregion
    }
}
