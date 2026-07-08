using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Client for the backend-connected notification inbox (bell + list + preferences).
    /// Distinct from <see cref="INotificationService"/>, which is the transient in-memory toast queue.
    /// Mirrors @wildwood/core NotificationInboxService.
    /// </summary>
    public interface INotificationInboxService
    {
        /// <summary>Set the bearer token used for authenticated inbox requests.</summary>
        void SetAuthToken(string? token);

        /// <summary>
        /// List the current user's inbox notifications. Returns an empty list on an auth deny
        /// (401/403) and <c>null</c> on a transient failure (5xx/network) so callers retain the last-good list.
        /// </summary>
        Task<List<AppNotification>?> GetNotificationsAsync();

        /// <summary>
        /// Get the unread count. Returns 0 on an auth deny and <c>null</c> on a transient failure
        /// so callers keep the last known badge count.
        /// </summary>
        Task<int?> GetUnreadCountAsync();

        /// <summary>Mark a single notification read. Returns whether the request succeeded.</summary>
        Task<bool> MarkReadAsync(string id);

        /// <summary>Mark all notifications read. Returns the number marked (0 on failure).</summary>
        Task<int> MarkAllReadAsync();

        /// <summary>Delete/dismiss a single notification. Returns whether the request succeeded.</summary>
        Task<bool> RemoveAsync(string id);

        /// <summary>
        /// Get per-app delivery preferences. Returns safe defaults on an auth deny and <c>null</c>
        /// on a transient failure so callers retain previously-loaded preferences.
        /// </summary>
        Task<UserNotificationPreference?> GetPreferencesAsync(string appId);

        /// <summary>Persist delivery preferences. Returns the saved record, or <c>null</c> on failure.</summary>
        Task<UserNotificationPreference?> UpdatePreferencesAsync(UserNotificationPreference pref);
    }
}
