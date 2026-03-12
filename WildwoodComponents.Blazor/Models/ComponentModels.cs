using System.ComponentModel.DataAnnotations;

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

    public class ComponentTheme
    {
        public string PrimaryColor { get; set; } = "#007bff";
        public string SecondaryColor { get; set; } = "#6c757d";
        public string SuccessColor { get; set; } = "#28a745";
        public string WarningColor { get; set; } = "#ffc107";
        public string DangerColor { get; set; } = "#dc3545";
        public string InfoColor { get; set; } = "#17a2b8";
        public string LightColor { get; set; } = "#f8f9fa";
        public string DarkColor { get; set; } = "#343a40";
        public string FontFamily { get; set; } = "system-ui, -apple-system, sans-serif";
        public string BorderRadius { get; set; } = "0.375rem";
        public string BoxShadow { get; set; } = "0 0.125rem 0.25rem rgba(0, 0, 0, 0.075)";
    }

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

    public class AIMessage
    {
        public string Id { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty; // user, assistant, system
        public string Content { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("createdAt")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        public int TokenCount { get; set; }
        public bool IsError { get; set; }
        
        // Additional properties from server DTO
        public string? SessionId { get; set; }
        public int MessageOrder { get; set; }
        public string? ParentMessageId { get; set; }
        public bool IsEdited { get; set; }
        public DateTime? EditedAt { get; set; }
    }

    public class AISession
    {
        public string Id { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("sessionName")]
        public string Name { get; set; } = string.Empty;
        
        public List<AIMessage> Messages { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? EndedAt { get; set; }
        public bool IsActive { get; set; } = true;
        
        // Additional properties from server DTO
        public string? UserId { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("aIConfigurationId")]
        public string? AIConfigurationId { get; set; }
        
        public DateTime LastAccessedAt { get; set; }
        public int MessageCount { get; set; }
        public string? LastMessagePreview { get; set; }
    }

    public class AIConfiguration
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string ProviderType { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool PersistentSessionEnabled { get; set; }
        public string ConfigurationType { get; set; } = "chat";

        // TTS Configuration
        public bool EnableTTS { get; set; } = false;
        public string? TTSModel { get; set; }
        public string? TTSDefaultVoice { get; set; }
        public double TTSDefaultSpeed { get; set; } = 1.0;
        public string TTSDefaultFormat { get; set; } = "mp3";
        public string? TTSEnabledVoicesJson { get; set; }
    }

    // Secure Messaging Models
    public class SecureMessage
    {
        public string Id { get; set; } = string.Empty;
        public string ThreadId { get; set; } = string.Empty;
        public string SenderId { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string? SenderAvatar { get; set; }
        public string Content { get; set; } = string.Empty;
        public MessageType MessageType { get; set; } = MessageType.Text;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public bool IsEdited { get; set; }
        public bool IsDeleted { get; set; }
        public string? ReplyToMessageId { get; set; }
        public List<MessageAttachment> Attachments { get; set; } = new();
        public List<MessageReaction> Reactions { get; set; } = new();
        public List<MessageReadReceipt> ReadReceipts { get; set; } = new();
    }

    public class MessageThread
    {
        public string Id { get; set; } = string.Empty;
        public string CompanyAppId { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public ThreadType ThreadType { get; set; } = ThreadType.Direct;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
        public string? LastMessagePreview { get; set; }
        public int UnreadCount { get; set; }
        public List<ThreadParticipant> Participants { get; set; } = new();
        public ThreadSettings Settings { get; set; } = new();
    }

    public class ThreadParticipant
    {
        public string Id { get; set; } = string.Empty;
        public string ThreadId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string? Avatar { get; set; }
        public ParticipantRole Role { get; set; } = ParticipantRole.Member;
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LeftAt { get; set; }
        public bool IsActive { get; set; } = true;
    }
    public class ThreadSettings
    {
        public string Id { get; set; } = string.Empty;
        public string ThreadId { get; set; } = string.Empty;
        public bool AllowFileSharing { get; set; } = true;
        public bool AllowReactions { get; set; } = true;
        public bool NotificationsEnabled { get; set; } = true;
        public bool ReadReceiptsEnabled { get; set; } = true;
        public int MaxParticipants { get; set; } = 100;
        public List<string> AllowedFileTypes { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
    public class MessageAttachment
    {
        public string Id { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string StoragePath { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class MessageReaction
    {
        public string Id { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Emoji { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class MessageReadReceipt
    {
        public string Id { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public DateTime ReadAt { get; set; } = DateTime.UtcNow;
    }

    public class CompanyAppUser
    {
        public string Id { get; set; } = string.Empty;
        public string CompanyAppId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Avatar { get; set; }
        public UserStatus Status { get; set; } = UserStatus.Available;
        public bool IsActive { get; set; } = true;
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    }

    public class OnlineStatus
    {
        public string UserId { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public UserStatus Status { get; set; } = UserStatus.Available;
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
        public string? StatusMessage { get; set; }
    }

    public class TypingIndicator
    {
        public string UserId { get; set; } = string.Empty;
        public string ThreadId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public bool IsVisible { get; set; }
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    }

    // Enums for messaging
    public enum MessageType
    {
        Text = 1,
        File = 2,
        Image = 3,
        System = 4
    }

    public enum ThreadType
    {
        Direct = 1,
        Group = 2,
        Channel = 3
    }

    public enum ParticipantRole
    {
        Member = 1,
        Admin = 2,
        Owner = 3
    }

    public enum UserStatus
    {
        Available = 1,
        Busy = 2,
        Away = 3,
        DoNotDisturb = 4,
        Offline = 5
    }

    // Configuration and Settings Classes
    public class SecureMessagingSettings
    {
        public string CompanyAppId { get; set; } = string.Empty;
        public string ApiBaseUrl { get; set; } = string.Empty;
        public bool EnableEncryption { get; set; } = true;
        public bool EnableFileUploads { get; set; } = true;
        public bool EnableReactions { get; set; } = true;
        public bool EnableTypingIndicators { get; set; } = true;
        public bool EnableReadReceipts { get; set; } = true;
        public bool EnableNotifications { get; set; } = true;
        public bool AutoScrollToBottom { get; set; } = true;
        public long MaxFileSize { get; set; } = 10485760; // 10MB
        public int MaxMessageLength { get; set; } = 2000;
        public int MessagesPerPage { get; set; } = 50;
        public ComponentTheme Theme { get; set; } = new();
        public NotificationSettings Notifications { get; set; } = new();
    }

    public class NotificationSettings
    {
        public bool DesktopNotifications { get; set; } = true;
        public bool SoundNotifications { get; set; } = true;
        public bool VibrateOnMobile { get; set; } = true;
        public string NotificationSound { get; set; } = "default";
    }

    public class PendingAttachment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public byte[]? FileData { get; set; }
        public bool IsUploading { get; set; }
        public double UploadProgress { get; set; }
    }

    public class MessageSearchResult
    {
        public string MessageId { get; set; } = string.Empty;
        public string ThreadId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string ThreadSubject { get; set; } = string.Empty;
    }

    public class MessageDraft
    {
        public string ThreadId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? ReplyToMessageId { get; set; }
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }
    public class AIChatSettings
    {
        public string ApiBaseUrl { get; set; } = string.Empty;
        public bool EnableSessions { get; set; } = true;
        public bool AutoLoadRecentSession { get; set; } = true;
        public bool ShowTokenUsage { get; set; } = true;
        public bool AutoScroll { get; set; } = true;
        public bool EnableFileUpload { get; set; } = false;
        public bool EnableVoiceInput { get; set; } = false;
        public bool EnableSpeechToText { get; set; } = false;
        public bool EnableTextToSpeech { get; set; } = false;
        public bool UseServerTTS { get; set; } = true;
        public string? TTSVoice { get; set; } = "alloy";
        public double TTSSpeed { get; set; } = 1.0;
        public bool ShowDebugInfo { get; set; } = false;
        public bool ShowConfigurationName { get; set; } = true;
        public bool ShowConfigurationSelector { get; set; } = true;
        public string PlaceholderText { get; set; } = "Ask anything";
        public string WelcomeMessage { get; set; } = "What's on the agenda today?";
        public int MaxHistorySize { get; set; } = 100;
        public int MaxMessageLength { get; set; } = 4000;
        public ComponentTheme Theme { get; set; } = new();
    }

    public class AISessionSummary
    {
        public string Id { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("sessionName")]
        public string Name { get; set; } = string.Empty;
        
        public int MessageCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public bool IsActive { get; set; } = true;
        public string? LastMessagePreview { get; set; }
    }
    public class ChatTypingIndicator
    {
        public bool IsVisible { get; set; }
        public string Text { get; set; } = "is typing...";
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    }

    public class AIChatRequest
    {
        public string ConfigurationId { get; set; } = string.Empty;
        public string? SessionId { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool SaveToSession { get; set; } = true;
        public Dictionary<string, string> MacroValues { get; set; } = new();
    }

    public class AIChatResponse
    {
        public string Id { get; set; } = string.Empty;
        public string? SessionId { get; set; }
        public string Response { get; set; } = string.Empty;
        public int TokensUsed { get; set; }
        public string Model { get; set; } = string.Empty;
        public string ProviderType { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsError { get; set; }
        public string? ErrorMessage { get; set; }
        /// <summary>
        /// Structured error code from the API (e.g., "AI_TOKENS", "RATE_LIMIT").
        /// Allows callers to distinguish error types without parsing the message string.
        /// </summary>
        public string? ErrorCode { get; set; }
    }

    // Extension Methods Helper Class
    public static class ComponentExtensions
    {
        public static string GetThemeClass(this ComponentTheme theme)
        {
            return $"theme-{theme.PrimaryColor.Replace("#", "").ToLower()}";
        }
    }

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

    // Notification Models
    public class ToastNotification
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public NotificationType Type { get; set; } = NotificationType.Info;
        public DateTime? Timestamp { get; set; }
        public bool IsVisible { get; set; } = true;
        public bool IsDismissible { get; set; } = true;
        public int? Duration { get; set; }
        public string? CssClass { get; set; }
        public List<NotificationAction>? Actions { get; set; }
    }

    public class NotificationAction
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Text { get; set; } = string.Empty;
        public NotificationActionStyle Style { get; set; } = NotificationActionStyle.Primary;
        public bool DismissOnClick { get; set; } = true;
        public Dictionary<string, object>? Data { get; set; }
    }

    public class NotificationActionArgs
    {
        public string NotificationId { get; set; } = string.Empty;
        public string ActionId { get; set; } = string.Empty;
        public string ActionText { get; set; } = string.Empty;
        public Dictionary<string, object>? Data { get; set; }
    }

    public enum NotificationType
    {
        Info,
        Success,
        Warning,
        Error
    }

    public enum NotificationActionStyle
    {
        Primary,
        Secondary,
        Success,
        Danger,
        Warning,
        Info,
        Light,
        Dark
    }

    public enum NotificationPosition
    {
        TopLeft,
        TopRight,
        TopCenter,
        BottomLeft,
        BottomRight,
        BottomCenter
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