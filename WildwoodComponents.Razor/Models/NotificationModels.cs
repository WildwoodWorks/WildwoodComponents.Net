using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Models;

public class NotificationViewModel
{
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-notifications";
    public string Position { get; set; } = "TopRight";
    public int DefaultDuration { get; set; } = 5000;
    public int MaxVisible { get; set; } = 5;
    public bool ShowDismissAll { get; set; } = true;
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}

public class NotificationToastViewModel
{
    public string Position { get; set; } = "TopRight";
    public int DefaultDuration { get; set; } = 5000;
    public int MaxVisible { get; set; } = 5;
    public bool ShowDismissAll { get; set; } = true;
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}

// DTOs (ToastNotificationDto, NotificationActionDto) have been consolidated into
// WildwoodComponents.Shared.Models as ToastNotification, NotificationAction,
// along with NotificationType, NotificationActionStyle, NotificationPosition enums.
