using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace WildwoodComponents.Razor.Authentication;

/// <summary>
/// Helper for storing and restoring WildwoodAPI tokens in authentication cookie properties.
/// Provides a resilient backup when session data is lost (e.g., in-memory cache
/// eviction, app restart, or session race conditions after re-login).
/// Ported from WildwoodAdmin — critical for surviving session loss.
/// </summary>
public static class AuthCookieTokenHelper
{
    public const string AccessTokenName = "ww_access_token";
    public const string RefreshTokenName = "ww_refresh_token";
    public const string TokenExpiryName = "ww_token_expiry";

    /// <summary>
    /// Creates a list of AuthenticationToken objects for storing in auth cookie properties.
    /// </summary>
    public static List<AuthenticationToken> CreateTokenList(string jwtToken, string? refreshToken, DateTime validToUtc)
    {
        var tokens = new List<AuthenticationToken>
        {
            new() { Name = AccessTokenName, Value = jwtToken },
            new() { Name = TokenExpiryName, Value = validToUtc.ToString("o") }
        };

        if (!string.IsNullOrEmpty(refreshToken))
        {
            tokens.Add(new AuthenticationToken { Name = RefreshTokenName, Value = refreshToken });
        }

        return tokens;
    }

    /// <summary>
    /// Restores WildwoodAPI tokens from auth cookie properties into the session.
    /// Call this when session tokens are missing but the auth cookie is still valid.
    /// Returns true if tokens were restored.
    /// </summary>
    public static bool TryRestoreSessionFromCookie(
        AuthenticationProperties? properties,
        ISession session,
        ILogger? logger = null)
    {
        if (properties == null)
            return false;

        var accessToken = properties.GetTokenValue(AccessTokenName);
        var tokenExpiry = properties.GetTokenValue(TokenExpiryName);

        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(tokenExpiry))
            return false;

        session.SetString(SessionConstants.AccessToken, accessToken);
        session.SetString(SessionConstants.TokenExpiry, tokenExpiry);

        var refreshToken = properties.GetTokenValue(RefreshTokenName);
        if (!string.IsNullOrEmpty(refreshToken))
        {
            session.SetString(SessionConstants.RefreshToken, refreshToken);
        }

        logger?.LogInformation("Restored WildwoodAPI tokens from auth cookie into session");
        return true;
    }
}
