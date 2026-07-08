using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.Notifications;

/// <summary>
/// Standalone full inbox list (no bell / dropdown). Loads once on init and offers a
/// manual refresh; item rendering matches <see cref="NotificationsBell"/>.
/// Mirrors the @wildwood/react NotificationList.
/// </summary>
public partial class NotificationList : BaseWildwoodComponent
{
    [Inject] private INotificationInboxService InboxService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    /// <summary>App the list is scoped to (falls back to service defaults when empty).</summary>
    [Parameter] public string AppId { get; set; } = string.Empty;

    /// <summary>Whether the header shows the "Mark all read" action.</summary>
    [Parameter] public bool ShowMarkAllRead { get; set; } = true;

    /// <summary>Text shown when the inbox is empty.</summary>
    [Parameter] public string EmptyText { get; set; } = "No notifications";

    /// <summary>Raised when an item is activated; when unset the item's Link is navigated to.</summary>
    [Parameter] public EventCallback<AppNotification> OnNavigate { get; set; }

    private List<AppNotification> _notifications = new();
    private int _unreadCount;

    protected override async Task OnComponentInitializedAsync()
    {
        var token = LocalStorage != null
            ? await LocalStorage.GetItemAsync<string>(WildwoodStorageKeys.AccessToken)
            : null;
        InboxService.SetAuthToken(token);
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        // Retain-on-null semantics: a null return is a transient failure, so keep the last-good values.
        var list = await InboxService.GetNotificationsAsync();
        if (list != null)
        {
            _notifications = list;
        }

        var count = await InboxService.GetUnreadCountAsync();
        if (count.HasValue)
        {
            _unreadCount = count.Value;
        }

        StateHasChanged();
    }

    private async Task OnItemClick(AppNotification n)
    {
        if (n.IsUnread)
        {
            var ok = await InboxService.MarkReadAsync(n.Id);
            if (ok)
            {
                n.Status = AppNotificationStatus.Read;
                if (_unreadCount > 0) _unreadCount--;
            }
        }

        await NavigateAsync(n);
    }

    private async Task NavigateAsync(AppNotification n)
    {
        if (OnNavigate.HasDelegate)
        {
            await OnNavigate.InvokeAsync(n);
        }
        else if (!string.IsNullOrEmpty(n.Link))
        {
            Navigation.NavigateTo(n.Link);
        }
    }

    private async Task MarkAllRead()
    {
        // Reconcile with the server rather than optimistically clobbering local state: a transient
        // failure returns 0 (indistinguishable from "nothing to mark"), so re-fetching keeps the
        // list/badge truthful instead of showing everything read when nothing persisted.
        await InboxService.MarkAllReadAsync();
        await RefreshAsync();
    }

    private async Task Remove(AppNotification n)
    {
        var wasUnread = n.IsUnread;
        var ok = await InboxService.RemoveAsync(n.Id);
        if (ok)
        {
            _notifications.Remove(n);
            if (wasUnread && _unreadCount > 0) _unreadCount--;
        }
    }

    /// <summary>Relative "time ago" label matching the JS/Swift formatting rules.</summary>
    private static string TimeAgo(DateTime createdAt)
    {
        var created = createdAt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(createdAt, DateTimeKind.Utc)
            : createdAt.ToUniversalTime();

        var seconds = (DateTime.UtcNow - created).TotalSeconds;
        if (seconds < 0) seconds = 0;
        if (seconds < 60) return "just now";

        var minutes = seconds / 60;
        if (minutes < 60) return $"{(int)minutes}m ago";

        var hours = minutes / 60;
        if (hours < 24) return $"{(int)hours}h ago";

        var days = hours / 24;
        return $"{(int)days}d ago";
    }
}
