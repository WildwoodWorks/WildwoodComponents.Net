using Microsoft.AspNetCore.Mvc;
using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Components.NotificationBell;

/// <summary>
/// ViewComponent that renders a notification bell: a bell icon with an unread-count badge that
/// opens a dropdown panel of recent inbox notifications. Server-renders only the shell (with a
/// <c>data-proxy-url</c> and app id); the client JS (notification-inbox.js) hydrates the count
/// and list from the same-origin proxy and keeps the badge and panel in sync (polled ~45s).
/// Razor Pages equivalent of the React <c>NotificationsBell</c>.
/// </summary>
public class NotificationBellViewComponent : ViewComponent
{
    /// <summary>Renders the notification bell shell.</summary>
    /// <param name="appId">Optional app scope (used for browser-notification gating; list/count are user-scoped via JWT).</param>
    /// <param name="proxyBaseUrl">Base URL for the notification proxy endpoints (default: /api/wildwood-notifications).</param>
    /// <param name="maxBadgeCount">Cap the displayed badge count; higher values render as "N+" (default: 99).</param>
    /// <param name="emptyText">Text shown when the panel has no notifications.</param>
    /// <param name="pollIntervalMs">Poll interval in ms for a full list + count refresh; 0 disables polling (default: 45000).</param>
    /// <param name="browserNotifications">Raise a native browser notification for newly-arrived unread items (default: false).</param>
    public Task<IViewComponentResult> InvokeAsync(
        string? appId = null,
        string proxyBaseUrl = "/api/wildwood-notifications",
        int maxBadgeCount = 99,
        string emptyText = "No notifications",
        int pollIntervalMs = 45000,
        bool browserNotifications = false)
    {
        var model = new NotificationBellViewModel
        {
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            MaxBadgeCount = maxBadgeCount,
            EmptyText = emptyText,
            PollIntervalMs = pollIntervalMs,
            BrowserNotifications = browserNotifications
        };
        return Task.FromResult<IViewComponentResult>(View(model));
    }
}
