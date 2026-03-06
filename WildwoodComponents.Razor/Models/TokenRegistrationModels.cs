using System.ComponentModel.DataAnnotations;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Models;

/// <summary>
/// View model for the TokenRegistrationViewComponent.
/// Razor Pages equivalent of TokenRegistrationComponent's inline models.
/// </summary>
public class TokenRegistrationViewModel
{
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string? AppId { get; set; }

    /// <summary>
    /// Pre-supplied registration token (auto-validated on load)
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// If true, token-based registration is allowed (default true)
    /// </summary>
    public bool AllowTokenRegistration { get; set; } = true;

    /// <summary>
    /// If true, open registration (without token) is allowed (default false)
    /// </summary>
    public bool AllowOpenRegistration { get; set; }

    /// <summary>
    /// Default pricing model ID for open registration (null = free)
    /// </summary>
    public string? DefaultPricingModelId { get; set; }

    /// <summary>
    /// If true, automatically logs in user after successful registration (default true)
    /// </summary>
    public bool AutoLogin { get; set; } = true;

    /// <summary>
    /// URL to redirect to after successful auto-login
    /// </summary>
    public string? RedirectUrl { get; set; }

    /// <summary>
    /// Base URL for proxy endpoints (default: /api/wildwood-registration)
    /// </summary>
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-registration";

    /// <summary>
    /// Base URL for payment proxy endpoints (for embedded PaymentViewComponent)
    /// </summary>
    public string? PaymentProxyBaseUrl { get; set; }

    /// <summary>
    /// Whether token is strictly required (no open registration)
    /// </summary>
    public bool TokenIsRequired => AllowTokenRegistration && !AllowOpenRegistration;

    /// <summary>
    /// Whether token is optional (both modes allowed)
    /// </summary>
    public bool TokenIsOptional => AllowTokenRegistration && AllowOpenRegistration;
}

// ──────────────────────────────────────────────
// API Request/Response DTOs for Token Registration
// ──────────────────────────────────────────────

public class TokenValidationResponse
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public string? AssignedRole { get; set; }
    public string? RestrictedToAppId { get; set; }
    public List<string> RestrictedToAppIds { get; set; } = new();
    public string? AppName { get; set; }
    public string? RestrictedToAppName { get; set; }
    public List<string> RestrictedToAppNames { get; set; } = new();
    public string? CompanyClientId { get; set; }
    public string? CompanyClientName { get; set; }
    public string? CompanyName { get; set; }
    public string? PricingModelName { get; set; }
    public string? PricingDescription { get; set; }
    public decimal? PriceAmount { get; set; }
    public string? PriceCurrency { get; set; }
    public string? BillingFrequency { get; set; }
    public bool IsSubscription { get; set; }
    public bool RequiresPaymentSetup { get; set; }
    public int? MaxUsages { get; set; }
    public int CurrentUsages { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public bool RequiresStripeSetup { get; set; }
}

public class TokenRegistrationRequest
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? AppId { get; set; }
    public string? Platform { get; set; }
    public string? DeviceInfo { get; set; }
    public List<DisclaimerAcceptanceResult>? DisclaimerAcceptances { get; set; }
}

public class OpenRegistrationRequest
{
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? AppId { get; set; }
    public string? Platform { get; set; }
    public string? DeviceInfo { get; set; }
    public string? PricingModelId { get; set; }
    public List<DisclaimerAcceptanceResult>? DisclaimerAcceptances { get; set; }
}

public class ValidateRegistrationRequest
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Token { get; set; }
    public string? AppId { get; set; }
}

public class RegistrationValidationResponse
{
    public bool IsValid { get; set; }
    public bool UsernameAvailable { get; set; } = true;
    public bool EmailAvailable { get; set; } = true;
    public bool PasswordValid { get; set; } = true;
    public List<string>? PasswordErrors { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public bool RequiresPayment { get; set; }
    public bool IsSubscription { get; set; }
    public string? PricingModelId { get; set; }
    public string? PricingModelName { get; set; }
    public decimal? PriceAmount { get; set; }
    public string? Currency { get; set; }
    public string[]? GrantedApps { get; set; }
    public string? PaymentAppId { get; set; }
}

public class RegistrationSuccessResponse
{
    public bool Success { get; set; }
    public string? UserId { get; set; }
    public string? Message { get; set; }
    public bool RequiresStripeSetup { get; set; }
    public bool RequiresPaymentSetup { get; set; }
    public bool IsSubscription { get; set; }
    public string? PaymentProviderId { get; set; }
    public string? PaymentProviderName { get; set; }
    public int? PaymentProviderType { get; set; }
    public string? PaymentPublishableKey { get; set; }
    public string? PaymentAppId { get; set; }
    public string? PricingModelId { get; set; }
    public string? PricingModelName { get; set; }
    public string[]? GrantedApps { get; set; }
}

public class RegistrationSuccessEventArgs
{
    public string? UserId { get; set; }
    public string? Email { get; set; }
    public bool RequiresStripeSetup { get; set; }
    public bool RequiresPaymentSetup { get; set; }
    public bool IsSubscription { get; set; }
}

public class PricingDetails
{
    public string? PlanName { get; set; }
    public string? PlanDescription { get; set; }
    public decimal PriceAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? BillingFrequency { get; set; }
    public bool IsSubscription { get; set; }
}

public class PricingModelResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string BillingFrequency { get; set; } = "Monthly";
    public string? Currency { get; set; } = "USD";
    public bool IsActive { get; set; }
}

public class SkipPaymentRequest
{
    public string UserId { get; set; } = string.Empty;
    public string RegistrationToken { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
