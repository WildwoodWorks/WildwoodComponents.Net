using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using System.Web.Security;
using WildwoodComponents.WebForms.Logging;
using WildwoodComponents.WebForms.Session;
using WildwoodComponents.WebForms.Utilities;

namespace WildwoodComponents.WebForms.Authentication
{
    /// <summary>
    /// Keeps a copy of the Wildwood tokens inside the Forms Authentication ticket's
    /// <c>UserData</c>, so a signed-in user survives the loss of server-side session state
    /// — an InProc session dropped by an app-pool recycle or memory pressure, or a request
    /// that lands on a different server. This is the WebForms counterpart of the Razor
    /// package's <c>AuthCookieTokenHelper</c>, which stores the same three values in
    /// cookie authentication properties.
    /// </summary>
    /// <remarks>
    /// Session remains the primary store; the ticket is a backup that is read only when
    /// session has no tokens. Unlike ASP.NET Core, classic Forms Authentication does not
    /// chunk an oversized cookie across several — it emits one cookie the browser then
    /// silently refuses — so the payload is size-checked and degraded rather than
    /// allowed to grow past what a browser will keep. See <see cref="MaxUserDataLength"/>.
    /// </remarks>
    public static class WildwoodFormsAuthHelper
    {
        /// <summary>UserData field name for the access token.</summary>
        public const string AccessTokenName = "ww_access_token";

        /// <summary>UserData field name for the refresh token.</summary>
        public const string RefreshTokenName = "ww_refresh_token";

        /// <summary>UserData field name for the token expiry.</summary>
        public const string TokenExpiryName = "ww_token_expiry";

        /// <summary>
        /// Largest <c>UserData</c> payload written into the ticket. Browsers cap a single
        /// cookie at about 4096 bytes, and encryption plus base64 inflates the ticket to
        /// roughly 1.4x its plaintext size on top of the name, path and the rest of the
        /// ticket, so the plaintext budget is held well under that.
        /// </summary>
        public const int MaxUserDataLength = 2400;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Builds the ticket payload. Returns null when even the access token alone would
        /// not fit, in which case the caller simply skips the backup: the user stays signed
        /// in through session state, and only the survive-a-recycle behaviour is lost.
        /// </summary>
        /// <param name="accessToken">The JWT access token.</param>
        /// <param name="refreshToken">The refresh token, if the API issued one.</param>
        /// <param name="expiryUtc">Access token expiry, in UTC.</param>
        /// <param name="logger">Optional; receives a warning when the payload is degraded.</param>
        public static string? CreateUserData(
            string accessToken,
            string? refreshToken,
            DateTime expiryUtc,
            IWildwoodLogger? logger = null)
        {
            if (string.IsNullOrEmpty(accessToken))
            {
                return null;
            }

            // Same normalisation the session manager applies, so the ticket copy and the
            // session copy of one expiry can never drift apart.
            var expiry = UtcTime.Normalize(expiryUtc).ToString("o", CultureInfo.InvariantCulture);

            var full = Serialize(accessToken, refreshToken, expiry);
            if (full.Length <= MaxUserDataLength)
            {
                return full;
            }

            // Drop the refresh token first: without it the session simply cannot be renewed
            // silently after a recycle, which is strictly better than losing the sign-in.
            var withoutRefresh = Serialize(accessToken, null, expiry);
            if (withoutRefresh.Length <= MaxUserDataLength)
            {
                (logger ?? NullWildwoodLogger.Instance).Warn(
                    "Wildwood tokens exceed the Forms Authentication cookie budget; the refresh " +
                    "token was omitted from the ticket. The signed-in user survives a session loss " +
                    "but cannot be refreshed silently afterwards.");
                return withoutRefresh;
            }

            (logger ?? NullWildwoodLogger.Instance).Warn(
                "The Wildwood access token alone exceeds the Forms Authentication cookie budget (" +
                withoutRefresh.Length.ToString(CultureInfo.InvariantCulture) + " > " +
                MaxUserDataLength.ToString(CultureInfo.InvariantCulture) +
                " chars); no token backup was written to the ticket. Sign-in still works through " +
                "session state, but will not survive an app-pool recycle.");
            return null;
        }

        /// <summary>
        /// Issues the Forms Authentication cookie for <paramref name="userName"/> with the
        /// Wildwood tokens embedded, replacing any cookie already on the response.
        /// </summary>
        /// <param name="context">The current request context.</param>
        /// <param name="userName">Value for the ticket's name, e.g. the user's email.</param>
        /// <param name="accessToken">The JWT access token.</param>
        /// <param name="refreshToken">The refresh token, if any.</param>
        /// <param name="expiryUtc">Access token expiry, in UTC.</param>
        /// <param name="createPersistentCookie">Whether the cookie outlives the browser session.</param>
        /// <param name="logger">Optional diagnostics sink.</param>
        public static void IssueAuthCookie(
            HttpContextBase context,
            string userName,
            string accessToken,
            string? refreshToken,
            DateTime expiryUtc,
            bool createPersistentCookie = false,
            IWildwoodLogger? logger = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrEmpty(userName)) throw new ArgumentException("User name must not be empty.", nameof(userName));

            var userData = CreateUserData(accessToken, refreshToken, expiryUtc, logger) ?? string.Empty;

            // FormsAuthenticationTicket dates are local time by convention. The ticket's own
            // lifetime follows the site's <forms timeout>, independently of when the API
            // access token expires — the module refreshes the latter.
            var issued = DateTime.Now;
            var ticket = new FormsAuthenticationTicket(
                2,
                userName,
                issued,
                issued.Add(FormsAuthentication.Timeout),
                createPersistentCookie,
                userData,
                FormsAuthentication.FormsCookiePath);

            var cookie = new HttpCookie(FormsAuthentication.FormsCookieName, FormsAuthentication.Encrypt(ticket))
            {
                HttpOnly = true,
                Path = FormsAuthentication.FormsCookiePath,
                Secure = FormsAuthentication.RequireSSL || context.Request.IsSecureConnection
            };

            if (FormsAuthentication.CookieDomain != null)
            {
                cookie.Domain = FormsAuthentication.CookieDomain;
            }

            if (createPersistentCookie)
            {
                cookie.Expires = ticket.Expiration;
            }

            context.Response.Cookies.Remove(cookie.Name);
            context.Response.Cookies.Add(cookie);
        }

        /// <summary>
        /// Restores tokens from the request's Forms Authentication ticket into session, for
        /// use when session has no tokens but the auth cookie is still valid.
        /// </summary>
        /// <returns>True when tokens were restored.</returns>
        public static bool TryRestoreSessionFromCookie(
            HttpContextBase context,
            IWildwoodSessionManager sessionManager,
            IWildwoodLogger? logger = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (sessionManager == null) throw new ArgumentNullException(nameof(sessionManager));

            var log = logger ?? NullWildwoodLogger.Instance;

            var cookie = context.Request.Cookies[FormsAuthentication.FormsCookieName];
            if (cookie == null || string.IsNullOrEmpty(cookie.Value))
            {
                return false;
            }

            FormsAuthenticationTicket? ticket;
            try
            {
                ticket = FormsAuthentication.Decrypt(cookie.Value);
            }
            catch (HttpException)
            {
                // "Unable to validate data." Decrypt validates before decrypting, so a
                // tampered cookie or one encrypted under a different machine key — the
                // ordinary case in a web farm with unsynchronised keys, and exactly the
                // scenario this backup exists to survive — arrives here rather than as an
                // ArgumentException.
                log.Debug("Forms Authentication ticket failed validation; ignoring it.");
                return false;
            }
            catch (ArgumentException)
            {
                // Malformed value: wrong length, or not valid base64/hex at all.
                log.Debug("Forms Authentication ticket could not be decrypted; ignoring it.");
                return false;
            }

            if (ticket == null || ticket.Expired)
            {
                return false;
            }

            return TryRestoreFromUserData(ticket.UserData, sessionManager, log);
        }

        /// <summary>
        /// Restores tokens from a ticket payload produced by
        /// <see cref="CreateUserData(string, string?, DateTime, IWildwoodLogger?)"/>.
        /// </summary>
        /// <returns>True when an access token and expiry were found and stored.</returns>
        public static bool TryRestoreFromUserData(
            string? userData,
            IWildwoodSessionManager sessionManager,
            IWildwoodLogger? logger = null)
        {
            if (sessionManager == null) throw new ArgumentNullException(nameof(sessionManager));
            if (string.IsNullOrEmpty(userData))
            {
                return false;
            }

            var log = logger ?? NullWildwoodLogger.Instance;

            TicketTokens? tokens;
            try
            {
                tokens = JsonSerializer.Deserialize<TicketTokens>(userData!, JsonOptions);
            }
            catch (JsonException)
            {
                // A ticket written by the host for its own purposes, not by this helper.
                return false;
            }

            if (tokens == null
                || string.IsNullOrEmpty(tokens.AccessToken)
                || string.IsNullOrEmpty(tokens.TokenExpiry))
            {
                return false;
            }

            if (!DateTimeOffset.TryParse(
                    tokens.TokenExpiry,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var expiry))
            {
                return false;
            }

            sessionManager.SetTokens(tokens.AccessToken!, tokens.RefreshToken, expiry.UtcDateTime);
            log.Info("Restored Wildwood tokens from the Forms Authentication ticket into session.");
            return true;
        }

        private static string Serialize(string accessToken, string? refreshToken, string expiry)
        {
            return JsonSerializer.Serialize(
                new TicketTokens
                {
                    AccessToken = accessToken,
                    RefreshToken = string.IsNullOrEmpty(refreshToken) ? null : refreshToken,
                    TokenExpiry = expiry
                },
                JsonOptions);
        }

        /// <summary>Shape of the JSON stored in the ticket's UserData.</summary>
        private sealed class TicketTokens
        {
            [JsonPropertyName(AccessTokenName)]
            public string? AccessToken { get; set; }

            [JsonPropertyName(RefreshTokenName)]
            public string? RefreshToken { get; set; }

            [JsonPropertyName(TokenExpiryName)]
            public string? TokenExpiry { get; set; }
        }
    }
}
