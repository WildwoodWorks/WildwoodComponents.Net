using System.Threading;
using System.Threading.Tasks;
using WildwoodComponents.WebForms.Models;

namespace WildwoodComponents.WebForms.Services
{
    /// <summary>
    /// Authentication against the WildwoodAPI. Mirrors the Razor package's
    /// <c>IWildwoodAuthService</c> method-for-method and calls the same endpoints.
    /// </summary>
    /// <remarks>
    /// The mutating methods are called by the site's proxy handler, never by a control:
    /// a control only reads configuration during render, so a user's credentials and the
    /// bearer token stay on the server.
    /// </remarks>
    public interface IWildwoodAuthService
    {
        /// <summary>
        /// Signs in. On success without two-factor, the tokens are stored in session; when
        /// the API reports two-factor is required, nothing is stored and the caller must
        /// complete <see cref="VerifyTwoFactorAsync"/> first.
        /// </summary>
        Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

        /// <summary>Registers a new account and stores the resulting tokens.</summary>
        Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Revokes the refresh token server-side and clears the local tokens. The local
        /// clear happens even when the API call fails.
        /// </summary>
        Task LogoutAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Exchanges the stored refresh token for a new access token. Clears the stored
        /// tokens when the exchange fails, since the session cannot be recovered.
        /// </summary>
        Task<AuthResult> RefreshTokenAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts a password reset. The result is deliberately identical whether or not the
        /// address is registered, so it cannot be used to enumerate accounts.
        /// </summary>
        Task<ApiResult> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);

        /// <summary>Completes a password reset using the emailed token.</summary>
        Task<ApiResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Verifies a two-factor code and, on success, stores the tokens that finish the
        /// sign-in.
        /// </summary>
        Task<AuthResult> VerifyTwoFactorAsync(TwoFactorVerifyRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the app's authentication configuration. Never returns null: when the API
        /// is unreachable, permissive defaults are returned so the login form still renders.
        /// </summary>
        Task<AuthConfigResponse> GetAuthConfigAsync(CancellationToken cancellationToken = default);
    }
}
