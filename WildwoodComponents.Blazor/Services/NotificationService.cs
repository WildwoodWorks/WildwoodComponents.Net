using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Shared.Models;

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

        void Show(string title, string message, NotificationType type = NotificationType.Info);
        void Show(ToastNotification notification);
        void Dismiss(string notificationId);
        void DismissAll();
        void Update(string notificationId, ToastNotification notification);
    }

    /// <summary>
    /// Notification Service implementation for toast notifications.
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly List<ToastNotification> _notifications = new();

        public event EventHandler<ToastNotification>? OnNotificationAdded;
        public event EventHandler<string>? OnNotificationRemoved;
        public event EventHandler<ToastNotification>? OnNotificationUpdated;

        public void Show(string title, string message, NotificationType type = NotificationType.Info)
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

            Show(notification);
        }

        public void Show(ToastNotification notification)
        {
            _notifications.Add(notification);
            OnNotificationAdded?.Invoke(this, notification);
        }

        public void Dismiss(string notificationId)
        {
            _notifications.RemoveAll(n => n.Id == notificationId);
            OnNotificationRemoved?.Invoke(this, notificationId);
        }

        public void DismissAll()
        {
            var ids = _notifications.Select(n => n.Id).ToList();
            _notifications.Clear();
            foreach (var id in ids)
            {
                OnNotificationRemoved?.Invoke(this, id);
            }
        }

        public void Update(string notificationId, ToastNotification notification)
        {
            var index = _notifications.FindIndex(n => n.Id == notificationId);
            if (index >= 0)
            {
                _notifications[index] = notification;
            }
            OnNotificationUpdated?.Invoke(this, notification);
        }
    }
}
