namespace WildwoodComponents.Shared.Models;

// ──────────────────────────────────────────────
// Notification shared DTOs
// Used by both WildwoodComponents.Blazor and WildwoodComponents.Razor
// ──────────────────────────────────────────────

public class ToastNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; } = NotificationType.Info;
    public DateTime? Timestamp { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsDismissible { get; set; } = true;
    public int? Duration { get; set; }
    public string? CssClass { get; set; }
    public List<NotificationAction>? Actions { get; set; }
}

public class NotificationAction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Text { get; set; } = string.Empty;
    public NotificationActionStyle Style { get; set; } = NotificationActionStyle.Primary;
    public bool DismissOnClick { get; set; } = true;
    public Dictionary<string, object>? Data { get; set; }
}

public class NotificationActionArgs
{
    public string NotificationId { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public string ActionText { get; set; } = string.Empty;
    public Dictionary<string, object>? Data { get; set; }
}

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error
}

public enum NotificationActionStyle
{
    Primary,
    Secondary,
    Success,
    Danger,
    Warning,
    Info,
    Light,
    Dark
}

public enum NotificationPosition
{
    TopLeft,
    TopRight,
    TopCenter,
    BottomLeft,
    BottomRight,
    BottomCenter
}

// ──────────────────────────────────────────────
// Notification Inbox DTOs (backend-connected inbox + delivery preferences)
// Distinct from ToastNotification above (the transient in-memory toast queue).
// Mirrors @wildwood/core notifications/inboxTypes.ts and Swift WildwoodCore InboxTypes.
// ──────────────────────────────────────────────

/// <summary>
/// Status values for a persisted inbox notification. Kept as string constants (not an enum)
/// to match the API payload verbatim and mirror the JS union 'Unread' | 'Read' | 'Dismissed'.
/// </summary>
public static class AppNotificationStatus
{
    public const string Unread = "Unread";
    public const string Read = "Read";
    public const string Dismissed = "Dismissed";
}

/// <summary>A persisted in-app notification (inbox item) returned by api/notifications.</summary>
public class AppNotification
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Message { get; set; } = string.Empty;
    /// <summary>Optional deep-link navigated to on click.</summary>
    public string? Link { get; set; }
    public string? AppId { get; set; }
    public string? EventType { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Status { get; set; } = AppNotificationStatus.Unread;
    public DateTime CreatedAt { get; set; }

    /// <summary>True when this notification has not yet been read.</summary>
    public bool IsUnread => string.Equals(Status, AppNotificationStatus.Unread, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Per-app delivery-channel preferences (opt-outs) for the authenticated user.
/// </summary>
public class UserNotificationPreference
{
    public string AppId { get; set; } = string.Empty;
    public bool EmailEnabled { get; set; } = true;
    public bool SmsEnabled { get; set; }
    public bool PushEnabled { get; set; }
    /// <summary>Browser (Web Notifications API) channel; opt-in, default false.</summary>
    public bool BrowserEnabled { get; set; }
    /// <summary>Opaque server-owned JSON blob of per-event opt-outs.</summary>
    public string? EventOptOutsJson { get; set; }

    /// <summary>
    /// Safe defaults (email on, all else off), returned when the API denies access (401/403)
    /// so callers get a usable object rather than null/stale values.
    /// </summary>
    public static UserNotificationPreference CreateDefault(string appId) => new()
    {
        AppId = appId,
        EmailEnabled = true,
        SmsEnabled = false,
        PushEnabled = false,
        BrowserEnabled = false,
        EventOptOutsJson = null,
    };
}

/// <summary>Response shape for the mark-all-read endpoint.</summary>
public class MarkAllReadResponse
{
    public int MarkedAsRead { get; set; }
}
