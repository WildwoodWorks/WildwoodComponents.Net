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
    /// <summary>
    /// Client for the app-facing flow-subscription endpoints
    /// (api/ai/flows/subscriptions): a user's standing orders for scheduled runs
    /// of published flows. GetLatestRunAsync returns the last scheduled run's
    /// full detail (including OutputJson) so a client can sync a fresh result
    /// after a completion notification.
    /// </summary>
    public interface IAIFlowSubscriptionService
    {
        event EventHandler? AuthenticationFailed;

        void SetAuthToken(string token);
        void SetApiBaseUrl(string apiBaseUrl);
        void SetAppId(string? appId);

        Task<List<AIFlowSubscription>> GetSubscriptionsAsync();
        Task<AIFlowSubscription?> CreateAsync(AIFlowSubscriptionCreateRequest request);
        Task<AIFlowSubscription?> UpdateAsync(string subscriptionId, AIFlowSubscriptionUpdateRequest request);
        Task<AIFlowSubscription?> SetEnabledAsync(string subscriptionId, bool enabled);
        Task<bool> DeleteAsync(string subscriptionId);
        Task<AIFlowRunDetail?> GetLatestRunAsync(string subscriptionId);

        /// <summary>Non-null after a 429 create failure — the server's limit message (upgrade CTA copy).</summary>
        string? LastLimitMessage { get; }
    }

    public class AIFlowSubscriptionService : IAIFlowSubscriptionService
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _httpClient;
        private readonly ILogger<AIFlowSubscriptionService> _logger;

        private string _authToken = string.Empty;
        private string _apiBaseUrl = string.Empty;
        private string? _appId;
        private bool _authFailureFired;

        public event EventHandler? AuthenticationFailed;

        public string? LastLimitMessage { get; private set; }

        public AIFlowSubscriptionService(HttpClient httpClient, ILogger<AIFlowSubscriptionService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public void SetAuthToken(string token)
        {
            _authToken = token ?? string.Empty;
            _authFailureFired = false; // re-arm the one-shot 401 signal for the new token
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _authToken);
        }

        public void SetApiBaseUrl(string apiBaseUrl) => _apiBaseUrl = apiBaseUrl?.TrimEnd('/') ?? string.Empty;

        public void SetAppId(string? appId) => _appId = appId;

        public async Task<List<AIFlowSubscription>> GetSubscriptionsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{BaseRoute()}{AppQuery()}");
                if (!EnsureAuthorized(response)) return new();
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<AIFlowSubscription>>(Json) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load flow subscriptions");
                return new();
            }
        }

        public async Task<AIFlowSubscription?> CreateAsync(AIFlowSubscriptionCreateRequest request)
        {
            LastLimitMessage = null;
            try
            {
                var response = await _httpClient.PostAsync($"{BaseRoute()}{AppQuery()}",
                    JsonContent(request));
                if (!EnsureAuthorized(response)) return null;
                if ((int)response.StatusCode == 429)
                {
                    LastLimitMessage = await ExtractLimitMessageAsync(response);
                    return null;
                }
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<AIFlowSubscription>(Json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create flow subscription");
                return null;
            }
        }

        public async Task<AIFlowSubscription?> UpdateAsync(string subscriptionId, AIFlowSubscriptionUpdateRequest request)
        {
            try
            {
                var response = await _httpClient.PutAsync(
                    $"{BaseRoute()}/{Uri.EscapeDataString(subscriptionId)}{AppQuery()}",
                    JsonContent(request));
                if (!EnsureAuthorized(response)) return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<AIFlowSubscription>(Json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update flow subscription {SubscriptionId}", subscriptionId);
                return null;
            }
        }

        public async Task<AIFlowSubscription?> SetEnabledAsync(string subscriptionId, bool enabled)
        {
            try
            {
                var action = enabled ? "enable" : "disable";
                var response = await _httpClient.PostAsync(
                    $"{BaseRoute()}/{Uri.EscapeDataString(subscriptionId)}/{action}{AppQuery()}",
                    content: null);
                if (!EnsureAuthorized(response)) return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<AIFlowSubscription>(Json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to {Action} flow subscription {SubscriptionId}",
                    enabled ? "enable" : "disable", subscriptionId);
                return null;
            }
        }

        public async Task<bool> DeleteAsync(string subscriptionId)
        {
            try
            {
                var response = await _httpClient.DeleteAsync(
                    $"{BaseRoute()}/{Uri.EscapeDataString(subscriptionId)}{AppQuery()}");
                if (!EnsureAuthorized(response)) return false;
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete flow subscription {SubscriptionId}", subscriptionId);
                return false;
            }
        }

        public async Task<AIFlowRunDetail?> GetLatestRunAsync(string subscriptionId)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"{BaseRoute()}/{Uri.EscapeDataString(subscriptionId)}/latest-run{AppQuery()}");
                if (!EnsureAuthorized(response)) return null;
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<AIFlowRunDetail>(Json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load latest run for subscription {SubscriptionId}", subscriptionId);
                return null;
            }
        }

        // ------------------------------------------------------------------

        private string BaseRoute() => $"{_apiBaseUrl}/ai/flows/subscriptions";

        private static StringContent JsonContent(object body) =>
            new(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

        private static async Task<string?> ExtractLimitMessageAsync(HttpResponseMessage response)
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                // Fall back to the friendly copy when the message is absent OR empty — matches
                // the JS (`body.message || fallback`) and Swift (`!message.isEmpty`) behavior so
                // all three stacks never surface a blank plan-limit message.
                if (doc.RootElement.TryGetProperty("message", out var message)
                    && message.GetString() is { Length: > 0 } text)
                    return text;
            }
            catch (JsonException) { /* non-JSON body — fall through */ }
            return "Your plan's favorites limit has been reached.";
        }

        private bool EnsureAuthorized(HttpResponseMessage response)
        {
            // Only 401 is an authentication failure. 403 is a permission/feature
            // denial (e.g. tier lacks FLOW_SUBSCRIPTIONS) — the token is valid, so
            // don't fire AuthenticationFailed (which prompts re-login).
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                if (!_authFailureFired)
                {
                    _authFailureFired = true;
                    AuthenticationFailed?.Invoke(this, EventArgs.Empty);
                }
                return false;
            }
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return false;
            return true;
        }

        private string AppQuery() =>
            string.IsNullOrEmpty(_appId) ? string.Empty : $"?requestedAppId={Uri.EscapeDataString(_appId)}";
    }
}
