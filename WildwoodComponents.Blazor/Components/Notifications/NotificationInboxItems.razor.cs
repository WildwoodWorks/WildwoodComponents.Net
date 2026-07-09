using Microsoft.AspNetCore.Components;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Components.Notifications;

/// <summary>
/// Presentational inbox list (header + items + empty state) shared by <c>NotificationsBell</c> and
/// <c>NotificationList</c> so the item markup and the relative-time formatting live in one place.
/// Holds no data or polling of its own — the parent owns the inbox state and wires the callbacks.
/// </summary>
public partial class NotificationInboxItems
{
    /// <summary>The notifications to render.</summary>
    [Parameter, EditorRequired] public List<AppNotification> Notifications { get; set; } = new();

    /// <summary>Unread count (drives the "Mark all read" disabled state).</summary>
    [Parameter] public int UnreadCount { get; set; }

    /// <summary>Text shown when the list is empty.</summary>
    [Parameter] public string EmptyText { get; set; } = "No notifications";

    /// <summary>Whether to show the "Mark all read" button.</summary>
    [Parameter] public bool ShowMarkAllRead { get; set; } = true;

    /// <summary>Whether to show the "Refresh" button (used by the standalone list, not the bell).</summary>
    [Parameter] public bool ShowRefresh { get; set; }

    /// <summary>Invoked when a notification's body is clicked.</summary>
    [Parameter] public EventCallback<AppNotification> OnItemClick { get; set; }

    /// <summary>Invoked when a notification's dismiss (×) button is clicked.</summary>
    [Parameter] public EventCallback<AppNotification> OnRemove { get; set; }

    /// <summary>Invoked when "Mark all read" is clicked.</summary>
    [Parameter] public EventCallback OnMarkAllRead { get; set; }

    /// <summary>Invoked when "Refresh" is clicked (only when <see cref="ShowRefresh"/> is true).</summary>
    [Parameter] public EventCallback OnRefresh { get; set; }
}
