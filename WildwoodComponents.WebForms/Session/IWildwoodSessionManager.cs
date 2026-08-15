using System;
using System.Net.Http.Headers;

namespace WildwoodComponents.WebForms.Session
{
    /// <summary>
    /// Holds the WildwoodAPI tokens for the current request. Mirrors the Razor package's
    /// <c>IWildwoodSessionManager</c>, with one deliberate difference: where Razor exposes
    /// <c>ApplyAuthorizationHeader(HttpClient)</c> and mutates the client's default headers,
    /// this returns a header value for the caller to attach to a single
    /// <c>HttpRequestMessage</c>. The package shares one <c>HttpClient</c> across all
    /// requests, so writing a user's bearer token into its default headers would leak that
    /// token into concurrent requests belonging to other users.
    /// </summary>
    public interface IWildwoodSessionManager
    {
        /// <summary>The current JWT access token, or null when not signed in.</summary>
        string? GetAccessToken();

        /// <summary>The current refresh token, or null.</summary>
        string? GetRefreshToken();

        /// <summary>The token expiry as a round-trip ("o") UTC string, or null.</summary>
        string? GetTokenExpiry();

        /// <summary>
        /// The parsed token expiry in UTC, or null when absent or unparseable.
        /// </summary>
        DateTime? GetTokenExpiryUtc();

        /// <summary>
        /// Stores tokens, deriving the expiry from the JWT's <c>exp</c> claim and falling
        /// back to 15 minutes when the token carries no usable claim.
        /// </summary>
        void SetTokens(string accessToken, string? refreshToken);

        /// <summary>Stores tokens with an expiry the caller already knows.</summary>
        void SetTokens(string accessToken, string? refreshToken, DateTime expiryUtc);

        /// <summary>Clears all stored tokens (sign-out).</summary>
        void ClearTokens();

        /// <summary>
        /// Whether a non-expired access token is present. Fails open — an unparseable
        /// expiry counts as valid — matching the Razor implementation, because the API is
        /// the real authority and a false negative would sign a user out needlessly.
        /// </summary>
        bool IsAuthenticated { get; }

        /// <summary>
        /// The <c>Authorization</c> header for the current user, or null when not signed
        /// in. Attach it to one request; never to a shared client's default headers.
        /// </summary>
        AuthenticationHeaderValue? GetAuthorizationHeader();
    }
}
