using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Default <see cref="IWildwoodNotificationInboxService"/> implementation. Calls the WildwoodAPI
/// <c>api/notifications</c> endpoints using the named "WildwoodAPI" HttpClient. Follows the
/// <see cref="WildwoodDisclaimerService"/> construction/auth/error idiom: relative endpoint paths
/// resolve against the HttpClient's <c>{base}/api/</c> base address, and the Bearer token is applied
/// from the server-side session on every call. The token never reaches the browser.
///
/// Endpoints (rooted at <c>{base}/api/notifications</c>):
///   GET    notifications                    -> AppNotification[]
///   GET    notifications/count              -> number
///   PUT    notifications/{id}/read
///   PUT    notifications/read-all           -> { markedAsRead }
///   DELETE notifications/{id}
///   GET    notifications/preferences?appId= -> UserNotificationPreference
///   PUT    notifications/preferences        -> UserNotificationPreference
/// </summary>
public class WildwoodNotificationInboxService : IWildwoodNotificationInboxService
{
    private readonly HttpClient _httpClient;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodNotificationInboxService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public bool LastResponseUnauthorized { get; private set; }

    public WildwoodNotificationInboxService(
        HttpClient httpClient,
        IWildwoodSessionManager sessionManager,
        ILogger<WildwoodNotificationInboxService> logger)
    {
        _httpClient = httpClient;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// 401 (auth failed) / 403 (feature off) are legitimate denies — callers degrade gracefully
    /// (empty list / 0 / default prefs). Any other non-success is transient.
    /// </summary>
    private static bool IsAuthDeny(HttpStatusCode status) =>
        status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden;

    public async Task<List<AppNotification>?> GetNotificationsAsync()
    {
        LastResponseUnauthorized = false;
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync("notifications");
            LastResponseUnauthorized = response.StatusCode == HttpStatusCode.Unauthorized;

            if (IsAuthDeny(response.StatusCode))
                return new List<AppNotification>();          // auth deny — graceful empty
            if (!response.IsSuccessStatusCode)
                return null;                                  // transient — retain last-good

            var list = await response.Content.ReadFromJsonAsync<List<AppNotification>>(JsonOptions);
            return list ?? new List<AppNotification>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list notifications");
            return null;
        }
    }

    public async Task<int?> GetUnreadCountAsync()
    {
        LastResponseUnauthorized = false;
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync("notifications/count");
            LastResponseUnauthorized = response.StatusCode == HttpStatusCode.Unauthorized;

            if (IsAuthDeny(response.StatusCode))
                return 0;                                     // auth deny — graceful 0
            if (!response.IsSuccessStatusCode)
                return null;                                  // transient — retain last-good

            // The count endpoint returns a bare number; parse from the raw body so a text/plain
            // (vs application/json) content type doesn't trip ReadFromJsonAsync's media-type check.
            var text = (await response.Content.ReadAsStringAsync()).Trim();
            return int.TryParse(text, out var value) ? value : 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load unread notification count");
            return null;
        }
    }

    public async Task<bool> MarkReadAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PutAsync($"notifications/{Uri.EscapeDataString(id)}/read", content: null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark notification {Id} read", id);
            return false;
        }
    }

    public async Task<int> MarkAllReadAsync()
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PutAsync("notifications/read-all", content: null);
            if (!response.IsSuccessStatusCode)
                return 0;

            var body = await response.Content.ReadFromJsonAsync<MarkAllReadResponse>(JsonOptions);
            return body?.MarkedAsRead ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark all notifications read");
            return 0;
        }
    }

    public async Task<bool> RemoveAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.DeleteAsync($"notifications/{Uri.EscapeDataString(id)}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete notification {Id}", id);
            return false;
        }
    }

    public async Task<UserNotificationPreference?> GetPreferencesAsync(string appId)
    {
        LastResponseUnauthorized = false;
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var url = $"notifications/preferences?appId={Uri.EscapeDataString(appId ?? string.Empty)}";
            using var response = await _httpClient.GetAsync(url);
            LastResponseUnauthorized = response.StatusCode == HttpStatusCode.Unauthorized;

            if (IsAuthDeny(response.StatusCode))
                return UserNotificationPreference.CreateDefault(appId ?? string.Empty); // auth deny — safe defaults
            if (!response.IsSuccessStatusCode)
                return null;                                  // transient — retain prior prefs

            var pref = await response.Content.ReadFromJsonAsync<UserNotificationPreference>(JsonOptions);
            return pref ?? UserNotificationPreference.CreateDefault(appId ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load notification preferences for app {AppId}", appId);
            return null;
        }
    }

    public async Task<UserNotificationPreference?> UpdatePreferencesAsync(UserNotificationPreference pref)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PutAsJsonAsync("notifications/preferences", pref);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<UserNotificationPreference>(JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update notification preferences for app {AppId}", pref?.AppId);
            return null;
        }
    }
}
