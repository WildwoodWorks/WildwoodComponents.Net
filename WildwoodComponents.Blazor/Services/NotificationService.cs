using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Interface for Notification Service operations.
    /// </summary>
    public interface INotificationService
    {
        event EventHandler<ToastNotification>? OnNotificationAdded;
        event EventHandler<string>? OnNotificationRemoved;
        event EventHandler<ToastNotification>? OnNotificationUpdated;
        
        Task ShowAsync(string title, string message, NotificationType type = NotificationType.Info);
        Task ShowAsync(ToastNotification notification);
        Task DismissAsync(string notificationId);
        Task DismissAllAsync();
        Task UpdateAsync(string notificationId, ToastNotification notification);
    }

    /// <summary>
    /// Notification Service implementation for toast notifications.
    /// </summary>
    public class NotificationService : INotificationService
    {
        public event EventHandler<ToastNotification>? OnNotificationAdded;
        public event EventHandler<string>? OnNotificationRemoved;
        public event EventHandler<ToastNotification>? OnNotificationUpdated;

        public async Task ShowAsync(string title, string message, NotificationType type = NotificationType.Info)
        {
            var notification = new ToastNotification
            {
                Title = title,
                Message = message,
                Type = type,
                Timestamp = DateTime.Now,
                IsVisible = true,
                IsDismissible = true
            };

            await ShowAsync(notification);
        }

        public async Task ShowAsync(ToastNotification notification)
        {
            OnNotificationAdded?.Invoke(this, notification);
            await Task.CompletedTask;
        }

        public async Task DismissAsync(string notificationId)
        {
            OnNotificationRemoved?.Invoke(this, notificationId);
            await Task.CompletedTask;
        }

        public async Task DismissAllAsync()
        {
            // Implementation for dismissing all notifications
            await Task.CompletedTask;
        }

        public async Task UpdateAsync(string notificationId, ToastNotification notification)
        {
            OnNotificationUpdated?.Invoke(this, notification);
            await Task.CompletedTask;
        }
    }
}
