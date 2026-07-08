using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Models;

// View models for the backend-connected notification inbox ViewComponents
// (NotificationBell, NotificationInbox, NotificationPreferences). Distinct from the
// transient toast NotificationViewModel/NotificationToastViewModel in NotificationModels.cs.
// The client JS (notification-inbox.js) hydrates the list/count against the same-origin
// proxy at ProxyBaseUrl (/api/wildwood-notifications by default).

/// <summary>View model for <c>NotificationBellViewComponent</c> — bell + badge + dropdown panel.</summary>
public class NotificationBellViewModel
{
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-notifications";
    /// <summary>App scope, forwarded to preference calls and browser-notification gating (optional for list/count, which are user-scoped via JWT).</summary>
    public string? AppId { get; set; }
    /// <summary>Cap the displayed badge count; higher values render as "N+". Default 99.</summary>
    public int MaxBadgeCount { get; set; } = 99;
    /// <summary>Text shown when the panel has no notifications.</summary>
    public string EmptyText { get; set; } = "No notifications";
    /// <summary>Poll interval (ms) for a full list + count refresh. 0 disables polling. Default 45000.</summary>
    public int PollIntervalMs { get; set; } = 45000;
    /// <summary>Raise a native browser notification for newly-arrived unread items (gated on permission).</summary>
    public bool BrowserNotifications { get; set; }
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}

/// <summary>View model for <c>NotificationInboxViewComponent</c> — standalone/full-page list.</summary>
public class NotificationInboxViewModel
{
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-notifications";
    public string? AppId { get; set; }
    public string EmptyText { get; set; } = "No notifications";
    public int PollIntervalMs { get; set; } = 45000;
    /// <summary>Show the header with the "Mark all read" action.</summary>
    public bool ShowMarkAllRead { get; set; } = true;
    public bool BrowserNotifications { get; set; }
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}

/// <summary>View model for <c>NotificationPreferencesViewComponent</c> — delivery-channel opt-outs.</summary>
public class NotificationPreferencesViewModel
{
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-notifications";
    public string AppId { get; set; } = string.Empty;
    /// <summary>Current preferences, pre-rendered server-side (safe defaults on transient/deny).</summary>
    public UserNotificationPreference Preferences { get; set; } = new();
    /// <summary>Show the push-notifications toggle.</summary>
    public bool ShowPush { get; set; } = true;
    /// <summary>Show the browser (Web Notifications API) toggle. Blazor/Razor have a browser, so default true.</summary>
    public bool ShowBrowser { get; set; } = true;
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}
