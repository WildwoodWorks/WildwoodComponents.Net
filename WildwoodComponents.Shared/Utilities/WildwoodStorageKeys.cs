namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Centralized browser localStorage key names used by the Wildwood SDK components.
/// These MUST stay in sync with the JS SDK (@wildwood/core) for cross-stack parity:
/// authService STORAGE_KEYS (ww_accessToken / ww_refreshToken / ww_user),
/// themeService THEME_STORAGE_KEY (ww_theme), messagingService DRAFT_STORAGE_PREFIX (ww_draft_).
/// </summary>
public static class WildwoodStorageKeys
{
    /// <summary>JWT access token.</summary>
    public const string AccessToken = "ww_accessToken";

    /// <summary>Refresh token.</summary>
    public const string RefreshToken = "ww_refreshToken";

    /// <summary>Serialized authenticated user / AuthenticationResponse.</summary>
    public const string User = "ww_user";

    /// <summary>Theme preference.</summary>
    public const string Theme = "ww_theme";

    /// <summary>Prefix for per-thread message drafts.</summary>
    public const string DraftPrefix = "ww_draft_";

    /// <summary>The draft key for a specific thread.</summary>
    public static string Draft(string threadId) => DraftPrefix + threadId;

    /// <summary>
    /// Pre-<c>ww_</c>-prefix key names. Retained ONLY for one-time migration-on-read so an
    /// SDK upgrade does not log users out / lose theme &amp; drafts. Do not write to these.
    /// </summary>
    public static class Legacy
    {
        public const string AccessToken = "accessToken";
        public const string RefreshToken = "refreshToken";
        public const string User = "user";
        public const string Theme = "wildwood-theme";
        public const string DraftPrefix = "draft_";

        public static string Draft(string threadId) => DraftPrefix + threadId;
    }
}
