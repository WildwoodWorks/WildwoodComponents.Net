using System.Collections.Generic;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.WebForms.Models
{
    // The request/response types below mirror WildwoodComponents.Razor\Models\AuthenticationModels.cs
    // field-for-field. They are deliberately separate from the Shared project's
    // WildwoodAuthModels types: these are the simplified shapes the browser exchanges with
    // the site's own proxy handler, while the Shared types map to WildwoodAPI's full
    // endpoint contracts. Validation attributes are omitted — the proxy handlers validate
    // explicitly rather than pulling System.ComponentModel.DataAnnotations into the package.

    /// <summary>Login request as posted by the component's JavaScript to the site's proxy.</summary>
    public class LoginRequest
    {
        /// <summary>Username or email. Required.</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>Plain-text password. Required.</summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>Whether the sign-in should outlive the browser session.</summary>
        public bool RememberMe { get; set; }
    }

    /// <summary>Registration request as posted to the site's proxy.</summary>
    public class RegisterRequest
    {
        /// <summary>Email address. Required.</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>Unique username; falls back to <see cref="Email"/> when absent.</summary>
        public string? Username { get; set; }

        /// <summary>Plain-text password. Required, minimum eight characters.</summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>Password confirmation. Required, must equal <see cref="Password"/>.</summary>
        public string ConfirmPassword { get; set; } = string.Empty;

        /// <summary>Optional given name.</summary>
        public string? FirstName { get; set; }

        /// <summary>Optional family name.</summary>
        public string? LastName { get; set; }

        /// <summary>Invitation token, for apps that gate registration.</summary>
        public string? RegistrationToken { get; set; }
    }

    /// <summary>Password reset initiation request.</summary>
    public class ForgotPasswordRequest
    {
        /// <summary>Email address to send the reset link to. Required.</summary>
        public string Email { get; set; } = string.Empty;
    }

    /// <summary>Password reset completion request.</summary>
    public class ResetPasswordRequest
    {
        /// <summary>Reset token from the emailed link. Required.</summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>Email address the reset was requested for. Required.</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>The new password. Required, minimum eight characters.</summary>
        public string NewPassword { get; set; } = string.Empty;

        /// <summary>Confirmation, must equal <see cref="NewPassword"/>.</summary>
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    /// <summary>Two-factor verification request posted after a 2FA-required login.</summary>
    public class TwoFactorVerifyRequest
    {
        /// <summary>The one-time code. Required.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Session id carried over from the login response.</summary>
        public string? SessionId { get; set; }

        /// <summary>Which 2FA method produced the code.</summary>
        public string? Method { get; set; }

        /// <summary>Whether to trust this device and skip 2FA next time.</summary>
        public bool RememberDevice { get; set; }
    }

    /// <summary>Normalised authentication response handed back to the browser.</summary>
    public class AuthResponse
    {
        /// <summary>The JWT access token.</summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>The refresh token, when the API issued one.</summary>
        public string? RefreshToken { get; set; }

        /// <summary>The authenticated user's id.</summary>
        public string? UserId { get; set; }

        /// <summary>The authenticated user's email.</summary>
        public string? Email { get; set; }

        /// <summary>Full name when known, otherwise the email.</summary>
        public string? DisplayName { get; set; }

        /// <summary>Role names, when the API returns them.</summary>
        public List<string>? Roles { get; set; }

        /// <summary>True when the credentials were right but 2FA is still outstanding.</summary>
        public bool RequiresTwoFactor { get; set; }

        /// <summary>True when the account still needs its email confirmed.</summary>
        public bool RequiresEmailConfirmation { get; set; }

        /// <summary>True when the user must change their password before continuing.</summary>
        public bool RequiresPasswordReset { get; set; }

        /// <summary>True when the user must accept a new disclaimer version.</summary>
        public bool RequiresDisclaimerAcceptance { get; set; }

        /// <summary>Session id to pass to the 2FA verification call.</summary>
        public string? TwoFactorSessionId { get; set; }

        /// <summary>Methods available when <see cref="RequiresTwoFactor"/> is true.</summary>
        public List<TwoFactorMethodInfo>? AvailableTwoFactorMethods { get; set; }

        /// <summary>The user's preferred 2FA method, when 2FA is required.</summary>
        public string? DefaultTwoFactorMethod { get; set; }

        /// <summary>Seconds until the 2FA session expires.</summary>
        public int? TwoFactorSessionExpiresIn { get; set; }

        /// <summary>Projects WildwoodAPI's response onto this shape.</summary>
        internal static AuthResponse FromWildwoodResponse(WildwoodAuthenticateResponse ww)
        {
            var displayName = (ww.FirstName + " " + ww.LastName).Trim();
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

    /// <summary>What the login screen needs to know before it renders.</summary>
    public class AuthConfigResponse
    {
        /// <summary>Whether self-registration is open.</summary>
        public bool AllowRegistration { get; set; }

        /// <summary>Whether registration requires an invitation token.</summary>
        public bool RequireRegistrationToken { get; set; }

        /// <summary>Whether a new account must confirm its email.</summary>
        public bool RequireEmailConfirmation { get; set; }

        /// <summary>Configured social sign-in providers.</summary>
        public List<string> ExternalProviders { get; set; } = new List<string>();

        /// <summary>Whether two-factor authentication is enabled for the app.</summary>
        public bool EnableTwoFactor { get; set; }

        /// <summary>Enabled two-factor method names.</summary>
        public List<string> TwoFactorMethods { get; set; } = new List<string>();
    }

    /// <summary>Outcome of an authentication operation.</summary>
    public class AuthResult
    {
        /// <summary>Whether the operation succeeded.</summary>
        public bool Succeeded { get; set; }

        /// <summary>Why it failed, when it did.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Whether the flow is waiting on two-factor verification.</summary>
        public bool RequiresTwoFactor { get; set; }

        /// <summary>The authentication payload on success.</summary>
        public AuthResponse? Response { get; set; }

        /// <summary>A successful result carrying <paramref name="response"/>.</summary>
        public static AuthResult Success(AuthResponse response)
        {
            return new AuthResult
            {
                Succeeded = true,
                Response = response,
                RequiresTwoFactor = response.RequiresTwoFactor
            };
        }

        /// <summary>A failed result carrying <paramref name="message"/>.</summary>
        public static AuthResult Failure(string message, bool requiresTwoFactor = false)
        {
            return new AuthResult
            {
                Succeeded = false,
                ErrorMessage = message,
                RequiresTwoFactor = requiresTwoFactor
            };
        }
    }

    /// <summary>Outcome of an operation with no payload beyond a message.</summary>
    public class ApiResult
    {
        /// <summary>Whether the operation succeeded.</summary>
        public bool Succeeded { get; set; }

        /// <summary>A message suitable for display.</summary>
        public string? Message { get; set; }

        /// <summary>A successful result.</summary>
        public static ApiResult Ok(string? message = null)
        {
            return new ApiResult { Succeeded = true, Message = message };
        }

        /// <summary>A failed result.</summary>
        public static ApiResult Fail(string message)
        {
            return new ApiResult { Succeeded = false, Message = message };
        }
    }

    /// <summary>WildwoodAPI's error body.</summary>
    public class ApiErrorResponse
    {
        /// <summary>Human-readable failure message.</summary>
        public string? Message { get; set; }

        /// <summary>Machine-readable error code.</summary>
        public string? ErrorCode { get; set; }

        /// <summary>Whether the failure was "two-factor required".</summary>
        public bool RequiresTwoFactor { get; set; }

        /// <summary>Per-field validation messages.</summary>
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}
