using System.Text.Json.Serialization;

namespace WildwoodComponents.Razor.Models;

// Request bodies the registration/subscription proxy binds where WildwoodComponents.Shared has no
// wire model of its own — the pack subscribe body and the token-detail lookup. Everything else the
// proxy accepts binds to the Shared request models (AddOnCheckoutQuoteRequestModel,
// AddOnCheckoutPaymentMethodRequestModel, AddOnCheckoutRequestModel,
// AddOnCheckoutCompleteRequestModel, SelfChangeTierOptions), so the browser never hands the proxy
// a shape it forwards unread.

/// <summary>
/// Body of <c>POST /api/wildwood-regsub/addons/subscribe</c>. The app id is NOT taken from here:
/// the proxy uses the app configured in <c>AddWildwoodComponentsRazor</c>.
/// </summary>
public class AddOnSubscribeProxyRequest
{
    [JsonPropertyName("AddOnId")]
    public string AddOnId { get; set; } = string.Empty;

    /// <summary>Leave out to take the pack's default pricing option.</summary>
    [JsonPropertyName("PricingId")]
    public string? PricingId { get; set; }

    /// <summary>A payment already made and verified, when the pack is not free.</summary>
    [JsonPropertyName("PaymentTransactionId")]
    public string? PaymentTransactionId { get; set; }
}

/// <summary>
/// Body of <c>POST /api/wildwood-regsub/token-details</c> — the POST form exists so a registration
/// token never has to travel in a URL (query strings land in access logs and referrers).
/// </summary>
public class RegistrationTokenDetailsProxyRequest
{
    [JsonPropertyName("Token")]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Optional app scope. It is only ever validated against the configured app id, never
    /// forwarded in its place.
    /// </summary>
    [JsonPropertyName("AppId")]
    public string? AppId { get; set; }
}
