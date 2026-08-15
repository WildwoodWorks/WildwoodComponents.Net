using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using System.Web.Security;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.WebForms.Authentication;
using WildwoodComponents.WebForms.Logging;
using WildwoodComponents.WebForms.Services;
using WildwoodComponents.WebForms.Session;

namespace WildwoodComponents.WebForms.Modules
{
    /// <summary>
    /// Keeps the WildwoodAPI session alive across page navigations, and ends it cleanly
    /// when it cannot be kept. The WebForms counterpart of the Razor package's
    /// <c>WildwoodTokenExpirationMiddleware</c>.
    /// </summary>
    /// <remarks>
    /// Registered in <c>web.config</c> by the package's <c>web.config.transform</c>:
    /// <code>
    /// &lt;system.webServer&gt;
    ///   &lt;modules&gt;
    ///     &lt;add name="WildwoodTokenExpiration"
    ///          type="WildwoodComponents.WebForms.Modules.WildwoodTokenExpirationModule, WildwoodComponents.WebForms" /&gt;
    ///   &lt;/modules&gt;
    /// &lt;/system.webServer&gt;
    /// </code>
    /// That is the Integrated-pipeline section, which IIS has defaulted to since version 7.
    /// The Classic-mode equivalent (<c>system.web/httpModules</c>) is left for the host to
    /// add by hand, because declaring it on an Integrated pool stops the application
    /// starting at all — see the package readme.
    /// <para>
    /// It runs at <c>PostAcquireRequestState</c>, the first point where both the
    /// authenticated user and session state are available.
    /// </para>
    /// <para>
    /// Every failure path lets the request through. A site whose API is briefly unreachable
    /// keeps serving pages; only a token that is known to be expired AND whose refresh was
    /// explicitly refused ends the session.
    /// </para>
    /// </remarks>
    public class WildwoodTokenExpirationModule : IHttpModule
    {
        /// <summary>
        /// How close to expiry a token is refreshed pre-emptively, so a user browsing
        /// normally never reaches an expired token.
        /// </summary>
        private static readonly TimeSpan ProactiveRefreshWindow = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Paths that never carry a Wildwood session and so are not worth a check. Matched
        /// case-insensitively with StartsWith against the app-relative path.
        /// </summary>
        private static readonly string[] DefaultPublicPaths =
        {
            "/account/login",
            "/account/logout",
            "/account/accessdenied",
            "/error",
            "/privacy",
            "/css",
            "/js",
            "/scripts",
            "/content",
            "/lib",
            "/images",
            "/favicon",
            "/app_themes",
            "/webresource.axd",
            "/scriptresource.axd"
        };

        private static readonly string[] StaticExtensions =
        {
            ".css", ".js", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico",
            ".woff", ".woff2", ".ttf", ".eot", ".map", ".axd"
        };

        /// <summary>
        /// Set when configuration turns out to be unusable, so an unconfigured site is not
        /// charged an exception on every request. Reset only by an app-pool recycle, which
        /// is also what applies a corrected <c>web.config</c>.
        /// </summary>
        private static volatile bool _inert;

        private static volatile string[]? _additionalPublicPaths;

        /// <summary>
        /// Extra paths to skip, beyond <see cref="DefaultPublicPaths"/>. Assign from
        /// <c>Application_Start</c>; matched the same way. Volatile so the assignment is
        /// visible to the request threads that read it.
        /// </summary>
        public static string[]? AdditionalPublicPaths
        {
            get { return _additionalPublicPaths; }
            set { _additionalPublicPaths = value; }
        }

        /// <inheritdoc />
        public void Init(HttpApplication context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // The token refresh is a network call, so the handler must be asynchronous;
            // EventHandlerTaskAsyncHelper is how a Task-based handler attaches to the
            // classic Begin/End pipeline events.
            var helper = new EventHandlerTaskAsyncHelper(OnPostAcquireRequestStateAsync);
            context.AddOnPostAcquireRequestStateAsync(helper.BeginEventHandler, helper.EndEventHandler);
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        private async Task OnPostAcquireRequestStateAsync(object sender, EventArgs e)
        {
            if (_inert)
            {
                return;
            }

            var application = sender as HttpApplication;
            if (application == null)
            {
                return;
            }

            var context = new HttpContextWrapper(application.Context);

            try
            {
                await ProcessAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A fault here must never cost the host a page.
                WildwoodWebForms.Logger.Warn(
                    "Wildwood token check failed; allowing the request to continue.", ex);
            }
        }

        /// <summary>
        /// The check itself, taking <see cref="HttpContextBase"/> so it can be exercised
        /// without an ASP.NET request.
        /// </summary>
        internal static async Task ProcessAsync(HttpContextBase context)
        {
            var user = context.User;
            if (user == null || user.Identity == null || !user.Identity.IsAuthenticated)
            {
                return; // Nothing to keep alive.
            }

            var path = context.Request.AppRelativeCurrentExecutionFilePath ?? string.Empty;
            if (path.StartsWith("~", StringComparison.Ordinal))
            {
                path = path.Substring(1);
            }

            if (IsPublicPath(path) || IsStaticFile(path))
            {
                return;
            }

            IWildwoodLogger logger;
            IWildwoodSessionManager sessionManager;
            IWildwoodAuthService authService;
            string loginPath;

            try
            {
                logger = WildwoodWebForms.Logger;
                if (WildwoodWebForms.Options.DisableTokenModule)
                {
                    _inert = true;
                    logger.Info("Wildwood token module disabled by WildwoodAPI:DisableTokenModule.");
                    return;
                }

                sessionManager = WildwoodWebForms.Session;
                authService = WildwoodWebForms.Auth;
                loginPath = WildwoodWebForms.Options.LoginPath;
            }
            catch (InvalidOperationException ex)
            {
                // The site has the module registered but no usable configuration. Say so
                // once, then stand down rather than repeating this on every request.
                _inert = true;
                WildwoodWebForms.Logger.Warn(
                    "Wildwood token module is standing down: the package is not configured. " +
                    "Add WildwoodAPI:BaseUrl to <appSettings>, or remove the module from " +
                    "<system.webServer><modules>.", ex);
                return;
            }

            // Session state can be lost while the auth cookie survives — an app-pool
            // recycle, or a request served by another server. Rehydrate before concluding
            // anything about the tokens.
            if (sessionManager.GetAccessToken() == null)
            {
                WildwoodFormsAuthHelper.TryRestoreSessionFromCookie(context, sessionManager, logger);
            }

            var accessToken = sessionManager.GetAccessToken();
            var expiry = sessionManager.GetTokenExpiry();

            if (accessToken == null || expiry == null)
            {
                // Authenticated by the host's own means, with no Wildwood session to manage.
                return;
            }

            var expired = false;
            if (TokenExpiryParser.IsExpired(expiry))
            {
                logger.Info("Wildwood token expired; attempting refresh.");
                try
                {
                    var refreshed = await authService.RefreshTokenAsync().ConfigureAwait(false);
                    expired = !refreshed.Succeeded || !sessionManager.IsAuthenticated;
                }
                catch (Exception ex)
                {
                    // The refresh could not be attempted, which says nothing about whether
                    // the session is really over. Let the request through; individual API
                    // calls will surface their own auth failures.
                    logger.Warn("Wildwood token refresh threw; allowing the request to continue.", ex);
                    return;
                }
            }
            else
            {
                await TryProactiveRefreshAsync(expiry, authService, logger).ConfigureAwait(false);
                return;
            }

            if (!expired)
            {
                return;
            }

            await EndSessionAsync(context, authService, sessionManager, logger, loginPath).ConfigureAwait(false);
        }

        /// <summary>
        /// Refreshes a still-valid token that is close to expiring, so the session does not
        /// lapse mid-visit. Failures are swallowed: the token is still good right now.
        /// </summary>
        private static async Task TryProactiveRefreshAsync(
            string expiry,
            IWildwoodAuthService authService,
            IWildwoodLogger logger)
        {
            try
            {
                DateTime expiryUtc;
                if (!TokenExpiryParser.TryParseUtc(expiry, out expiryUtc))
                {
                    return;
                }

                var remaining = expiryUtc - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero && remaining <= ProactiveRefreshWindow)
                {
                    logger.Debug("Wildwood token expires in " + remaining.TotalMinutes.ToString("F1") +
                                 " minutes; refreshing pre-emptively.");
                    await authService.RefreshTokenAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.Debug("Wildwood proactive refresh failed; will retry on the next request. " + ex.Message);
            }
        }

        /// <summary>
        /// Signs the user out locally and tells the browser, as a 401 for a background
        /// request or a redirect for a navigation.
        /// </summary>
        private static async Task EndSessionAsync(
            HttpContextBase context,
            IWildwoodAuthService authService,
            IWildwoodSessionManager sessionManager,
            IWildwoodLogger logger,
            string loginPath)
        {
            logger.Info("Wildwood session expired and could not be refreshed; signing out.");

            try
            {
                await authService.LogoutAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Debug("Revoking the refresh token during sign-out failed. " + ex.Message);
            }

            sessionManager.ClearTokens();
            FormsAuthentication.SignOut();

            var resolvedLogin = ResolveLoginUrl(context, loginPath);

            if (IsAjaxRequest(context.Request))
            {
                // A background fetch must not be answered with a login page: the caller's
                // JavaScript needs a status it can act on.
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                context.Response.Write(JsonSerializer.Serialize(new SessionExpiredPayload
                {
                    Error = "Session expired",
                    Redirect = resolvedLogin
                }));

                // Deliberately NOT Response.End(): that unwinds by throwing
                // ThreadAbortException, which this module's own top-level catch would
                // intercept and log as a failure before the runtime re-raised it. Ending
                // the request the same way the redirect branch does keeps the normal
                // sign-out path free of exceptions.
                context.Response.Flush();
                context.ApplicationInstance.CompleteRequest();
                return;
            }

            var returnUrl = context.Request.Url == null
                ? "/"
                : context.Request.Url.PathAndQuery;

            var separator = resolvedLogin.IndexOf('?') >= 0 ? "&" : "?";
            context.Response.Redirect(
                resolvedLogin + separator + "returnUrl=" + Uri.EscapeDataString(returnUrl),
                endResponse: false);
            context.ApplicationInstance.CompleteRequest();
        }

        /// <summary>
        /// Turns the configured login path into one the browser can use, resolving a
        /// <c>~/</c> application-relative path against the site root.
        /// </summary>
        private static string ResolveLoginUrl(HttpContextBase context, string loginPath)
        {
            if (string.IsNullOrEmpty(loginPath))
            {
                return FormsAuthentication.LoginUrl;
            }

            if (!loginPath.StartsWith("~", StringComparison.Ordinal))
            {
                return loginPath;
            }

            try
            {
                return VirtualPathUtility.ToAbsolute(loginPath, context.Request.ApplicationPath);
            }
            catch (ArgumentException)
            {
                return FormsAuthentication.LoginUrl;
            }
        }

        /// <summary>
        /// Whether the caller is a script rather than a navigating browser. Mirrors the
        /// Razor package's <c>RequestHelper.IsAjaxRequest</c>.
        /// </summary>
        private static bool IsAjaxRequest(HttpRequestBase request)
        {
            if (string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var accept = request.Headers["Accept"];
            if (accept != null && accept.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private static bool IsPublicPath(string path)
        {
            if (Matches(path, DefaultPublicPaths))
            {
                return true;
            }

            var additional = AdditionalPublicPaths;
            return additional != null && Matches(path, additional);
        }

        private static bool Matches(string path, IEnumerable<string> prefixes)
        {
            foreach (var prefix in prefixes)
            {
                if (!string.IsNullOrEmpty(prefix)
                    && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsStaticFile(string path)
        {
            foreach (var extension in StaticExtensions)
            {
                if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Body of the 401 answer to a background request. Same shape the Razor middleware
        /// writes, so client code can treat both stacks identically.
        /// </summary>
        private sealed class SessionExpiredPayload
        {
            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("redirect")]
            public string? Redirect { get; set; }
        }
    }
}
