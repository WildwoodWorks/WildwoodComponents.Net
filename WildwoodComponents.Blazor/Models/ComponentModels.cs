using System.ComponentModel.DataAnnotations;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Models
{
    public class LoginRequest
    {
        /// <summary>
        /// Username for authentication (primary login identifier)
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Email address (optional, for OAuth only)
        /// </summary>
        public string Email { get; set; } = string.Empty;

        public string? Password { get; set; }

        public string? ProviderName { get; set; }

        public string? ProviderToken { get; set; }

        public string? AppId { get; set; }

        public bool RememberMe { get; set; }

        public string? CaptchaResponse { get; set; }

        public string? LicenseToken { get; set; }

        public string? Platform { get; set; }

        public string? DeviceInfo { get; set; }

        public string? AppVersion { get; set; }

        /// <summary>
        /// Trusted device token for bypassing 2FA on remembered devices
        /// </summary>
        public string? TrustedDeviceToken { get; set; }
    }

    public class RegistrationRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Username for the account (used for login). If not provided, email will be used.
        /// </summary>
        public string? Username { get; set; }

        [Required]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        public string LastName { get; set; } = string.Empty;

        public string? Password { get; set; }

        public string? ProviderName { get; set; }

        public string? ProviderToken { get; set; }

        [Required]
        public string AppId { get; set; } = string.Empty;

        public string? Platform { get; set; }

        public string? DeviceInfo { get; set; }

        public string? PhoneNumber { get; set; }

        public string? CaptchaResponse { get; set; }

        public string? LicenseToken { get; set; }

        public string? RegistrationToken { get; set; }
    }
    public class AuthenticationResponse
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("jwtToken")]
        public string JwtToken { get; set; } = string.Empty;

        public string RefreshToken { get; set; } = string.Empty;
        public bool RequiresTwoFactor { get; set; }
        public bool RequiresPasswordReset { get; set; }
        public List<string> Roles { get; set; } = new();
        public string? CompanyId { get; set; }
        public List<string> Permissions { get; set; } = new();

        // Two-Factor Authentication fields
        /// <summary>
        /// Session ID for 2FA verification flow.
        /// Only populated when RequiresTwoFactor is true.
        /// </summary>
        public string? TwoFactorSessionId { get; set; }

        /// <summary>
        /// Available 2FA methods for the user.
        /// Only populated when RequiresTwoFactor is true.
        /// </summary>
        public List<TwoFactorMethodInfo>? AvailableTwoFactorMethods { get; set; }

        /// <summary>
        /// User's preferred/default 2FA method type.
        /// Only populated when RequiresTwoFactor is true.
        /// </summary>
        public string? DefaultTwoFactorMethod { get; set; }

        /// <summary>
        /// Seconds until the 2FA session expires.
        /// Only populated when RequiresTwoFactor is true.
        /// </summary>
        public int? TwoFactorSessionExpiresIn { get; set; }

        // Disclaimer Acceptance fields
        /// <summary>
        /// Whether the user needs to accept new or updated disclaimers.
        /// Tokens are still returned so the client can call the acceptance endpoint.
        /// </summary>
        public bool RequiresDisclaimerAcceptance { get; set; }

        /// <summary>
        /// List of pending disclaimers that need acceptance.
        /// Only populated when RequiresDisclaimerAcceptance is true.
        /// </summary>
        public List<PendingDisclaimerModel>? PendingDisclaimers { get; set; }
    }

    // TwoFactorMethodInfo now lives in WildwoodComponents.Shared.Models

    /// <summary>
    /// Request to verify a 2FA code
    /// </summary>
    public class TwoFactorVerifyRequest
    {
        public string SessionId { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string ProviderType { get; set; } = string.Empty;
        public bool RememberDevice { get; set; }
        public string? DeviceFingerprint { get; set; }
        public string? DeviceName { get; set; }
    }

    /// <summary>
    /// Response from 2FA code verification
    /// </summary>
    public class TwoFactorVerifyResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public AuthenticationResponse? AuthResponse { get; set; }
        public string? TrustedDeviceToken { get; set; }
    }

    /// <summary>
    /// Request to send a 2FA verification code
    /// </summary>
    public class TwoFactorSendCodeRequest
    {
        public string SessionId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response from sending a 2FA code
    /// </summary>
    public class TwoFactorSendCodeResponse
    {
        public bool Success { get; set; }
        public string? MaskedDestination { get; set; }
        public int? ExpiresInSeconds { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class AuthProvider
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public string? ClientId { get; set; }
        public string? RedirectUri { get; set; }
    }

    // ComponentTheme now lives in WildwoodComponents.Shared.Models

    public class CaptchaConfiguration
    {
        public bool IsEnabled { get; set; }
        public string ProviderType { get; set; } = "GoogleReCaptcha";
        public string? SiteKey { get; set; }
        public double MinimumScore { get; set; } = 0.5;
        public bool RequireForLogin { get; set; } = true;
        public bool RequireForRegistration { get; set; } = true;
        public bool RequireForPasswordReset { get; set; } = true;
    }

    // AI Chat types now live in WildwoodComponents.Shared.Models
    // (AIMessage, AISession, AIConfiguration, AISessionSummary, AIChatSettings,
    //  ChatTypingIndicator, AIChatRequest, AIChatResponse)

    // Secure Messaging types now live in WildwoodComponents.Shared.Models
    // (SecureMessage, MessageThread, ThreadParticipant, ThreadSettings,
    //  MessageAttachment, MessageReaction, MessageReadReceipt, CompanyAppUser,
    //  OnlineStatus, TypingIndicator, MessageSearchResult, MessageDraft,
    //  PendingAttachment, SecureMessagingSettings, NotificationSettings,
    //  MessageType, ThreadType, ParticipantRole, UserStatus enums)

    // Notification types now live in WildwoodComponents.Shared.Models
    // (ToastNotification, NotificationAction, NotificationActionArgs,
    //  NotificationType, NotificationActionStyle, NotificationPosition enums)

    public class AuthenticationConfiguration
    {
        public bool IsEnabled { get; set; } = false;
        public string DefaultProvider { get; set; } = "local";
        public bool AllowLocalAuth { get; set; } = true;
        public bool RequireEmailVerification { get; set; } = true;

        public bool AllowPasswordReset { get; set; } = true;
        public bool ShowDetailedErrors { get; set; } = false;

        // Registration mode settings
        /// <summary>
        /// Allow users to register using a registration token (token defines its own pricing model)
        /// </summary>
        public bool AllowTokenRegistration { get; set; } = false;

        /// <summary>
        /// Allow users to register without a token (open registration)
        /// </summary>
        public bool AllowOpenRegistration { get; set; } = false;

        /// <summary>
        /// Default pricing model ID for open registration. Null means free registration.
        /// </summary>
        public string? DefaultPricingModelId { get; set; }

        /// <summary>
        /// Name of the default pricing model (for display purposes)
        /// </summary>
        public string? DefaultPricingModelName { get; set; }

        // Email verification settings
        /// <summary>
        /// Require email verification for open registration.
        /// Only works if the company has email configuration set up.
        /// </summary>
        public bool RequireEmailVerificationForOpenRegistration { get; set; } = false;

        /// <summary>
        /// Indicates if the company has email configuration set up (read-only, for UI display)
        /// </summary>
        public bool HasEmailConfiguration { get; set; } = false;

        // Rate limiting settings
        /// <summary>
        /// Maximum registrations per hour for this app. 0 = unlimited.
        /// </summary>
        public int RegistrationRateLimitPerHour { get; set; } = 0;

        /// <summary>
        /// Maximum registrations per day for this app. 0 = unlimited.
        /// </summary>
        public int RegistrationRateLimitPerDay { get; set; } = 0;

        /// <summary>
        /// Maximum registrations per IP address per hour. 0 = unlimited.
        /// </summary>
        public int RegistrationRateLimitPerIpPerHour { get; set; } = 0;

        // Password policy settings
        public int PasswordMinimumLength { get; set; } = 8;
        public bool PasswordRequireDigit { get; set; } = true;
        public bool PasswordRequireLowercase { get; set; } = true;
        public bool PasswordRequireUppercase { get; set; } = true;
        public bool PasswordRequireSpecialChar { get; set; } = true;
        public int PasswordHistoryLimit { get; set; } = 5;
        public int PasswordExpiryDays { get; set; } = 90;
    }

    // API Response models for authentication configuration
    public class AppComponentAuthProvidersResponse
    {
        public string Id { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public string DefaultProvider { get; set; } = string.Empty;
        public bool AllowLocalAuth { get; set; }
        public bool RequireEmailVerification { get; set; }
        public bool AllowTokenRegistration { get; set; }
        public bool AllowOpenRegistration { get; set; }
        public bool AllowPasswordReset { get; set; }
        public int PasswordMinimumLength { get; set; }
        public bool PasswordRequireDigit { get; set; }
        public bool PasswordRequireLowercase { get; set; }
        public bool PasswordRequireUppercase { get; set; }
        public bool PasswordRequireSpecialChar { get; set; }
        public int PasswordHistoryLimit { get; set; }
        public int PasswordExpiryDays { get; set; }
        public List<AuthProviderDetails> AuthProviders { get; set; } = new();
    }

    public class AuthProviderDetails
    {
        public string Id { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public int DisplayOrder { get; set; }
        public string? ButtonText { get; set; }
        public string? ButtonColor { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? RedirectUri { get; set; }
        public string? AuthUrl { get; set; }
        public string? TokenUrl { get; set; }
        public string? Scope { get; set; }
    }

    // Payment Models
    public class PaymentRequest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "USD";
        public string MerchantId { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.CreditCard;

        // Credit Card Details
        public string CardNumber { get; set; } = string.Empty;
        public string ExpiryDate { get; set; } = string.Empty;
        public string CVV { get; set; } = string.Empty;
        public string CardholderName { get; set; } = string.Empty;

        // Billing Address
        public BillingAddress BillingAddress { get; set; } = new();

        // Additional Data
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public class BillingAddress
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
    }

    public class PaymentResult
    {
        public bool IsSuccess { get; set; }
        public string? TransactionId { get; set; }
        public string? PaymentId { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ErrorCode { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
        public PaymentStatus Status { get; set; }
        public Dictionary<string, object>? AdditionalData { get; set; }
    }

    public enum PaymentMethod
    {
        CreditCard,
        BankTransfer,
        DigitalWallet,
        Cryptocurrency
    }

    public enum PaymentStatus
    {
        Pending,
        Processing,
        Completed,
        Failed,
        Cancelled,
        Refunded
    }

    // Subscription Models
    public class SubscriptionPlan
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Currency { get; set; } = "USD";
        public BillingInterval BillingInterval { get; set; } = BillingInterval.Monthly;
        public decimal? MonthlyEquivalent { get; set; }
        public bool IsFree { get; set; }
        public bool IsRecommended { get; set; }
        public List<string>? Features { get; set; }
        public List<string>? Limitations { get; set; }
        public int? TrialDays { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public class Subscription
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } = string.Empty;
        public string PlanId { get; set; } = string.Empty;
        public string PlanName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Currency { get; set; } = "USD";
        public BillingInterval BillingInterval { get; set; }
        public SubscriptionStatus Status { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime? NextBillingDate { get; set; }
        public DateTime? CancelledAt { get; set; }
        public List<string>? Features { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public class SubscriptionResult
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ErrorCode { get; set; }
        public Subscription? Subscription { get; set; }
        public string? PaymentUrl { get; set; }
        public Dictionary<string, object>? AdditionalData { get; set; }
    }

    public enum BillingInterval
    {
        Weekly,
        Monthly,
        Quarterly,
        Yearly
    }

    public enum SubscriptionStatus
    {
        Active,
        Paused,
        Cancelled,
        Expired,
        Trial,
        PendingPayment
    }

    // Exception Classes
    public class PaymentException : Exception
    {
        public PaymentException(string message) : base(message) { }
        public PaymentException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class SubscriptionException : Exception
    {
        public SubscriptionException(string message) : base(message) { }
        public SubscriptionException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class ValidationException : Exception
    {
        public ValidationException(string message) : base(message) { }
        public ValidationException(string message, Exception innerException) : base(message, innerException) { }
    }

}
