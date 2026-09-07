namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Manages JWT tokens and session state for server-side WildwoodAPI communication.
/// Tokens are stored in IHttpContextAccessor session/cookies, not browser local storage.
/// </summary>
public interface IWildwoodSessionManager
{
    /// <summary>
    /// Gets the current JWT access token
    /// </summary>
    string? GetAccessToken();

    /// <summary>
    /// Gets the current refresh token
    /// </summary>
    string? GetRefreshToken();

    /// <summary>
    /// Stores authentication tokens from a successful login.
    /// Expiry is extracted from the JWT token automatically.
    /// </summary>
    void SetTokens(string accessToken, string refreshToken);

    /// <summary>
    /// Stores authentication tokens with an explicit expiry time.
    /// Use this overload when the expiry is already known to avoid re-parsing the JWT.
    /// </summary>
    void SetTokens(string accessToken, string refreshToken, DateTime expiryUtc);

    /// <summary>
    /// Gets the token expiry as an ISO 8601 string, or null if not set
    /// </summary>
    string? GetTokenExpiry();

    /// <summary>
    /// Clears all stored tokens (logout)
    /// </summary>
    void ClearTokens();

    /// <summary>
    /// Whether the user has a valid (non-expired) access token
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Applies the current access token to an HttpClient's Authorization header
    /// </summary>
    void ApplyAuthorizationHeader(HttpClient httpClient);

    /// <summary>
    /// Whether the signed-in user still has to replace a temporary password. Recorded from the
    /// login / two-factor response because the refresh-token response never carries it, so a
    /// refresh must not clear a pending forced reset. Defaults to false so existing
    /// implementations keep compiling.
    /// </summary>
    bool RequiresPasswordReset => false;

    /// <summary>Records (true) or clears (false) the pending forced-reset flag.</summary>
    void SetRequiresPasswordReset(bool value) { }
}
