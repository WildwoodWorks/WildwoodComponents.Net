using System.Text.Json.Serialization;

namespace WildwoodComponents.Shared.Models;

// Note: These enums mirror the server-side enums in WildwoodAPI.Models
// They are duplicated here for client-side use without server dependency
// When used in admin pages, prefer the WildwoodAPI.Models versions

/// <summary>
/// Payment provider types matching the server-side enum (for client use)
/// </summary>
public enum PaymentProviderType
{
    // Traditional Payment Processors
    Stripe = 1,
    PayPal = 2,
    Square = 3,
    Braintree = 4,
    AuthorizeNet = 5,

    // App Store In-App Purchase
    AppleAppStore = 10,
    GooglePlayStore = 11,

    // Digital Wallets
    ApplePay = 20,
    GooglePay = 21,

    // Buy Now Pay Later
    Klarna = 30,
    Affirm = 31,
    Afterpay = 32,

    // Regional/International
    Razorpay = 40,
    Adyen = 41,

    // Cryptocurrency
    Coinbase = 50,
    BitPay = 51
}

/// <summary>
/// Legacy alias for backward compatibility
/// </summary>
public enum ClientPaymentProviderType
{
    // Traditional Payment Processors
    Stripe = 1,
    PayPal = 2,
    Square = 3,
    Braintree = 4,
    AuthorizeNet = 5,

    // App Store In-App Purchase
    AppleAppStore = 10,
    GooglePlayStore = 11,

    // Digital Wallets
    ApplePay = 20,
    GooglePay = 21,

    // Buy Now Pay Later
    Klarna = 30,
    Affirm = 31,
    Afterpay = 32,

    // Regional/International
    Razorpay = 40,
    Adyen = 41,

    // Cryptocurrency
    Coinbase = 50,
    BitPay = 51
}

/// <summary>
/// Category of payment provider for UI grouping (for client use)
/// </summary>
public enum ClientPaymentProviderCategory
{
    CardProcessor = 1,
    AppStore = 2,
    DigitalWallet = 3,
    BuyNowPayLater = 4,
    Regional = 5,
    Cryptocurrency = 6
}

/// <summary>
/// DTO for a payment provider available for an app
/// </summary>
public class PaymentProviderDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public int ProviderType { get; set; }
    public int Category { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsDefault { get; set; }
    public bool IsSandboxMode { get; set; }
    public int DisplayOrder { get; set; }

    // Public keys only (no secrets)
    public string? PublishableKey { get; set; }
    public string? ClientId { get; set; }
    public string? MerchantId { get; set; }

    // Capabilities
    public bool SupportsSubscriptions { get; set; }
    public bool SupportsRefunds { get; set; }
    public bool Supports3DSecure { get; set; }
    public bool SupportsSavedPaymentMethods { get; set; }

    /// <summary>
    /// Whether this provider supports Apple Pay as a funding source
    /// </summary>
    public bool SupportsApplePay { get; set; }

    /// <summary>
    /// Whether this provider supports Google Pay as a funding source
    /// </summary>
    public bool SupportsGooglePay { get; set; }

    // Platform restrictions
    public int AllowedPlatforms { get; set; }
    public bool IsAppStoreExclusive { get; set; }

    // Currency support
    public string? SupportedCurrencies { get; set; }
    public string? DefaultCurrency { get; set; }

    /// <summary>
    /// Get the icon class for this provider
    /// </summary>
    public string GetIconClass()
    {
        return ProviderType switch
        {
            1 => "bi bi-credit-card", // Stripe
            2 => "bi bi-paypal", // PayPal
            3 => "bi bi-credit-card-2-front", // Square
            10 => "bi bi-apple", // AppleAppStore
            11 => "bi bi-google-play", // GooglePlayStore
            20 => "bi bi-apple", // ApplePay
            21 => "bi bi-google", // GooglePay
            30 or 31 or 32 => "bi bi-credit-card", // BNPL
            40 or 41 => "bi bi-credit-card", // Regional
            50 or 51 => "bi bi-currency-bitcoin", // Crypto
            _ => "bi bi-credit-card"
        };
    }

    /// <summary>
    /// Get the display label for this provider
    /// </summary>
    public string GetDisplayLabel()
    {
        return DisplayName ?? ProviderType switch
        {
            1 => "Credit/Debit Card",
            2 => "PayPal",
            3 => "Square",
            10 => "In-App Purchase",
            11 => "Google Play",
            20 => "Apple Pay",
            21 => "Google Pay",
            30 => "Klarna - Pay Later",
            31 => "Affirm - Pay Later",
            32 => "Afterpay",
            40 => "Razorpay",
            50 => "Pay with Crypto",
            51 => "BitPay",
            _ => Name
        };
    }
}

/// <summary>
/// Payment configuration for an app
/// </summary>
public class AppPaymentConfigurationDto
{
    public string AppId { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public bool IsPaymentEnabled { get; set; }
    public string DefaultCurrency { get; set; } = "USD";
    public string? SupportedCurrencies { get; set; }
    public bool AllowSavedPaymentMethods { get; set; }
    public bool RequireBillingAddress { get; set; }
    public bool Require3DSecure { get; set; }

    /// <summary>
    /// List of payment providers enabled for this app
    /// </summary>
    public List<PaymentProviderDto> Providers { get; set; } = new();

    /// <summary>
    /// Default provider ID for this app
    /// </summary>
    public string? DefaultProviderId { get; set; }
}

/// <summary>
/// Available payment providers filtered by platform
/// </summary>
public class PlatformFilteredProvidersDto
{
    public string AppId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public bool RequiresAppStorePayment { get; set; }
    public string? RequiredProviderId { get; set; }
    public List<PaymentProviderDto> AvailableProviders { get; set; } = new();
    public PaymentProviderDto? DefaultProvider { get; set; }
}

/// <summary>
/// Request to initiate a payment
/// </summary>
public class InitiatePaymentRequest
{
    public string ProviderId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? Description { get; set; }
    public string? CustomerId { get; set; }
    public string? CustomerEmail { get; set; }
    public string? OrderId { get; set; }
    public string? SubscriptionId { get; set; }
    public string? PricingModelId { get; set; }
    public bool IsSubscription { get; set; }

    /// <summary>
    /// Billing frequency for subscription payments (Monthly, Quarterly, Annually)
    /// </summary>
    public string? BillingFrequency { get; set; }

    public string? ReturnUrl { get; set; }
    public string? CancelUrl { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Response from initiating a payment
/// </summary>
public class InitiatePaymentResponse
{
    public bool Success { get; set; }
    public string? PaymentIntentId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RedirectUrl { get; set; }
    public string? ApprovalUrl { get; set; }
    public string? OrderId { get; set; }

    /// <summary>
    /// For subscription payments, the Stripe Subscription ID
    /// </summary>
    public string? SubscriptionId { get; set; }

    /// <summary>
    /// Whether the client needs to confirm payment (e.g., Stripe confirmCardPayment).
    /// False when subscription has a trial, $0 amount, or payment succeeded immediately.
    /// </summary>
    public bool RequiresClientConfirmation { get; set; } = true;

    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentProviderType ProviderType { get; set; }

    /// <summary>
    /// For app store purchases, the product IDs to request
    /// </summary>
    public List<string>? ProductIds { get; set; }

    /// <summary>
    /// Additional provider-specific data
    /// </summary>
    public Dictionary<string, object>? ProviderData { get; set; }
}

/// <summary>
/// Result of completing a payment
/// </summary>
public class PaymentCompletionResult
{
    public bool Success { get; set; }
    public string? TransactionId { get; set; }
    public string? PaymentIntentId { get; set; }
    public string? SubscriptionId { get; set; }
    public decimal? AmountPaid { get; set; }
    public string? Currency { get; set; }
    public string? Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ReceiptUrl { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Saved payment method for a customer
/// </summary>
public class SavedPaymentMethodDto
{
    public string Id { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public int ProviderType { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Last4 { get; set; }
    public string? Brand { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpYear { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Event args for payment success
/// </summary>
public class PaymentSuccessEventArgs
{
    public string? TransactionId { get; set; }
    public string? PaymentIntentId { get; set; }
    public string? SubscriptionId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public int ProviderType { get; set; }
    public string? ReceiptUrl { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Event args for payment failure
/// </summary>
public class PaymentFailureEventArgs
{
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public int ProviderType { get; set; }
    public bool IsRetryable { get; set; }
    public string? DeclineCode { get; set; }
}
