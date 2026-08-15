using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.Web.SessionState;
using WildwoodComponents.WebForms.Logging;

namespace WildwoodComponents.WebForms.Handlers
{
    /// <summary>
    /// Base for the package's same-origin proxy handlers. The components' JavaScript never
    /// talks to the WildwoodAPI directly: it posts to a handler in the host site, which
    /// forwards the call server-side. That is what keeps the bearer token out of the
    /// browser.
    /// </summary>
    /// <remarks>
    /// Routing is by <see cref="HttpRequest.PathInfo"/>: with the control's
    /// <c>ProxyBaseUrl</c> pointing at <c>~/Handlers/Wildwood/WildwoodAuthProxy.ashx</c>,
    /// the script's call to <c>proxyUrl + '/login'</c> arrives here with a path info of
    /// <c>/login</c>.
    /// <para>
    /// The Razor package documents these proxies as copy-paste controllers for the host to
    /// write. Here they ship compiled, and the <c>.ashx</c> files in the package's content
    /// simply point at them, so a WebForms site gets working endpoints on install.
    /// </para>
    /// </remarks>
    public abstract class WildwoodProxyHandlerBase : HttpTaskAsyncHandler, IRequiresSessionState
    {
        /// <summary>
        /// Serialisation for everything crossing the wire to the browser. camelCase
        /// because that is what the shipped scripts read.
        /// </summary>
        protected static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions(JsonSerializerDefaults.Web);

        /// <summary>A handler instance is not shared between requests.</summary>
        public override bool IsReusable
        {
            get { return false; }
        }

        /// <summary>Diagnostics sink.</summary>
        protected static IWildwoodLogger Logger
        {
            get { return WildwoodWebForms.Logger; }
        }

        /// <inheritdoc />
        public override Task ProcessRequestAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            return ProcessRequestAsync(new HttpContextWrapper(context));
        }

        /// <summary>
        /// The request pipeline, taking <see cref="HttpContextBase"/> so it can be driven
        /// from a test without an ASP.NET request.
        /// </summary>
        /// <param name="context">The current request.</param>
        public async Task ProcessRequestAsync(HttpContextBase context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            context.Response.ContentType = "application/json";

            // Proxy responses are per-user and must never be cached by a browser or an
            // intermediary.
            context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            context.Response.Cache.SetNoStore();

            var method = context.Request.HttpMethod ?? string.Empty;
            var route = NormalizeRoute(context.Request.PathInfo);

            if (!IsSameOrigin(context.Request))
            {
                // A cross-site caller has no business here: these endpoints act with the
                // signed-in user's authority.
                await WriteErrorAsync(context, 403, "Cross-origin requests are not allowed.").ConfigureAwait(false);
                return;
            }

            if (HasBody(method) && !IsJsonContentType(context.Request.ContentType))
            {
                // Requiring a JSON content type is itself a CSRF defence: an HTML form can
                // only send urlencoded, multipart or plain text, so it cannot forge one of
                // these calls without a CORS preflight the browser will refuse.
                await WriteErrorAsync(context, 415, "Expected Content-Type: application/json.").ConfigureAwait(false);
                return;
            }

            try
            {
                var handled = await TryHandleAsync(context, route, method).ConfigureAwait(false);
                if (!handled)
                {
                    await WriteErrorAsync(context, 404, "No proxy route matches " + method + " " + route + ".").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Wildwood proxy " + method + " " + route + " failed.", ex);
                await WriteErrorAsync(context, 500, "The request could not be completed.").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Handles one route. Return false for an unrecognised route so the base can answer
        /// 404.
        /// </summary>
        /// <param name="context">The current request.</param>
        /// <param name="route">
        /// Path info with its original case preserved and any trailing slash removed, e.g.
        /// <c>/login</c> or <c>/credentials/AbC123/set-primary</c>; never null. Compare
        /// static routes with <see cref="StringComparison.OrdinalIgnoreCase"/> or via
        /// <see cref="RouteEquals"/>, and pull ids out with <see cref="MatchSegment"/> —
        /// ids are opaque and may be case-sensitive, so the route is deliberately not
        /// lower-cased.
        /// </param>
        /// <param name="method">The HTTP method.</param>
        protected abstract Task<bool> TryHandleAsync(HttpContextBase context, string route, string method);

        /// <summary>Case-insensitive comparison of a route against a literal.</summary>
        /// <param name="route">The route as handed to <c>TryHandleAsync</c>.</param>
        /// <param name="literal">The route to match, e.g. <c>/login</c>.</param>
        protected static bool RouteEquals(string route, string literal)
        {
            return string.Equals(route, literal, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extracts an id from a route shaped <c>{prefix}{id}{suffix}</c>. The fixed parts
        /// are matched case-insensitively while the id keeps the case it arrived with,
        /// because the API's ids are opaque strings and nothing promises they are
        /// lower-case. Returns null when the route has a different shape.
        /// </summary>
        /// <param name="route">The route as handed to <c>TryHandleAsync</c>.</param>
        /// <param name="prefix">Literal before the id, e.g. <c>/credentials/</c>.</param>
        /// <param name="suffix">Literal after the id, or null when the id ends the route.</param>
        protected static string? MatchSegment(string route, string prefix, string? suffix)
        {
            if (route == null || prefix == null)
            {
                return null;
            }

            if (route.Length <= prefix.Length
                || string.Compare(route, 0, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                return null;
            }

            var start = prefix.Length;
            int length;

            if (suffix == null)
            {
                length = route.Length - start;
            }
            else
            {
                var suffixStart = route.Length - suffix.Length;
                if (suffixStart <= start
                    || string.Compare(route, suffixStart, suffix, 0, suffix.Length, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    return null;
                }

                length = suffixStart - start;
            }

            if (length <= 0)
            {
                return null;
            }

            var id = route.Substring(start, length);
            if (id.IndexOf('/') >= 0)
            {
                // A further separator means this is a different route, not an id.
                return null;
            }

            return HttpUtility.UrlDecode(id);
        }

        /// <summary>Reads and deserializes the request body.</summary>
        /// <typeparam name="T">Expected body shape.</typeparam>
        /// <param name="context">The current request.</param>
        /// <returns>The deserialized body, or null when it is absent or malformed.</returns>
        protected static async Task<T?> ReadJsonAsync<T>(HttpContextBase context) where T : class
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8, true, 1024, leaveOpen: true))
            {
                body = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(body, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Writes a JSON response.</summary>
        /// <param name="context">The current request.</param>
        /// <param name="payload">Value to serialize.</param>
        /// <param name="statusCode">HTTP status, 200 by default.</param>
        protected static Task WriteJsonAsync(HttpContextBase context, object payload, int statusCode = 200)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            context.Response.Write(JsonSerializer.Serialize(payload, JsonOptions));
            return CompletedTask;
        }

        /// <summary>Writes a JSON error body.</summary>
        /// <param name="context">The current request.</param>
        /// <param name="statusCode">HTTP status to send.</param>
        /// <param name="message">Message for the caller; must not leak internals.</param>
        protected static Task WriteErrorAsync(HttpContextBase context, int statusCode, string message)
        {
            return WriteJsonAsync(context, new ProxyError { Error = message }, statusCode);
        }

        /// <summary>
        /// Whether a redirect target is safe to hand back to the browser. Only paths within
        /// this site qualify: a caller supplies the return URL, so echoing an absolute one
        /// would turn every sign-in into an open redirect.
        /// </summary>
        /// <param name="url">Candidate URL.</param>
        protected static bool IsLocalUrl(string? url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            // Browsers strip tab, CR and LF from a URL anywhere in the string before
            // parsing it, so "/\t/evil.com" reaches the navigation as "//evil.com" — it
            // would pass the protocol-relative test below while still leaving the site.
            // Anything carrying a control character is refused rather than sanitised,
            // because a legitimate return URL never contains one.
            foreach (var c in url!)
            {
                if (c < 0x20 || c == 0x7F)
                {
                    return false;
                }
            }

            // "//evil.com" and "/\evil.com" are protocol-relative and leave the site.
            if (url.Length > 1 && (url[0] == '/') && (url[1] == '/' || url[1] == '\\'))
            {
                return false;
            }

            if (url[0] == '/')
            {
                return true;
            }

            // "~/page" is app-relative and therefore local.
            if (url.Length > 1 && url[0] == '~' && url[1] == '/')
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Picks the redirect target for a completed sign-in: the caller's request when it
        /// is local, otherwise the site root.
        /// </summary>
        /// <param name="requested">The return URL the browser asked for.</param>
        protected static string ResolveReturnUrl(string? requested)
        {
            return IsLocalUrl(requested) ? requested! : "/";
        }

        /// <summary>
        /// Trims a trailing slash off the path info. Case is deliberately preserved: ids
        /// are carried in the route, and lower-casing them here would silently change which
        /// credential or device a call targets.
        /// </summary>
        private static string NormalizeRoute(string? pathInfo)
        {
            if (string.IsNullOrEmpty(pathInfo))
            {
                return "/";
            }

            var route = pathInfo!;
            if (route.Length > 1 && route.EndsWith("/", StringComparison.Ordinal))
            {
                route = route.Substring(0, route.Length - 1);
            }

            return route;
        }

        private static bool HasBody(string method)
        {
            return string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(method, "PATCH", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsJsonContentType(string? contentType)
        {
            return contentType != null
                   && contentType.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Rejects a request whose <c>Origin</c> names a different host. A same-origin
        /// caller often sends no Origin at all, so an absent header is allowed — the JSON
        /// content-type requirement above covers that case.
        /// </summary>
        private static bool IsSameOrigin(HttpRequestBase request)
        {
            var origin = request.Headers["Origin"];
            if (string.IsNullOrEmpty(origin) || string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Uri originUri;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out originUri))
            {
                return false;
            }

            var current = request.Url;
            if (current == null)
            {
                return true;
            }

            return string.Equals(originUri.Host, current.Host, StringComparison.OrdinalIgnoreCase)
                   && originUri.Port == current.Port
                   && string.Equals(originUri.Scheme, current.Scheme, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// net48 has no <c>Task.CompletedTask</c>; this stands in for it.
        /// </summary>
        private static readonly Task CompletedTask = Task.FromResult(0);

        /// <summary>Error body shape.</summary>
        private sealed class ProxyError
        {
            public string? Error { get; set; }
        }
    }
}
