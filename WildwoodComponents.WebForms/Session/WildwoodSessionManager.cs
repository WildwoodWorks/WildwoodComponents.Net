using System;
using System.Globalization;
using System.Net.Http.Headers;
using WildwoodComponents.WebForms.Logging;
using WildwoodComponents.WebForms.Utilities;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.WebForms.Session
{
    /// <summary>
    /// Stores the WildwoodAPI tokens in ASP.NET session state, using the same key names as
    /// the Razor package so both stacks are inspectable the same way.
    /// </summary>
    public sealed class WildwoodSessionManager : IWildwoodSessionManager
    {
        /// <summary>Session key for the JWT access token.</summary>
        public const string AccessTokenKey = "WildwoodAPI_AccessToken";

        /// <summary>Session key for the refresh token.</summary>
        public const string RefreshTokenKey = "WildwoodAPI_RefreshToken";

        /// <summary>Session key for the token expiry, stored round-trip ("o") in UTC.</summary>
        public const string TokenExpiryKey = "WildwoodAPI_TokenExpiry";

        /// <summary>
        /// Assumed lifetime when a token carries no readable <c>exp</c> claim. Short on
        /// purpose: it only decides when the package proactively refreshes, and the API
        /// remains the authority on whether a token is actually still good.
        /// </summary>
        private static readonly TimeSpan FallbackLifetime = TimeSpan.FromMinutes(15);

        private readonly ITokenStore _store;
        private readonly IWildwoodLogger _logger;

        /// <summary>Creates a manager over the given store.</summary>
        public WildwoodSessionManager(ITokenStore store, IWildwoodLogger? logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? NullWildwoodLogger.Instance;
        }

        /// <inheritdoc />
        public string? GetAccessToken()
        {
            return NullIfEmpty(_store.Get(AccessTokenKey));
        }

        /// <inheritdoc />
        public string? GetRefreshToken()
        {
            return NullIfEmpty(_store.Get(RefreshTokenKey));
        }

        /// <inheritdoc />
        public string? GetTokenExpiry()
        {
            return NullIfEmpty(_store.Get(TokenExpiryKey));
        }

        /// <inheritdoc />
        public DateTime? GetTokenExpiryUtc()
        {
            return ParseExpiry(GetTokenExpiry());
        }

        /// <inheritdoc />
        public void SetTokens(string accessToken, string? refreshToken)
        {
            // TokenExpiryParser is the same JWT reader the other .NET components use, so an
            // odd token is interpreted identically everywhere.
            var expiry = TokenExpiryParser.GetJwtExpiration(accessToken)
                         ?? DateTime.UtcNow.Add(FallbackLifetime);

            SetTokens(accessToken, refreshToken, expiry);
        }

        /// <inheritdoc />
        public void SetTokens(string accessToken, string? refreshToken, DateTime expiryUtc)
        {
            if (string.IsNullOrEmpty(accessToken))
            {
                throw new ArgumentException("Access token must not be empty.", nameof(accessToken));
            }

            if (!_store.IsAvailable)
            {
                _logger.Warn(
                    "Cannot store tokens: no ASP.NET session is available for this request. " +
                    "Enable session state (<sessionState mode=\"InProc\" /> or an out-of-process " +
                    "mode) for the Wildwood components to keep a signed-in user.");
                return;
            }

            _store.Set(AccessTokenKey, accessToken);
            _store.Set(TokenExpiryKey, UtcTime.Normalize(expiryUtc).ToString("o", CultureInfo.InvariantCulture));

            if (string.IsNullOrEmpty(refreshToken))
            {
                // A re-login that returns no refresh token must not leave the previous
                // user's refresh token behind.
                _store.Remove(RefreshTokenKey);
            }
            else
            {
                _store.Set(RefreshTokenKey, refreshToken!);
            }

            _logger.Debug("Wildwood tokens stored in session; expiry " + expiryUtc.ToString("o", CultureInfo.InvariantCulture) + ".");
        }

        /// <inheritdoc />
        public void ClearTokens()
        {
            if (!_store.IsAvailable)
            {
                return;
            }

            _store.Remove(AccessTokenKey);
            _store.Remove(RefreshTokenKey);
            _store.Remove(TokenExpiryKey);
            _logger.Debug("Wildwood tokens cleared from session.");
        }

        /// <inheritdoc />
        public bool IsAuthenticated
        {
            get
            {
                if (GetAccessToken() == null)
                {
                    return false;
                }

                var expiry = GetTokenExpiry();
                if (expiry == null)
                {
                    // No expiry tracked: treat as valid, as the Razor package does.
                    return true;
                }

                var parsed = ParseExpiry(expiry);
                if (parsed == null)
                {
                    return true; // Unparseable — fail open.
                }

                return DateTime.UtcNow < parsed.Value;
            }
        }

        /// <inheritdoc />
        public AuthenticationHeaderValue? GetAuthorizationHeader()
        {
            var token = GetAccessToken();
            return token == null ? null : new AuthenticationHeaderValue("Bearer", token);
        }

        /// <summary>
        /// Parses an expiry written by this class or restored from an auth cookie.
        /// <see cref="DateTimeStyles.AdjustToUniversal"/> keeps a value that carries an
        /// offset from being reinterpreted in the server's local time zone.
        /// </summary>
        private static DateTime? ParseExpiry(string? value)
        {
            if (value == null)
            {
                return null;
            }

            if (DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var offset))
            {
                return offset.UtcDateTime;
            }

            if (DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var dateTime))
            {
                return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
            }

            return null;
        }

        private static string? NullIfEmpty(string? value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
