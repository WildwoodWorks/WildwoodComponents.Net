using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.WebForms.Session;


namespace WildwoodComponents.WebForms.Attribution
{
    /// <summary>
    /// Campaign Attribution for a WebForms site. Classic ASP.NET has no dependency injection, so this
    /// static façade plays the part the Razor package's <c>&lt;vc:attribution /&gt;</c> plays: one call
    /// starts capture, and <c>WildwoodAuthService.RegisterAsync</c> attaches what was captured.
    /// </summary>
    /// <example>
    /// Capture every request, in <c>Global.asax</c> — the campaign usually lands on a marketing page,
    /// not on the sign-up page, so capturing only where the form lives misses it:
    /// <code>
    /// void Application_AcquireRequestState(object sender, EventArgs e)
    /// {
    ///     WildwoodAttribution.Capture(new HttpContextWrapper(HttpContext.Current));
    /// }
    /// </code>
    /// Session state is needed, which is why this belongs in <c>AcquireRequestState</c> rather than
    /// <c>BeginRequest</c>; a single page can call it from <c>Page_Load</c> instead.
    /// </example>
    /// <remarks>
    /// A site that renders the packaged <c>attribution.js</c> in its master page does not need this: the
    /// browser engine captures the landing and <c>authentication.js</c> posts the payload to the proxy
    /// handler. Both paths end at the same registration field, and an explicitly supplied payload always
    /// wins, so using both is safe.
    /// </remarks>
    public static class WildwoodAttribution
    {
        private static Func<WildwoodAttributionConsent>? _consentDecision;

        /// <summary>
        /// How the host reports the visitor's consent decision for the app's persistence category.
        /// Unset means <see cref="WildwoodAttributionConsent.Undecided"/> — touches are held for the
        /// visit in session state and nothing is written to the visitor's device, which is the JS SDK's
        /// default while a visitor has not answered. Set it to
        /// <see cref="WildwoodAttributionConsent.Denied"/> once the visitor declines or withdraws and the
        /// held touches are dropped.
        /// </summary>
        public static Func<WildwoodAttributionConsent>? ConsentDecision
        {
            get { return _consentDecision; }
            set { _consentDecision = value; }
        }

        /// <summary>
        /// The store bound to the current request's session. Cheap to construct; the touches live in
        /// session state, not on this object.
        /// </summary>
        public static WildwoodAttributionStore Store
        {
            get { return new WildwoodAttributionStore(new HttpSessionTokenStore(), _consentDecision); }
        }

        /// <summary>
        /// Captures the request's URL and referrer as a campaign touch. Returns the touch, null for a
        /// direct visit, and null when there is no request to read. Never throws.
        /// </summary>
        /// <param name="context">The current request.</param>
        /// <param name="options">Capture options; the SDK defaults are used when null.</param>
        public static AttributionTouchModel? Capture(HttpContextBase? context, AttributionCaptureOptions? options = null)
        {
            try
            {
                var request = context?.Request;
                if (request is null)
                {
                    return null;
                }

                var url = request.Url?.AbsoluteUri;
                var referrer = request.UrlReferrer?.AbsoluteUri;
                return Store.Capture(url, referrer, options);
            }
            catch (Exception)
            {
                // Measurement must never cost a page load.
                return null;
            }
        }

        /// <summary>The payload a registration request carries, or null when nothing was captured.</summary>
        public static AttributionPayloadModel? GetForRegistration()
        {
            try
            {
                return Store.GetForRegistration();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Drops the captured touches, after a recorded signup.</summary>
        public static void Clear()
        {
            try
            {
                Store.Clear();
            }
            catch (Exception)
            {
                // Best-effort.
            }
        }

        /// <summary>How long a funnel event may take before it is abandoned. Tracking must never hold up a page.</summary>
        public static readonly TimeSpan EventsTimeout = TimeSpan.FromSeconds(3);

        private static readonly JsonSerializerOptions EventsJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Records a Campaign Attribution funnel event from the server (the counterpart of the browser
        /// engines' <c>track()</c>), fire-and-forget: the request is built from session state now and
        /// sent in the background, so the page never waits on it. Returns whether an event was queued;
        /// false when there is no request, consent was declined, the name is malformed or server-only,
        /// or the app id is not configured. A queued event is still dropped, in the background, when
        /// the app's attribution config does not accept it (see <see cref="GetConfigAsync"/>). Never throws.
        /// </summary>
        /// <example>
        /// <code>
        /// WildwoodAttribution.Track(new HttpContextWrapper(HttpContext.Current), "signup_view");
        /// WildwoodAttribution.Track(context, "plan_selected", label: tierId);
        /// </code>
        /// </example>
        /// <param name="context">The current request; its path becomes the event's page path.</param>
        /// <param name="name">A standard client event (page_view, cta_click, signup_start...) or one of the app's custom names.</param>
        /// <param name="label">Optional label: a CTA name, a plan id, a signup_error category. Never personal data.</param>
        /// <param name="value">Optional number.</param>
        /// <param name="deviceClass"><c>mobile</c>, <c>tablet</c> or <c>desktop</c> when the host knows it; omitted otherwise.</param>
        public static bool Track(
            HttpContextBase? context,
            string name,
            string? label = null,
            double? value = null,
            string? deviceClass = null)
        {
            try
            {
                var body = BuildEventsRequest(context, name, label, value, deviceClass);
                if (body is null)
                {
                    return false;
                }

                // Read everything request-bound above, before anything awaits; the send outlives the request.
                var client = WildwoodWebForms.HttpClient;
                _ = SendIfAcceptedAsync(client, body);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// <see cref="Track"/>, awaited: true when the server accepted the event. Never throws; a timeout
        /// (<see cref="EventsTimeout"/>) or an error answers false.
        /// </summary>
        public static async Task<bool> TrackAsync(
            HttpContextBase? context,
            string name,
            string? label = null,
            double? value = null,
            string? deviceClass = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var body = BuildEventsRequest(context, name, label, value, deviceClass);
                if (body is null)
                {
                    return false;
                }

                return await SendIfAcceptedAsync(WildwoodWebForms.HttpClient, body, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>How long a loaded attribution config is reused before it is fetched again.</summary>
        public static readonly TimeSpan ConfigCacheDuration = TimeSpan.FromMinutes(5);

        private static readonly ConcurrentDictionary<string, CachedConfig> ConfigCache =
            new ConcurrentDictionary<string, CachedConfig>(StringComparer.Ordinal);

        private static readonly JsonSerializerOptions ConfigJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        /// <summary>
        /// Sends the event only when the app's attribution config accepts it — funnel tracking on, the
        /// name allowed, signup steps only while <c>TrackSignupSteps</c> is on — which is
        /// <c>@wildwood/core</c>'s rule: nothing is sent until the config says so. A config that could
        /// not be loaded accepts nothing. What <see cref="Track"/> and <see cref="TrackAsync"/> send
        /// through; <see cref="SendEventsAsync"/> is the unchecked post beneath it. Never throws.
        /// </summary>
        public static async Task<bool> SendIfAcceptedAsync(
            HttpClient client,
            AttributionEventsRequestModel body,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var config = await GetConfigAsync(client, body.AppId, cancellationToken).ConfigureAwait(false);
                foreach (var e in body.Events)
                {
                    if (!AttributionRules.IsFunnelEventAcceptedBy(config, e.Name))
                    {
                        return false;
                    }
                }

                return await SendEventsAsync(client, body, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// The app's public attribution config (<c>GET attribution/config?appId=</c>), cached for
        /// <see cref="ConfigCacheDuration"/>. A failure is never cached, so the next event tries again.
        /// Null when it could not be loaded. Never throws.
        /// </summary>
        public static async Task<AttributionConfigModel?> GetConfigAsync(
            HttpClient client,
            string? appId,
            CancellationToken cancellationToken = default)
        {
            if (client is null || appId is null || appId.Trim().Length == 0)
            {
                return null;
            }

            var key = appId.Trim();
            if (ConfigCache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.LoadedAt < ConfigCacheDuration)
            {
                return cached.Config;
            }

            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(EventsTimeout);
                    var escapedAppId = Uri.EscapeDataString(key);
                    using (var response = await client.GetAsync($"attribution/config?appId={escapedAppId}", timeout.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            return null;
                        }

                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var config = JsonSerializer.Deserialize<AttributionConfigModel>(json, ConfigJsonOptions);
                        if (config is not null)
                        {
                            ConfigCache[key] = new CachedConfig(config, DateTimeOffset.UtcNow);
                        }

                        return config;
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Forgets every cached config, so the next event reloads it. For tests and config changes.</summary>
        public static void ResetConfigCache()
        {
            ConfigCache.Clear();
        }

        private sealed class CachedConfig
        {
            public CachedConfig(AttributionConfigModel config, DateTimeOffset loadedAt)
            {
                Config = config;
                LoadedAt = loadedAt;
            }

            public AttributionConfigModel Config { get; }

            public DateTimeOffset LoadedAt { get; }
        }

        /// <summary>
        /// Posts a funnel event body to <c>attribution/events?appId=</c> on <paramref name="client"/>, whose base
        /// address is the WildwoodAPI root ending in <c>/api/</c>. Answers whether the server accepted it. Never
        /// throws, and gives up after <see cref="EventsTimeout"/>.
        /// </summary>
        public static async Task<bool> SendEventsAsync(
            HttpClient client,
            AttributionEventsRequestModel body,
            CancellationToken cancellationToken = default)
        {
            if (client is null || body is null || string.IsNullOrWhiteSpace(body.AppId))
            {
                return false;
            }

            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(EventsTimeout);

                    // appId rides in the query string too: the server's rate-limit partition reads it.
                    using (var request = new HttpRequestMessage(
                               HttpMethod.Post,
                               "attribution/events?appId=" + Uri.EscapeDataString(body.AppId)))
                    {
                        request.Content = new StringContent(
                            JsonSerializer.Serialize(body, EventsJsonOptions), Encoding.UTF8, "application/json");

                        using (var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false))
                        {
                            return response.IsSuccessStatusCode;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Measurement: a lost event costs nothing.
                return false;
            }
        }

        private static AttributionEventsRequestModel? BuildEventsRequest(
            HttpContextBase? context,
            string name,
            string? label,
            double? value,
            string? deviceClass)
        {
            var request = context?.Request;
            if (request is null)
            {
                return null;
            }

            var appId = WildwoodWebForms.Options.AppId;
            return Store.CreateEventsRequest(appId, name, label, value, request.Url?.AbsolutePath, deviceClass);
        }
    }
}
