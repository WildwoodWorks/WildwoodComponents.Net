using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// HTTP-backed client for the persisted notification inbox and per-app delivery preferences.
    /// Mirrors @wildwood/core NotificationInboxService: a transient failure (5xx/network) returns
    /// <c>null</c> so callers retain their last-good data, while a genuine 401/403 deny returns safe
    /// empties/defaults. Distinct from <see cref="NotificationService"/> (the transient toast queue).
    /// All endpoints are rooted at <c>{BaseAddress}/api/notifications</c>.
    /// </summary>
    public class NotificationInboxService : INotificationInboxService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<NotificationInboxService> _logger;
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public NotificationInboxService(HttpClient httpClient, ILogger<NotificationInboxService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public void SetAuthToken(string? token)
        {
            if (!string.IsNullOrEmpty(token))
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // A 401 (session expired) or 403 (tier/feature denial) is a genuine deny, distinct from a
        // transient 5xx/network failure. Callers get safe empties/defaults on deny, null on transient.
        private static bool IsAuthDeny(HttpStatusCode status) =>
            status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden;

        public async Task<List<AppNotification>?> GetNotificationsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/notifications");
                if (IsAuthDeny(response.StatusCode))
                    return new List<AppNotification>();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GetNotifications returned {StatusCode}", response.StatusCode);
                    return null; // transient — caller retains the last-good list
                }
                var result = await response.Content.ReadFromJsonAsync<List<AppNotification>>(JsonOptions);
                return result ?? new List<AppNotification>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetNotifications failed");
                return null;
            }
        }

        public async Task<int?> GetUnreadCountAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/notifications/count");
                if (IsAuthDeny(response.StatusCode))
                    return 0;
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GetUnreadCount returned {StatusCode}", response.StatusCode);
                    return null; // transient — caller keeps the last known count
                }
                return await response.Content.ReadFromJsonAsync<int>(JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetUnreadCount failed");
                return null;
            }
        }

        public async Task<bool> MarkReadAsync(string id)
        {
            try
            {
                var response = await _httpClient.PutAsync($"api/notifications/{Uri.EscapeDataString(id)}/read", null);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MarkRead failed for {Id}", id);
                return false;
            }
        }

        public async Task<int> MarkAllReadAsync()
        {
            try
            {
                var response = await _httpClient.PutAsync("api/notifications/read-all", null);
                if (!response.IsSuccessStatusCode)
                    return 0;
                var result = await response.Content.ReadFromJsonAsync<MarkAllReadResponse>(JsonOptions);
                return result?.MarkedAsRead ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MarkAllRead failed");
                return 0;
            }
        }

        public async Task<bool> RemoveAsync(string id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"api/notifications/{Uri.EscapeDataString(id)}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Remove failed for {Id}", id);
                return false;
            }
        }

        public async Task<UserNotificationPreference?> GetPreferencesAsync(string appId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/notifications/preferences?appId={Uri.EscapeDataString(appId)}");
                if (IsAuthDeny(response.StatusCode))
                    return UserNotificationPreference.CreateDefault(appId);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GetPreferences returned {StatusCode} for app {AppId}", response.StatusCode, appId);
                    return null; // transient — caller retains previously-loaded preferences
                }
                var result = await response.Content.ReadFromJsonAsync<UserNotificationPreference>(JsonOptions);
                return result ?? UserNotificationPreference.CreateDefault(appId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetPreferences failed for app {AppId}", appId);
                return null;
            }
        }

        public async Task<UserNotificationPreference?> UpdatePreferencesAsync(UserNotificationPreference pref)
        {
            try
            {
                var response = await _httpClient.PutAsJsonAsync("api/notifications/preferences", pref);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("UpdatePreferences returned {StatusCode} for app {AppId}", response.StatusCode, pref.AppId);
                    return null;
                }
                var result = await response.Content.ReadFromJsonAsync<UserNotificationPreference>(JsonOptions);
                return result ?? pref;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UpdatePreferences failed for app {AppId}", pref.AppId);
                return null;
            }
        }
    }
}
