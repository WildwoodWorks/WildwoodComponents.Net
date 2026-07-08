using Microsoft.AspNetCore.Mvc;
using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Components.NotificationInbox;

/// <summary>
/// ViewComponent that renders a standalone / full-page notification inbox list (the same list the
/// bell shows in its dropdown, without the bell chrome). Server-renders the shell; the client JS
/// (notification-inbox.js) hydrates and polls the list from the same-origin proxy.
/// Razor Pages equivalent of the React <c>NotificationList</c>.
/// </summary>
public class NotificationInboxViewComponent : ViewComponent
{
    /// <summary>Renders the standalone inbox list shell.</summary>
    /// <param name="appId">Optional app scope (list is user-scoped via JWT).</param>
    /// <param name="proxyBaseUrl">Base URL for the notification proxy endpoints (default: /api/wildwood-notifications).</param>
    /// <param name="emptyText">Text shown when there are no notifications.</param>
    /// <param name="pollIntervalMs">Poll interval in ms for a full list + count refresh; 0 disables polling (default: 45000).</param>
    /// <param name="showMarkAllRead">Show the header with the "Mark all read" action (default: true).</param>
    /// <param name="browserNotifications">Raise a native browser notification for newly-arrived unread items (default: false).</param>
    public Task<IViewComponentResult> InvokeAsync(
        string? appId = null,
        string proxyBaseUrl = "/api/wildwood-notifications",
        string emptyText = "No notifications",
        int pollIntervalMs = 45000,
        bool showMarkAllRead = true,
        bool browserNotifications = false)
    {
        var model = new NotificationInboxViewModel
        {
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            EmptyText = emptyText,
            PollIntervalMs = pollIntervalMs,
            ShowMarkAllRead = showMarkAllRead,
            BrowserNotifications = browserNotifications
        };
        return Task.FromResult<IViewComponentResult>(View(model));
    }
}
