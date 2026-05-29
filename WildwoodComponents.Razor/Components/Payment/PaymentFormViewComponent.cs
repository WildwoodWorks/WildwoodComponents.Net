using Microsoft.AspNetCore.Mvc;
using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Components.Payment;

/// <summary>
/// ViewComponent that renders a standalone payment form (without provider selection).
/// Supports credit/debit card fields, optional billing address, and amount display.
/// Client-side JavaScript handles form validation, card formatting, and AJAX submission.
/// Razor Pages equivalent of WildwoodComponents.Blazor PaymentFormComponent.
/// </summary>
/// <remarks>
/// IMPORTANT: The form POSTs to <c>{proxyBaseUrl}/process</c> (default base
/// <c>/api/wildwood-payment</c>), which is NOT a built-in WildwoodAPI endpoint — the host
/// application must provide it (e.g. a server-side proxy that tokenizes the card). Until
/// then this is a UI-only demo. For end-to-end WildwoodAPI payments use
/// <c>PaymentViewComponent</c> (provider tokenization + persisted PaymentTransaction with
/// AppId). The <c>appId</c> is included in the submitted payload when supplied.
/// </remarks>
public class PaymentFormViewComponent : ViewComponent
{
    /// <summary>
    /// Renders the payment form component
    /// </summary>
    /// <param name="appId">Required. The application ID.</param>
    /// <param name="amount">Required. The payment amount.</param>
    /// <param name="proxyBaseUrl">Base URL for payment proxy endpoints (default: /api/wildwood-payment)</param>
    /// <param name="currency">Currency code (default: USD)</param>
    /// <param name="description">Payment description shown to user</param>
    /// <param name="requireBillingAddress">Whether billing address is required</param>
    /// <param name="merchantId">Optional merchant ID</param>
    /// <param name="orderId">Optional order ID to associate with payment</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string appId,
        decimal amount,
        string proxyBaseUrl = "/api/wildwood-payment",
        string currency = "USD",
        string? description = null,
        bool requireBillingAddress = false,
        string? merchantId = null,
        string? orderId = null)
    {
        var model = new PaymentFormViewModel
        {
            AppId = appId,
            Amount = amount,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            Currency = currency,
            Description = description,
            RequireBillingAddress = requireBillingAddress,
            MerchantId = merchantId,
            OrderId = orderId
        };

        return await Task.FromResult<IViewComponentResult>(View(model));
    }
}
