using System.Text.Json.Serialization;

namespace WildwoodComponents.Shared.Models;

// Port of the add-on checkout / tier-change surfaces in
// packages/wildwood-core/src/features/types.ts.
//
// Request models carry explicit PascalCase [JsonPropertyName] attributes so the body is
// byte-compatible with what the JS SDK posts whatever naming policy a service serialises with.
// Response models use plain PascalCase properties, read back through the camelCase-insensitive
// web JsonSerializerOptions the services already use.

#region Tier change options

/// <summary>
/// Options form of a self-service tier change. Property names mirror the JS
/// <c>SelfChangeTierOptions</c>; the JSON names are what WildwoodAPI's SelfChangeTierDto binds.
/// </summary>
public class SelfChangeTierOptions
{
    [JsonPropertyName("NewAppTierId")]
    public string NewTierId { get; set; } = string.Empty;

    [JsonPropertyName("NewAppTierPricingId")]
    public string? NewPricingId { get; set; }

    /// <summary>
    /// Apply now rather than at the end of the billing period. Defaults to true.
    /// </summary>
    [JsonPropertyName("Immediate")]
    public bool Immediate { get; set; } = true;

    /// <summary>
    /// A payment that has already been made, to pay for the new plan.
    /// </summary>
    [JsonPropertyName("PaymentTransactionId")]
    public string? PaymentTransactionId { get; set; }

    /// <summary>
    /// The caller can confirm a payment (it has the payment SDK loaded) and will call the
    /// tier-change completion endpoint afterwards. Left false, a change whose proration needs
    /// 3-D Secure is refused rather than parked.
    /// </summary>
    [JsonPropertyName("SupportsPaymentAction")]
    public bool SupportsPaymentAction { get; set; }
}

/// <summary>
/// Refusal reasons a tier change can report (the server's <c>TierChangeErrorCodes</c>). The wire
/// value is open-ended, so compare against these rather than switching exhaustively.
/// </summary>
public static class TierChangeErrorCodes
{
    public const string PendingChangeNotFound = "pending_change_not_found";
    public const string PendingChangeExpired = "pending_change_expired";
    public const string PendingChangePaymentFailed = "pending_change_payment_failed";
    public const string PendingChangeSuperseded = "pending_change_superseded";
    public const string TierChangeAlreadyInProgress = "tier_change_already_in_progress";
}

#endregion

#region Trial eligibility

/// <summary>
/// What <c>GET api/app-tiers/{appId}/trial-eligibility</c> answers.
/// </summary>
public class TrialEligibilityModel
{
    /// <summary>
    /// Whether this account may still start a free trial on a tier.
    /// </summary>
    public bool TierTrialEligible { get; set; } = true;

    /// <summary>
    /// The same answer per active add-on, keyed by add-on id. A missing key means "unknown".
    /// </summary>
    public Dictionary<string, bool> AddOns { get; set; } = new();
}

#endregion

#region Add-on checkout requests

/// <summary>
/// One pack in a checkout basket. Leave <see cref="PricingId"/> out to take the pack's default.
/// </summary>
public class AddOnCheckoutItemInput
{
    [JsonPropertyName("AddOnId")]
    public string AddOnId { get; set; } = string.Empty;

    [JsonPropertyName("PricingId")]
    public string? PricingId { get; set; }
}

/// <summary>
/// Body of <c>POST api/app-tier-addons/{appId}/checkout/quote</c>.
/// </summary>
public class AddOnCheckoutQuoteRequestModel
{
    [JsonPropertyName("Items")]
    public List<AddOnCheckoutItemInput> Items { get; set; } = new();
}

/// <summary>
/// Body of <c>POST api/app-tier-addons/{appId}/checkout/payment-method</c>.
/// </summary>
public class AddOnCheckoutPaymentMethodRequestModel
{
    [JsonPropertyName("ProviderId")]
    public string ProviderId { get; set; } = string.Empty;
}

/// <summary>
/// The purchase. The card comes from exactly one of <see cref="PaymentTransactionId"/> or
/// <see cref="UseSavedCard"/>.
/// </summary>
public class AddOnCheckoutRequestModel
{
    [JsonPropertyName("CheckoutId")]
    public string CheckoutId { get; set; } = string.Empty;

    [JsonPropertyName("ProviderId")]
    public string ProviderId { get; set; } = string.Empty;

    [JsonPropertyName("PaymentTransactionId")]
    public string? PaymentTransactionId { get; set; }

    [JsonPropertyName("UseSavedCard")]
    public bool UseSavedCard { get; set; }

    [JsonPropertyName("Items")]
    public List<AddOnCheckoutItemInput> Items { get; set; } = new();
}

/// <summary>
/// Body of <c>POST api/app-tier-addons/{appId}/checkout/complete</c>.
/// </summary>
public class AddOnCheckoutCompleteRequestModel
{
    [JsonPropertyName("PaymentTransactionId")]
    public string PaymentTransactionId { get; set; } = string.Empty;
}

#endregion

#region Add-on checkout responses

/// <summary>
/// One quoted pack. Every field is the server's own answer — never the client's ask.
/// </summary>
public class AddOnCheckoutQuoteLineModel
{
    public string AddOnId { get; set; } = string.Empty;
    public string PricingId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string BillingFrequency { get; set; } = string.Empty;
    public int TrialDays { get; set; }

    /// <summary>
    /// Whether THIS account may still use the pack's trial (one trial per account).
    /// </summary>
    public bool TrialEligible { get; set; }

    /// <summary>
    /// Zero when the pack starts a trial, else the full price.
    /// </summary>
    public decimal DueToday { get; set; }

    public DateTime? TrialEnd { get; set; }
}

/// <summary>
/// The card already on file, named the way a UI shows it. Never carries a payment method id.
/// </summary>
public class AddOnCheckoutSavedCardModel
{
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
}

/// <summary>
/// A priced basket. <see cref="CheckoutId"/> is echoed back on the purchase and is what makes it
/// idempotent, so a double-clicked buy button cannot buy the same pack twice.
/// </summary>
public class AddOnCheckoutQuoteModel
{
    public bool Success { get; set; }
    public string CheckoutId { get; set; } = string.Empty;

    /// <summary>
    /// The provider this quote's saved card and <see cref="RequiresPaymentMethod"/> are about;
    /// echo it back on the purchase.
    /// </summary>
    public string? ProviderId { get; set; }

    /// <summary>
    /// ISO currency of the quoted prices. Empty when the quote was refused before pricing anything.
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    public List<AddOnCheckoutQuoteLineModel> Lines { get; set; } = new();
    public decimal TotalDueToday { get; set; }

    /// <summary>
    /// True when there is no card to reuse and one has to be collected first.
    /// </summary>
    public bool RequiresPaymentMethod { get; set; }

    public AddOnCheckoutSavedCardModel? SavedCard { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// The card-collection intent: confirm <see cref="ClientSecret"/>, then hand
/// <see cref="PaymentTransactionId"/> to the checkout, which reads the saved card off it.
/// </summary>
public class AddOnCheckoutPaymentMethodModel
{
    public bool Success { get; set; }
    public string? ClientSecret { get; set; }
    public string? SetupIntentId { get; set; }
    public string? PaymentTransactionId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// What became of one pack: it is running (trialing/active), the customer still has to
/// authenticate the card (requires_action, with nothing subscribed yet), or it failed and nothing
/// was charged.
/// </summary>
public static class AddOnCheckoutItemStatuses
{
    public const string Trialing = "trialing";
    public const string Active = "active";
    public const string RequiresAction = "requires_action";
    public const string Failed = "failed";
}

public class AddOnCheckoutItemResultModel
{
    public string AddOnId { get; set; } = string.Empty;
    public string PricingId { get; set; } = string.Empty;

    /// <summary>
    /// One of <see cref="AddOnCheckoutItemStatuses"/>.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// The add-on subscription row's id, once there is one.
    /// </summary>
    public string? SubscriptionId { get; set; }

    public DateTime? TrialEnd { get; set; }
    public decimal? AmountDueToday { get; set; }

    /// <summary>
    /// The 3-D Secure secret for a pack the bank wants authenticated. Secret — never log it.
    /// </summary>
    public string? ClientSecret { get; set; }

    public string? PaymentIntentId { get; set; }
    public string? PaymentTransactionId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// One result per requested pack, or a refusal that stopped the whole basket.
/// </summary>
public class AddOnCheckoutResultModel
{
    public bool Success { get; set; }
    public string CheckoutId { get; set; } = string.Empty;
    public List<AddOnCheckoutItemResultModel> Results { get; set; } = new();

    /// <summary>
    /// Set when nothing was attempted — a bad basket, a foreign provider, no card.
    /// </summary>
    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Why a quote, a purchase or one pack was refused (the server's <c>AddOnCheckoutErrors</c>).
/// Callers may see other strings; compare against these rather than switching exhaustively.
/// </summary>
public static class AddOnCheckoutErrorCodes
{
    public const string NoItems = "NoItems";
    public const string DuplicateItem = "DuplicateItem";
    public const string AppNotFound = "AppNotFound";
    public const string AddOnNotFound = "AddOnNotFound";
    public const string AddOnNotAvailable = "AddOnNotAvailable";
    public const string PricingNotFound = "PricingNotFound";
    public const string AlreadySubscribed = "AlreadySubscribed";
    public const string BundledInTier = "BundledInTier";
    public const string TierTooLow = "TierTooLow";
    public const string DependencyMissing = "DependencyMissing";
    public const string ProviderNotAvailable = "ProviderNotAvailable";
    public const string PaymentMethodRequired = "PaymentMethodRequired";
    public const string TransactionNotFound = "TransactionNotFound";
    public const string TransactionNotYours = "TransactionNotYours";
    public const string TransactionProviderMismatch = "TransactionProviderMismatch";
    public const string PaymentNotVerified = "PaymentNotVerified";
    public const string CheckoutIdRequired = "CheckoutIdRequired";
    public const string ProcessorError = "ProcessorError";
    public const string SubscriptionNotCreated = "SubscriptionNotCreated";
}

#endregion

#region Add-on subscription lifecycle

/// <summary>
/// The <c>errorCode</c> values the pack lifecycle returns (the server's
/// <c>AddOnSubscriptionErrorCodes</c>).
/// </summary>
public static class AddOnSubscriptionErrorCodes
{
    public const string NotFound = "addon_subscription_not_found";
    public const string Forbidden = "addon_subscription_forbidden";
    public const string ProviderRefused = "addon_subscription_provider_refused";
    public const string NotPendingCancellation = "addon_subscription_not_pending_cancellation";
    public const string NotProviderBilled = "addon_subscription_not_provider_billed";
    public const string Error = "addon_subscription_error";
}

/// <summary>
/// Codes the SDK itself supplies when the server sent none.
/// </summary>
public static class AppTierActionErrorCodes
{
    /// <summary>The route is absent — a server older than this SDK.</summary>
    public const string NotSupported = "NotSupported";

    /// <summary>The request never got a structured answer.</summary>
    public const string RequestFailed = "RequestFailed";
}

/// <summary>
/// A refusal an add-on/tier action reports instead of throwing.
/// </summary>
public class AppTierActionError
{
    /// <summary>
    /// The server's own error code when it sent one; otherwise
    /// <see cref="AppTierActionErrorCodes.NotSupported"/> or
    /// <see cref="AppTierActionErrorCodes.RequestFailed"/>.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// HTTP status, when the failure got that far.
    /// </summary>
    public int? Status { get; set; }
}

/// <summary>
/// Result of subscribing to a single pack. The JS union
/// (<c>{success:true, subscription?} | {success:false, error}</c>) flattens to one class in the
/// idiom <see cref="AppTierCancelResultModel"/> already uses: on success <see cref="Error"/> is
/// null, on failure <see cref="Subscription"/> is.
/// </summary>
public class AddOnSubscribeResultModel
{
    public bool Success { get; set; }
    public UserAddOnSubscriptionModel? Subscription { get; set; }
    public AppTierActionError? Error { get; set; }
}

/// <summary>
/// Result of cancelling a pack. IsScheduled=true means access continues until EffectiveDate (the
/// end of the period already paid for); false means it ended now.
/// </summary>
public class AddOnSubscriptionCancelResultModel
{
    public bool Success { get; set; }
    public bool? IsScheduled { get; set; }

    /// <summary>The row's status after the call.</summary>
    public string? Status { get; set; }

    public DateTime? EffectiveDate { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result of clearing a scheduled pack cancellation.
/// </summary>
public class AddOnSubscriptionReactivateResultModel
{
    public bool Success { get; set; }
    public string? Status { get; set; }

    /// <summary>
    /// The refreshed subscription, so a client can render the restored status without a re-read.
    /// </summary>
    public UserAddOnSubscriptionModel? Subscription { get; set; }

    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

#endregion
