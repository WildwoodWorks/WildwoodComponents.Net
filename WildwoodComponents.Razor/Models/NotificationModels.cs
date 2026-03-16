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

public class ToastNotificationDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string? Title { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "Info";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsDismissible { get; set; } = true;
    public int? Duration { get; set; }
    public string? CssClass { get; set; }
    public List<NotificationActionDto>? Actions { get; set; }
}

public class NotificationActionDto
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Style { get; set; } = "Primary";
    public bool DismissOnClick { get; set; } = true;
    public Dictionary<string, object>? Data { get; set; }
}
