using Microsoft.AspNetCore.Mvc;
using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Components.Notification;

/// <summary>
/// ViewComponent that renders a toast notification stack for transient user feedback.
/// Supports slide-in animations, auto-dismiss, action buttons, and position-based layout.
/// Razor Pages equivalent of WildwoodComponents.Blazor NotificationToast.
/// </summary>
public class NotificationToastViewComponent : ViewComponent
{
    /// <summary>
    /// Renders the notification toast component
    /// </summary>
    /// <param name="position">Position on screen: TopRight, TopLeft, BottomRight, BottomLeft, TopCenter, BottomCenter (default: TopRight)</param>
    /// <param name="defaultDuration">Default auto-dismiss duration in ms (default: 5000)</param>
    /// <param name="maxVisible">Maximum number of visible toasts (default: 5)</param>
    /// <param name="showDismissAll">Whether to show a "Dismiss All" button when multiple toasts are visible (default: true)</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string position = "TopRight",
        int defaultDuration = 5000,
        int maxVisible = 5,
        bool showDismissAll = true)
    {
        var model = new NotificationToastViewModel
        {
            Position = position,
            DefaultDuration = defaultDuration,
            MaxVisible = maxVisible,
            ShowDismissAll = showDismissAll
        };
        return await Task.FromResult<IViewComponentResult>(View(model));
    }
}
