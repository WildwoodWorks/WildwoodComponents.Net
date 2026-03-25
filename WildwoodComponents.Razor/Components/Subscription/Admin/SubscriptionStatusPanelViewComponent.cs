using Microsoft.AspNetCore.Mvc;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Components.Subscription.Admin;

/// <summary>
/// Displays current subscription status: tier name, status badge, dates, and cancel button.
/// Razor Pages equivalent of WildwoodComponents.Blazor SubscriptionStatusPanel.
/// </summary>
public class SubscriptionStatusPanelViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(
        string componentId,
        string appId,
        string proxyBaseUrl,
        bool isAdmin,
        UserTierSubscriptionModel? subscription)
    {
        var model = new SubscriptionStatusPanelViewModel
        {
            ComponentId = componentId,
            AppId = appId,
            ProxyBaseUrl = proxyBaseUrl,
            IsAdmin = isAdmin,
            Subscription = subscription
        };

        return View(model);
    }
}
