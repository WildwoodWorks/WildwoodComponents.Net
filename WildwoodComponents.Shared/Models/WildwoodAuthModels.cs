namespace WildwoodComponents.Shared.Models;

// ──────────────────────────────────────────────
// WildwoodAPI Auth Request DTOs
// These map to WildwoodAPI's endpoint contracts.
// ──────────────────────────────────────────────

/// <summary>
/// Maps to WildwoodAPI's AuthenticateRequest DTO.
/// </summary>
public class WildwoodLoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public string? Platform { get; set; }
    public string? DeviceInfo { get; set; }
    public string? TrustedDeviceToken { get; set; }
}

/// <summary>
/// Maps to WildwoodAPI's RegisterRequest DTO.
/// </summary>
public class WildwoodRegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
}

/// <summary>
/// Maps to WildwoodAPI's ForgotPasswordRequest DTO.
/// </summary>
public class WildwoodForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
}

/// <summary>
/// Maps to WildwoodAPI's ResetPasswordRequest DTO.
/// </summary>
public class WildwoodResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
}

/// <summary>
/// Maps to WildwoodAPI's VerifyTwoFactorRequest DTO.
/// </summary>
public class WildwoodTwoFactorVerifyRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool RememberDevice { get; set; }
}

/// <summary>
/// Maps to WildwoodAPI's RefreshTokenRequest DTO.
/// </summary>
public class WildwoodRefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

// ──────────────────────────────────────────────
// WildwoodAPI Auth Response DTOs
// ──────────────────────────────────────────────

/// <summary>
/// Maps to WildwoodAPI's AuthenticateResponse DTO.
/// Field names match the API JSON exactly.
/// </summary>
public class WildwoodAuthenticateResponse
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string JwtToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public bool RequiresTwoFactor { get; set; }
    public bool RequiresPasswordReset { get; set; }
    public bool RequiresDisclaimerAcceptance { get; set; }
    public string? TwoFactorSessionId { get; set; }
    public List<TwoFactorMethodInfo>? AvailableTwoFactorMethods { get; set; }
    public string? DefaultTwoFactorMethod { get; set; }
    public int? TwoFactorSessionExpiresIn { get; set; }
}

/// <summary>
/// Maps to WildwoodAPI's VerifyTwoFactorResponse.
/// </summary>
public class WildwoodTwoFactorResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int? RemainingAttempts { get; set; }
    public string? TrustedDeviceToken { get; set; }
    public WildwoodAuthenticateResponse? AuthResponse { get; set; }
}

// ──────────────────────────────────────────────
// Shared Auth Types
// ──────────────────────────────────────────────

/// <summary>
/// Information about a user's available 2FA method.
/// Used by both WildwoodComponents (Blazor) and WildwoodComponents.Razor.
/// </summary>
public class TwoFactorMethodInfo
{
    public string ProviderType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string? MaskedDestination { get; set; }
}
