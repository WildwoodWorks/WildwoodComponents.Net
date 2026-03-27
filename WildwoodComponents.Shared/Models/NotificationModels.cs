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
