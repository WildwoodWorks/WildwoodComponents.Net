namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Centralized session/storage key constants for authentication tokens.
/// Used by WildwoodSessionManager (client-side) and consumable by server-side apps.
/// </summary>
public static class SessionConstants
{
    // --- Client-side storage keys (used by WildwoodSessionManager / Blazor apps) ---

    /// <summary>Key for storing session expiry timestamp in local storage.</summary>
    public const string SessionExpiry = "ww_session_expiry";

    /// <summary>Key for storing serialized AuthenticationResponse in local storage.</summary>
    public const string AuthData = "ww_session_auth";

    // --- Server-side session keys (used by WildwoodAdmin / Razor Pages apps) ---

    /// <summary>Session key for the JWT access token.</summary>
    public const string AccessToken = "ApiAccessToken";

    /// <summary>Session key for the refresh token.</summary>
    public const string RefreshToken = "ApiRefreshToken";

    /// <summary>Session key for the token expiry timestamp.</summary>
    public const string TokenExpiry = "ApiTokenExpiry";

    // --- Impersonation session keys ---

    /// <summary>Session key indicating impersonation is active.</summary>
    public const string IsImpersonating = "Impersonation_Active";

    /// <summary>Session key for the original admin's access token (backup during impersonation).</summary>
    public const string OriginalAccessToken = "Impersonation_OriginalAccessToken";

    /// <summary>Session key for the original admin's refresh token (backup during impersonation).</summary>
    public const string OriginalRefreshToken = "Impersonation_OriginalRefreshToken";

    /// <summary>Session key for the original admin's token expiry (backup during impersonation).</summary>
    public const string OriginalTokenExpiry = "Impersonation_OriginalTokenExpiry";

    /// <summary>Session key for the impersonated user's display name.</summary>
    public const string ImpersonatedUserName = "Impersonation_UserName";

    /// <summary>Session key for the impersonated user's ID.</summary>
    public const string ImpersonatedUserId = "Impersonation_UserId";

    /// <summary>Session key for the impersonation start time (UTC, ISO 8601).</summary>
    public const string ImpersonationStartedAt = "Impersonation_StartedAt";

    /// <summary>Session key for the impersonated user's roles (comma-separated).</summary>
    public const string ImpersonatedUserRoles = "Impersonation_UserRoles";

    /// <summary>Session key for the impersonated user's company ID.</summary>
    public const string ImpersonatedUserCompanyId = "Impersonation_CompanyId";
}
