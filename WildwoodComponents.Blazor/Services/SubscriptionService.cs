using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Interface for Subscription Service operations.
    /// </summary>
    public interface ISubscriptionService
    {
        Task<List<SubscriptionPlan>> GetAvailablePlansAsync();
        Task<Subscription?> GetCurrentSubscriptionAsync(string userId);
        Task<SubscriptionResult> SubscribeToPlanAsync(string userId, string planId);
        Task<SubscriptionResult> CancelSubscriptionAsync(string subscriptionId);
        Task<SubscriptionResult> PauseSubscriptionAsync(string subscriptionId);
        Task<SubscriptionResult> ResumeSubscriptionAsync(string subscriptionId);
        Task<SubscriptionResult> UpgradeSubscriptionAsync(string subscriptionId, string newPlanId);
    }

    /// <summary>
    /// Subscription Service implementation for managing subscriptions.
    /// </summary>
    public class SubscriptionService : ISubscriptionService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<SubscriptionService> _logger;

        public SubscriptionService(HttpClient httpClient, ILogger<SubscriptionService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<List<SubscriptionPlan>> GetAvailablePlansAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/subscription/plans");
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<List<SubscriptionPlan>>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<SubscriptionPlan>();
                }
                else
                {
                    _logger.LogWarning("Failed to load subscription plans: {StatusCode}", response.StatusCode);
                    return new List<SubscriptionPlan>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading subscription plans");
                return new List<SubscriptionPlan>();
            }
        }

        public async Task<Subscription?> GetCurrentSubscriptionAsync(string userId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/api/subscription/current/{userId}");
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<Subscription>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null; // No current subscription
                }
                else
                {
                    _logger.LogWarning("Failed to load current subscription: {StatusCode}", response.StatusCode);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading current subscription for user {UserId}", userId);
                return null;
            }
        }

        public async Task<SubscriptionResult> SubscribeToPlanAsync(string userId, string planId)
        {
            try
            {
                var request = new { UserId = userId, PlanId = planId };
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync("/api/subscription/subscribe", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<SubscriptionResult>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new SubscriptionResult { IsSuccess = false, ErrorMessage = "Invalid response" };
                }
                else
                {
                    return new SubscriptionResult 
                    { 
                        IsSuccess = false, 
                        ErrorMessage = $"Subscription failed with status: {response.StatusCode}" 
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error subscribing to plan {PlanId} for user {UserId}", planId, userId);
                return new SubscriptionResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<SubscriptionResult> CancelSubscriptionAsync(string subscriptionId)
        {
            try
            {
                var response = await _httpClient.PostAsync($"/api/subscription/cancel/{subscriptionId}", null);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<SubscriptionResult>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new SubscriptionResult { IsSuccess = false, ErrorMessage = "Invalid response" };
                }
                else
                {
                    return new SubscriptionResult 
                    { 
                        IsSuccess = false, 
                        ErrorMessage = $"Cancellation failed with status: {response.StatusCode}" 
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error canceling subscription {SubscriptionId}", subscriptionId);
                return new SubscriptionResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<SubscriptionResult> PauseSubscriptionAsync(string subscriptionId)
        {
            try
            {
                var response = await _httpClient.PostAsync($"/api/subscription/pause/{subscriptionId}", null);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<SubscriptionResult>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new SubscriptionResult { IsSuccess = false, ErrorMessage = "Invalid response" };
                }
                else
                {
                    return new SubscriptionResult 
                    { 
                        IsSuccess = false, 
                        ErrorMessage = $"Pause failed with status: {response.StatusCode}" 
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pausing subscription {SubscriptionId}", subscriptionId);
                return new SubscriptionResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<SubscriptionResult> ResumeSubscriptionAsync(string subscriptionId)
        {
            try
            {
                var response = await _httpClient.PostAsync($"/api/subscription/resume/{subscriptionId}", null);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<SubscriptionResult>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new SubscriptionResult { IsSuccess = false, ErrorMessage = "Invalid response" };
                }
                else
                {
                    return new SubscriptionResult 
                    { 
                        IsSuccess = false, 
                        ErrorMessage = $"Resume failed with status: {response.StatusCode}" 
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resuming subscription {SubscriptionId}", subscriptionId);
                return new SubscriptionResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<SubscriptionResult> UpgradeSubscriptionAsync(string subscriptionId, string newPlanId)
        {
            try
            {
                var request = new { NewPlanId = newPlanId };
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"/api/subscription/upgrade/{subscriptionId}", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<SubscriptionResult>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new SubscriptionResult { IsSuccess = false, ErrorMessage = "Invalid response" };
                }
                else
                {
                    return new SubscriptionResult 
                    { 
                        IsSuccess = false, 
                        ErrorMessage = $"Upgrade failed with status: {response.StatusCode}" 
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error upgrading subscription {SubscriptionId} to plan {NewPlanId}", subscriptionId, newPlanId);
                return new SubscriptionResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
        }
    }
}
