using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.Notifications;

/// <summary>
/// Notification bell: an unread badge over a bell button that opens a dropdown panel
/// hosting the inbox list. Polls the unread count + list together on a fixed interval
/// and raises a native browser notification when a new unread item arrives.
/// Mirrors the @wildwood/react NotificationBell.
/// </summary>
public partial class NotificationsBell : BaseWildwoodComponent
{
    [Inject] private INotificationInboxService InboxService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    /// <summary>App the bell is scoped to (falls back to service defaults when empty).</summary>
    [Parameter] public string AppId { get; set; } = string.Empty;

    /// <summary>Highest number shown in the badge before it becomes "N+".</summary>
    [Parameter] public int MaxBadgeCount { get; set; } = 99;

    /// <summary>Text shown when the inbox is empty.</summary>
    [Parameter] public string EmptyText { get; set; } = "No notifications";

    /// <summary>Poll cadence in seconds for the count + list refresh.</summary>
    [Parameter] public int PollIntervalSeconds { get; set; } = 45;

    /// <summary>
    /// Opt in to raising a native browser (Web Notifications API) notification when a new unread
    /// item arrives. Off by default, mirroring the React <c>browserNotifications</c> option; it
    /// still only fires when the user has granted permission (e.g. via NotificationPreferences).
    /// </summary>
    [Parameter] public bool BrowserNotifications { get; set; }

    /// <summary>Raised when an item is activated; when unset the item's Link is navigated to.</summary>
    [Parameter] public EventCallback<AppNotification> OnNavigate { get; set; }

    private List<AppNotification> _notifications = new();
    private int _unreadCount;
    private bool _open;
    private Timer? _pollTimer;

    // Tracks ids already seen so we only surface a browser notification for genuinely new items.
    private readonly HashSet<string> _knownIds = new();
    private bool _primed;

    private string BadgeText => _unreadCount > MaxBadgeCount ? $"{MaxBadgeCount}+" : _unreadCount.ToString();

    protected override async Task OnComponentInitializedAsync()
    {
        var token = LocalStorage != null
            ? await LocalStorage.GetItemAsync<string>(WildwoodStorageKeys.AccessToken)
            : null;
        InboxService.SetAuthToken(token);
        await RefreshAsync();
    }

    protected override Task OnComponentFirstRenderAsync()
    {
        // Start polling only after the first interactive render so ticks never fire during prerender.
        var period = TimeSpan.FromSeconds(PollIntervalSeconds > 0 ? PollIntervalSeconds : 45);
        _pollTimer = new Timer(async _ => await PollTickAsync(), null, period, period);
        return Task.CompletedTask;
    }

    private async Task PollTickAsync()
    {
        try
        {
            await InvokeAsync(async () =>
            {
                await RefreshAsync();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // Component was disposed between the timer firing and the dispatch — safe to ignore.
        }
    }

    private async Task RefreshAsync()
    {
        // Retain-on-null semantics: a null return is a transient failure, so keep the last-good values.
        var list = await InboxService.GetNotificationsAsync();
        if (list != null)
        {
            _notifications = list;
            await NotifyNewItemsAsync(list);
        }

        var count = await InboxService.GetUnreadCountAsync();
        if (count.HasValue)
        {
            _unreadCount = count.Value;
        }
    }

    private async Task NotifyNewItemsAsync(List<AppNotification> list)
    {
        // Off by default (mirrors React's browserNotifications option): only raise native OS
        // notifications when the host explicitly opts in, so the bell never fires them unrequested.
        if (!BrowserNotifications) return;

        // Prime silently on the first successful load so existing items don't all fire notifications.
        if (!_primed)
        {
            foreach (var n in list)
            {
                _knownIds.Add(n.Id);
            }
            _primed = true;
            return;
        }

        foreach (var n in list)
        {
            if (n.IsUnread && !_knownIds.Contains(n.Id))
            {
                await InvokeJSVoidAsync("wildwoodBrowserNotifications.show", n.Title ?? "Notification", n.Message, n.Id);
            }
            _knownIds.Add(n.Id);
        }
    }

    private void Toggle() => _open = !_open;

    private void Close() => _open = false;

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

        _open = false;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
