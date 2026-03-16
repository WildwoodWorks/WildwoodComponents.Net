using Microsoft.AspNetCore.Mvc;
using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Components.Notification;

/// <summary>
/// ViewComponent that renders a notification container for displaying server-pushed
/// or client-triggered notification items. Positioned based on the Position parameter.
/// Razor Pages equivalent of WildwoodComponents.Blazor NotificationComponent.
/// </summary>
public class NotificationViewComponent : ViewComponent
{
    /// <summary>
    /// Renders the notification component
    /// </summary>
    /// <param name="proxyBaseUrl">Base URL for the notification proxy endpoints (default: /api/wildwood-notifications)</param>
    /// <param name="position">Position on screen: TopRight, TopLeft, BottomRight, BottomLeft, TopCenter, BottomCenter (default: TopRight)</param>
    /// <param name="defaultDuration">Default auto-dismiss duration in ms (default: 5000)</param>
    /// <param name="maxVisible">Maximum number of visible notifications (default: 5)</param>
    /// <param name="showDismissAll">Whether to show a "Dismiss All" button (default: true)</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string proxyBaseUrl = "/api/wildwood-notifications",
        string position = "TopRight",
        int defaultDuration = 5000,
        int maxVisible = 5,
        bool showDismissAll = true)
    {
        var model = new NotificationViewModel
        {
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            Position = position,
            DefaultDuration = defaultDuration,
            MaxVisible = maxVisible,
            ShowDismissAll = showDismissAll
        };
        return await Task.FromResult<IViewComponentResult>(View(model));
    }
}
