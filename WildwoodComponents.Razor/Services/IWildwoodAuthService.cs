using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Authentication service for communicating with WildwoodAPI auth endpoints.
/// Razor Pages equivalent of WildwoodComponents.Services.IAuthenticationService.
/// </summary>
public interface IWildwoodAuthService
{
    /// <summary>
    /// Authenticate with username and password
    /// </summary>
    Task<AuthResult> LoginAsync(LoginRequest request);

    /// <summary>
    /// Register a new user account
    /// </summary>
    Task<AuthResult> RegisterAsync(RegisterRequest request);

    /// <summary>
    /// Log out and invalidate the current session
    /// </summary>
    Task LogoutAsync();

    /// <summary>
    /// Refresh the JWT access token using the refresh token
    /// </summary>
    Task<AuthResult> RefreshTokenAsync();

    /// <summary>
    /// Request a password reset email
    /// </summary>
    Task<ApiResult> ForgotPasswordAsync(string email);

    /// <summary>
    /// Reset password with a token
    /// </summary>
    Task<ApiResult> ResetPasswordAsync(ResetPasswordRequest request);

    /// <summary>
    /// Verify a two-factor authentication code
    /// </summary>
    Task<AuthResult> VerifyTwoFactorAsync(TwoFactorVerifyRequest request);

    /// <summary>
    /// Get the authentication configuration for the current app (social providers, registration settings, etc.)
    /// </summary>
    Task<AuthConfigResponse?> GetAuthConfigAsync();

    /// <summary>
    /// Call once the user has enrolled a two-factor method after a sign-in that answered
    /// <see cref="AuthResponse.RequiresTwoFactorEnrollment"/>. Refreshes the held session so the
    /// restricted token is replaced; succeeds only when the new one is no longer restricted, and
    /// then the proxy finishes the app sign-in it withheld. Port of JS
    /// <c>session.completeTwoFactorEnrollment()</c>. Defaults to a failure so existing
    /// implementations keep compiling.
    /// </summary>
    Task<AuthResult> CompleteTwoFactorEnrollmentAsync()
        => Task.FromResult(AuthResult.Failure("Two-factor enrollment completion is not supported."));
}
