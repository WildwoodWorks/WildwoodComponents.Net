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

// ── The signup routes ───────────────────────────────────────────────────────────────────────
//
// The new signup view needs register, log in, link the plan's payment, subscribe and the
// disclaimer gate, and every one of those used to be a route the HOST wrote (/api/wildwood-auth,
// /api/wildwood-subscription) or an origin-relative hit on api/userregistration/* that only works
// when WildwoodAPI happens to sit behind the same origin. They are shipped now, so a consuming app
// writes none of them.
//
// Two rules shape these types:
//
//   * A RESULT never carries a token. Razor keeps the JWT in the server session; handing the
//     browser one would throw away the reason the session exists. The login route answers a user
//     id and whether disclaimers are pending, and nothing else.
//   * A REQUEST binds to a declared shape. Nothing the browser sends is forwarded unread, so an
//     extra property a page invents never reaches WildwoodAPI.

/// <summary>
/// Body of <c>POST /api/wildwood-regsub/register</c>. A non-empty <see cref="Token"/> takes the
/// token path (<c>userregistration/register-with-token</c>); otherwise the open one.
/// </summary>
public class SignupRegisterProxyRequest
{
    [JsonPropertyName("FirstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("LastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("Username")]
    public string? Username { get; set; }

    [JsonPropertyName("Email")]
    public string? Email { get; set; }

    [JsonPropertyName("Password")]
    public string? Password { get; set; }

    /// <summary>A registration token, when the visitor is redeeming one.</summary>
    [JsonPropertyName("Token")]
    public string? Token { get; set; }

    /// <summary>Open registration's pricing model, when the app configures a default one.</summary>
    [JsonPropertyName("PricingModelId")]
    public string? PricingModelId { get; set; }

    /// <summary>
    /// The campaign touches the browser's attribution engine captured, carried exactly as the
    /// existing Razor registration carries them so a signup through the new view is attributed the
    /// same way as one through the old one.
    /// </summary>
    [JsonPropertyName("Attribution")]
    public WildwoodComponents.Shared.Models.AttributionPayloadModel? Attribution { get; set; }
}

/// <summary>What the register route answers. No token, no provider keys, no granted-app list.</summary>
public class SignupRegisterResultModel
{
    public bool Success { get; set; }

    public string? UserId { get; set; }

    /// <summary>The server's own words when it refused, so the page can show them verbatim.</summary>
    public string? Message { get; set; }

    /// <summary>
    /// <c>registration_refused</c> when the server said no. Absent on success.
    /// </summary>
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Body of <c>POST /api/wildwood-regsub/login</c> — the sign-in that follows a registration.
/// </summary>
public class SignupLoginProxyRequest
{
    [JsonPropertyName("Username")]
    public string? Username { get; set; }

    [JsonPropertyName("Email")]
    public string? Email { get; set; }

    [JsonPropertyName("Password")]
    public string? Password { get; set; }
}

/// <summary>
/// What the login route answers once the SERVER session holds the tokens. Deliberately thin: the
/// JWT and the refresh token stay on the server.
/// </summary>
public class SignupLoginResultModel
{
    public bool Success { get; set; }

    public string? UserId { get; set; }

    /// <summary>The account has disclaimers to accept before the signup is finished.</summary>
    public bool RequiresDisclaimerAcceptance { get; set; }

    public string? Message { get; set; }

    /// <summary><c>login_failed</c> when the sign-in did not produce a session.</summary>
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Body of <c>POST /api/wildwood-regsub/link-transaction</c> — attaching the plan's payment to the
/// account that now exists. Pay-first means the transaction belongs to nobody until this runs.
/// </summary>
public class SignupLinkTransactionProxyRequest
{
    /// <summary>The PROVIDER's id for the payment, which is what the server looks it up by.</summary>
    [JsonPropertyName("ExternalTransactionId")]
    public string ExternalTransactionId { get; set; } = string.Empty;

    [JsonPropertyName("UserId")]
    public string UserId { get; set; } = string.Empty;
}

/// <summary>Whether the link was made. Never fatal to a signup; the money is already taken.</summary>
public class SignupLinkTransactionResultModel
{
    public bool Success { get; set; }
}

/// <summary>
/// Body of <c>POST /api/wildwood-regsub/subscribe</c> — the self-service plan start a signup makes
/// once the account exists and its card has been linked.
/// </summary>
public class SignupSubscribeProxyRequest
{
    [JsonPropertyName("TierId")]
    public string TierId { get; set; } = string.Empty;

    [JsonPropertyName("PricingId")]
    public string? PricingId { get; set; }

    [JsonPropertyName("PaymentTransactionId")]
    public string? PaymentTransactionId { get; set; }
}

/// <summary>
/// Body of <c>POST /api/wildwood-regsub/disclaimer-gate/accept</c>.
/// </summary>
public class SignupDisclaimerAcceptProxyRequest
{
    [JsonPropertyName("Acceptances")]
    public List<WildwoodComponents.Shared.Models.DisclaimerAcceptanceResult> Acceptances { get; set; } = new();
}

/// <summary>
/// Body of <c>POST /api/wildwood-regsub/payment/confirm</c> — the shape
/// <c>wwwroot/js/payment.js</c> posts to <c>{proxyBase}/confirm</c>, so the shipped proxy can
/// stand in for a host-written payment proxy without the script changing.
/// </summary>
public class SignupConfirmPaymentProxyRequest
{
    [JsonPropertyName("paymentIntentId")]
    public string? PaymentIntentId { get; set; }

    /// <summary>
    /// The numeric <c>PaymentProviderType</c> the payment component picked, sent as a number
    /// exactly as the script sends it.
    /// </summary>
    [JsonPropertyName("providerType")]
    public int ProviderType { get; set; }
}

/// <summary>
/// What <c>GET /api/wildwood-regsub/registration-mode</c> answers: the two live flags, and the
/// mode they resolve to, so a browser never re-implements the resolution table.
/// </summary>
public class SignupRegistrationModeModel
{
    /// <summary>No registration path is open.</summary>
    public bool Closed { get; set; }

    /// <summary>A registration token must be supplied.</summary>
    public bool RequireToken { get; set; }

    /// <summary>Anyone may sign up without a token.</summary>
    public bool AllowOpenRegistration { get; set; }

    /// <summary>Offer the optional "Have a registration token?" entry.</summary>
    public bool ShowOptionalTokenEntry { get; set; }

    /// <summary>
    /// <c>Config</c>, <c>Fallback</c> (the settings could not be read) or <c>TokenMode</c> (the
    /// caller is redeeming an invite, which overrides the settings).
    /// </summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// The body of <c>POST /api/wildwood-regsub/tier-change/preview</c>: which plan the caller is
/// asking to be priced on.
/// </summary>
/// <remarks>
/// The PascalCase names WildwoodAPI itself binds, so the browser posts one shape whether the
/// preview goes through this proxy or the host's app-tier one.
/// </remarks>
public class TierChangePreviewProxyRequest
{
    /// <summary>The plan being moved to.</summary>
    public string? NewAppTierId { get; set; }

    /// <summary>Its pricing option, when the plan sells more than one.</summary>
    public string? NewAppTierPricingId { get; set; }
}
