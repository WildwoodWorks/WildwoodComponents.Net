using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Components.NotificationPreferences;

/// <summary>
/// ViewComponent that renders the user's notification delivery-channel preferences
/// (Email / SMS / Push / Browser opt-outs) for an app. The current preferences are pre-rendered
/// server-side via <see cref="IWildwoodNotificationInboxService"/>; the client JS
/// (notification-inbox.js) persists each toggle via a PUT to the same-origin proxy.
/// Razor Pages equivalent of the React <c>NotificationPreferences</c>.
/// </summary>
public class NotificationPreferencesViewComponent : ViewComponent
{
    private readonly IWildwoodNotificationInboxService _service;
    private readonly ILogger<NotificationPreferencesViewComponent> _logger;

    public NotificationPreferencesViewComponent(
        IWildwoodNotificationInboxService service,
        ILogger<NotificationPreferencesViewComponent> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>Renders the preferences form, pre-populated with the user's current preferences.</summary>
    /// <param name="appId">Required. The app whose delivery preferences are managed.</param>
    /// <param name="proxyBaseUrl">Base URL for the notification proxy endpoints (default: /api/wildwood-notifications).</param>
    /// <param name="showPush">Show the push-notifications toggle (default: true).</param>
    /// <param name="showBrowser">Show the browser (Web Notifications API) toggle (default: true).</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string appId,
        string proxyBaseUrl = "/api/wildwood-notifications",
        bool showPush = true,
        bool showBrowser = true)
    {
        // Pre-render the current prefs. A transient failure (null) falls back to safe defaults
        // for the initial render; the client can refine after hydration.
        UserNotificationPreference preferences;
        try
        {
            preferences = await _service.GetPreferencesAsync(appId)
                          ?? UserNotificationPreference.CreateDefault(appId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to pre-render notification preferences for app {AppId}", appId);
            preferences = UserNotificationPreference.CreateDefault(appId);
        }

        var model = new NotificationPreferencesViewModel
        {
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            Preferences = preferences,
            ShowPush = showPush,
            ShowBrowser = showBrowser
        };
        return View(model);
    }
}
