using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Server-side client for the WildwoodAPI notification inbox surface
/// (<c>api/notifications</c>): the persisted per-user inbox plus per-app delivery
/// preferences. The Razor Pages sibling of the Blazor <c>INotificationInboxService</c>
/// and the JS <c>NotificationInboxService</c>.
///
/// Transient-vs-deny semantics mirror the JS core service and the Blazor interface:
/// a <c>null</c> (or <c>0</c>) result means a TRANSIENT failure (5xx / network) so the
/// caller can retain its last-good data; an empty list / <c>0</c> / safe-default
/// preference means a legitimate auth deny (401/403).
/// </summary>
public interface IWildwoodNotificationInboxService
{
    /// <summary>
    /// True when the most recent read call (list/count/preferences) received a 401 from the API —
    /// the server session token is no longer valid. The proxy reads this to signal the browser so a
    /// background poll surfaces session expiry. Valid only immediately after a read call (the service
    /// is per-request scoped).
    /// </summary>
    bool LastResponseUnauthorized { get; }

    /// <summary>
    /// All inbox notifications for the authenticated user.
    /// <c>null</c> = transient failure (retain last-good); empty list = auth deny.
    /// </summary>
    Task<List<AppNotification>?> GetNotificationsAsync();

    /// <summary>
    /// Count of unread notifications.
    /// <c>null</c> = transient failure (retain last-good); <c>0</c> = auth deny.
    /// </summary>
    Task<int?> GetUnreadCountAsync();

    /// <summary>Marks a single notification read. Returns whether the server acknowledged.</summary>
    Task<bool> MarkReadAsync(string id);

    /// <summary>Marks every unread notification read. Returns how many were marked (0 on failure).</summary>
    Task<int> MarkAllReadAsync();

    /// <summary>Deletes (dismisses) a notification. Returns whether the server acknowledged.</summary>
    Task<bool> RemoveAsync(string id);

    /// <summary>
    /// The user's delivery preferences for an app.
    /// <c>null</c> = transient failure (retain prior prefs); safe defaults on auth deny.
    /// </summary>
    Task<UserNotificationPreference?> GetPreferencesAsync(string appId);

    /// <summary>Persists delivery preferences. Returns the saved record, or <c>null</c> on failure.</summary>
    Task<UserNotificationPreference?> UpdatePreferencesAsync(UserNotificationPreference pref);
}
