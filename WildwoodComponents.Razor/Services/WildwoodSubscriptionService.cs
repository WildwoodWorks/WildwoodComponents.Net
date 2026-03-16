using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Subscription service implementation that calls WildwoodAPI subscription endpoints.
/// Razor Pages equivalent of WildwoodComponents.Blazor's SubscriptionService.
/// Uses server-side session for JWT token management via IWildwoodSessionManager.
/// </summary>
public class WildwoodSubscriptionService : IWildwoodSubscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodSubscriptionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WildwoodSubscriptionService(
        HttpClient httpClient,
        IWildwoodSessionManager sessionManager,
        ILogger<WildwoodSubscriptionService> logger)
    {
        _httpClient = httpClient;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    #region Plan Discovery

    public async Task<List<SubscriptionPlanDto>> GetAvailablePlansAsync()
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync("api/subscription/plans");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<SubscriptionPlanDto>>(JsonOptions);
                return result ?? new List<SubscriptionPlanDto>();
            }

            _logger.LogWarning("Failed to get subscription plans: {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription plans");
        }

        return new List<SubscriptionPlanDto>();
    }

    #endregion

    #region Current Subscription

    public async Task<SubscriptionDto?> GetCurrentSubscriptionAsync(string? userId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var url = string.IsNullOrEmpty(userId)
                ? "api/subscription/current"
                : $"api/subscription/current/{userId}";
            using var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
            }

            if ((int)response.StatusCode == 404)
                return null;

            _logger.LogWarning("Failed to get current subscription: {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current subscription");
        }

        return null;
    }

    #endregion

    #region Subscription Actions

    public async Task<ApiResult> SubscribeToPlanAsync(string planId, string? userId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new { UserId = userId, PlanId = planId };
            using var response = await _httpClient.PostAsJsonAsync("api/subscription/subscribe", payload);

            if (response.IsSuccessStatusCode)
                return ApiResult.Ok("Subscription created successfully");

            var content = await response.Content.ReadAsStringAsync();
            var error = JsonSerializer.Deserialize<ApiErrorResponse>(content, JsonOptions);
            return ApiResult.Fail(error?.Message ?? "Subscription failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to plan {PlanId}", planId);
            return ApiResult.Fail("An error occurred while subscribing");
        }
    }

    public async Task<ApiResult> CancelSubscriptionAsync(string subscriptionId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/subscription/cancel/{subscriptionId}", null);

            if (response.IsSuccessStatusCode)
                return ApiResult.Ok("Subscription cancelled");

            var content = await response.Content.ReadAsStringAsync();
            var error = JsonSerializer.Deserialize<ApiErrorResponse>(content, JsonOptions);
            return ApiResult.Fail(error?.Message ?? "Cancellation failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel subscription {SubscriptionId}", subscriptionId);
            return ApiResult.Fail("An error occurred while cancelling");
        }
    }

    public async Task<ApiResult> PauseSubscriptionAsync(string subscriptionId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/subscription/pause/{subscriptionId}", null);

            if (response.IsSuccessStatusCode)
                return ApiResult.Ok("Subscription paused");

            var content = await response.Content.ReadAsStringAsync();
            var error = JsonSerializer.Deserialize<ApiErrorResponse>(content, JsonOptions);
            return ApiResult.Fail(error?.Message ?? "Failed to pause subscription");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause subscription {SubscriptionId}", subscriptionId);
            return ApiResult.Fail("An error occurred");
        }
    }

    public async Task<ApiResult> ResumeSubscriptionAsync(string subscriptionId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/subscription/resume/{subscriptionId}", null);

            if (response.IsSuccessStatusCode)
                return ApiResult.Ok("Subscription resumed");

            var content = await response.Content.ReadAsStringAsync();
            var error = JsonSerializer.Deserialize<ApiErrorResponse>(content, JsonOptions);
            return ApiResult.Fail(error?.Message ?? "Failed to resume subscription");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume subscription {SubscriptionId}", subscriptionId);
            return ApiResult.Fail("An error occurred");
        }
    }

    public async Task<ApiResult> UpgradeSubscriptionAsync(string subscriptionId, string newPlanId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new { NewPlanId = newPlanId };
            using var response = await _httpClient.PostAsJsonAsync($"api/subscription/upgrade/{subscriptionId}", payload);

            if (response.IsSuccessStatusCode)
                return ApiResult.Ok("Subscription upgraded");

            var content = await response.Content.ReadAsStringAsync();
            var error = JsonSerializer.Deserialize<ApiErrorResponse>(content, JsonOptions);
            return ApiResult.Fail(error?.Message ?? "Failed to upgrade subscription");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upgrade subscription {SubscriptionId}", subscriptionId);
            return ApiResult.Fail("An error occurred");
        }
    }

    #endregion

    #region Invoices

    public async Task<List<InvoiceDto>> GetInvoicesAsync(string? subscriptionId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var url = string.IsNullOrEmpty(subscriptionId)
                ? "api/subscription/invoices"
                : $"api/subscription/invoices?subscriptionId={subscriptionId}";
            using var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<InvoiceDto>>(JsonOptions);
                return result ?? new List<InvoiceDto>();
            }

            _logger.LogWarning("Failed to get invoices: {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting invoices");
        }

        return new List<InvoiceDto>();
    }

    #endregion
}
