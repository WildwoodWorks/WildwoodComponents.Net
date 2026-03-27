using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Blazor;

public class NotificationServiceTests
{
    [Fact]
    public void Show_FiresOnNotificationAdded()
    {
        var service = new NotificationService();
        ToastNotification? received = null;
        service.OnNotificationAdded += (_, n) => received = n;

        service.Show("Title", "Message", NotificationType.Success);

        Assert.NotNull(received);
        Assert.Equal("Title", received.Title);
        Assert.Equal("Message", received.Message);
        Assert.Equal(NotificationType.Success, received.Type);
    }

    [Fact]
    public void Dismiss_FiresOnNotificationRemoved()
    {
        var service = new NotificationService();
        var notification = new ToastNotification { Title = "Test", Message = "Msg" };
        service.Show(notification);

        string? removedId = null;
        service.OnNotificationRemoved += (_, id) => removedId = id;

        service.Dismiss(notification.Id);

        Assert.Equal(notification.Id, removedId);
    }

    [Fact]
    public void DismissAll_FiresOnNotificationRemovedForEach()
    {
        var service = new NotificationService();
        var n1 = new ToastNotification { Title = "A", Message = "1" };
        var n2 = new ToastNotification { Title = "B", Message = "2" };
        service.Show(n1);
        service.Show(n2);

        var removedIds = new List<string>();
        service.OnNotificationRemoved += (_, id) => removedIds.Add(id);

        service.DismissAll();

        Assert.Equal(2, removedIds.Count);
        Assert.Contains(n1.Id, removedIds);
        Assert.Contains(n2.Id, removedIds);
    }

    [Fact]
    public void Update_FiresOnNotificationUpdated()
    {
        var service = new NotificationService();
        var original = new ToastNotification { Title = "Old", Message = "Old msg" };
        service.Show(original);

        ToastNotification? updated = null;
        service.OnNotificationUpdated += (_, n) => updated = n;

        var replacement = new ToastNotification { Title = "New", Message = "New msg" };
        service.Update(original.Id, replacement);

        Assert.NotNull(updated);
        Assert.Equal("New", updated.Title);
    }
}
