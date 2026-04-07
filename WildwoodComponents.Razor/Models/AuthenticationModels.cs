using System.ComponentModel.DataAnnotations;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Models;

// ──────────────────────────────────────────────
// View Models
// ──────────────────────────────────────────────

/// <summary>
/// View model for the AuthenticationViewComponent
/// </summary>
public class AuthenticationViewModel
{
    public string ReturnUrl { get; set; } = "/";
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-auth";
    public bool AllowRegistration { get; set; } = true;
    public List<string> ExternalProviders { get; set; } = new();
    public bool EnableTwoFactor { get; set; }
    public string Title { get; set; } = "Welcome";
    public string Subtitle { get; set; } = "Sign in to your account";
    public string ExternalLoginPath { get; set; } = "/Account/ExternalLogin";
}

// ──────────────────────────────────────────────
// Client-facing request DTOs (used by proxy controllers)
// NOTE: These types (LoginRequest, RegisterRequest, ForgotPasswordRequest,
// ResetPasswordRequest, TwoFactorVerifyRequest) are intentionally separate from
// the Shared project's WildwoodAuthModels.cs types. The Razor versions are
// simplified for server-side proxy use (e.g., no AppId, different field sets),
// while the Shared versions map to WildwoodAPI's full endpoint contracts.
// ──────────────────────────────────────────────

/// <summary>
/// Login request from the consuming app's proxy controller
/// </summary>
public class LoginRequest
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

/// <summary>
/// Registration request from the consuming app's proxy controller
/// </summary>
public class RegisterRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Unique username for the account. Falls back to Email if not provided.
    /// </summary>
    public string? Username { get; set; }

    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? RegistrationToken { get; set; }
}

/// <summary>
/// Forgot password request
/// </summary>
public class ForgotPasswordRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Password reset request
/// </summary>
public class ResetPasswordRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// <summary>
/// Two-factor verification request from the consuming app's proxy controller
/// </summary>
public class TwoFactorVerifyRequest
{
    [Required]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Session ID from the login response when 2FA is required
    /// </summary>
    public string? SessionId { get; set; }

    public string? Method { get; set; }
    public bool RememberDevice { get; set; }
}

// WildwoodAPI-specific DTOs now live in WildwoodComponents.Shared.Models
// (WildwoodLoginRequest, WildwoodRegisterRequest, WildwoodForgotPasswordRequest,
//  WildwoodResetPasswordRequest, WildwoodTwoFactorVerifyRequest, WildwoodRefreshTokenRequest,
//  WildwoodAuthenticateResponse, WildwoodTwoFactorResponse, TwoFactorMethodInfo)

// ──────────────────────────────────────────────
// Client-facing response DTOs (returned to proxy controllers)
// ──────────────────────────────────────────────

/// <summary>
/// Normalized authentication response returned to the consuming app
/// </summary>
public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public string? UserId { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public List<string>? Roles { get; set; }
    public bool RequiresTwoFactor { get; set; }
    public bool RequiresEmailConfirmation { get; set; }
    public bool RequiresPasswordReset { get; set; }
    public bool RequiresDisclaimerAcceptance { get; set; }

    /// <summary>
    /// Session ID for 2FA flow — must be passed to VerifyTwoFactorAsync
    /// </summary>
    public string? TwoFactorSessionId { get; set; }

    /// <summary>
    /// Available 2FA methods when RequiresTwoFactor is true
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

    /// <summary>
    /// Creates an AuthResponse from WildwoodAPI's response format
    /// </summary>
    internal static AuthResponse FromWildwoodResponse(WildwoodAuthenticateResponse ww)
    {
        var displayName = $"{ww.FirstName} {ww.LastName}".Trim();
        return new AuthResponse
        {
            Token = ww.JwtToken,
            RefreshToken = ww.RefreshToken,
            UserId = ww.Id,
            Email = ww.Email,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? ww.Email : displayName,
            RequiresTwoFactor = ww.RequiresTwoFactor,
            RequiresPasswordReset = ww.RequiresPasswordReset,
            RequiresDisclaimerAcceptance = ww.RequiresDisclaimerAcceptance,
            TwoFactorSessionId = ww.TwoFactorSessionId,
            AvailableTwoFactorMethods = ww.AvailableTwoFactorMethods,
            DefaultTwoFactorMethod = ww.DefaultTwoFactorMethod,
            TwoFactorSessionExpiresIn = ww.TwoFactorSessionExpiresIn
        };
    }
}

/// <summary>
/// Auth configuration response (social providers, registration settings)
/// </summary>
public class AuthConfigResponse
{
    public bool AllowRegistration { get; set; }
    public bool RequireRegistrationToken { get; set; }
    public bool RequireEmailConfirmation { get; set; }
    public List<string> ExternalProviders { get; set; } = new();
    public bool EnableTwoFactor { get; set; }
    public List<string> TwoFactorMethods { get; set; } = new();
}

/// <summary>
/// Result of an authentication operation
/// </summary>
public class AuthResult
{
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
    public bool RequiresTwoFactor { get; set; }
    public AuthResponse? Response { get; set; }

    public static AuthResult Success(AuthResponse response) => new()
    {
        Succeeded = true,
        Response = response,
        RequiresTwoFactor = response.RequiresTwoFactor
    };

    public static AuthResult Failure(string message, bool requiresTwoFactor = false) => new()
    {
        Succeeded = false,
        ErrorMessage = message,
        RequiresTwoFactor = requiresTwoFactor
    };
}

/// <summary>
/// Generic API result
/// </summary>
public class ApiResult
{
    public bool Succeeded { get; set; }
    public string? Message { get; set; }

    public static ApiResult Ok(string? message = null) => new() { Succeeded = true, Message = message };
    public static ApiResult Fail(string message) => new() { Succeeded = false, Message = message };
}

/// <summary>
/// API error response DTO
/// </summary>
public class ApiErrorResponse
{
    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
    public bool RequiresTwoFactor { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
}
