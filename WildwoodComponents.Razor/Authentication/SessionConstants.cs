using WildwoodComponents.Razor.Services;

namespace WildwoodComponents.Razor.Authentication;

/// <summary>
/// Centralized session key constants for WildwoodAPI authentication tokens.
/// References WildwoodSessionManager's keys to ensure consistency.
/// </summary>
public static class SessionConstants
{
    public const string AccessToken = WildwoodSessionManager.AccessTokenKey;
    public const string RefreshToken = WildwoodSessionManager.RefreshTokenKey;
    public const string TokenExpiry = WildwoodSessionManager.TokenExpiryKey;
}
