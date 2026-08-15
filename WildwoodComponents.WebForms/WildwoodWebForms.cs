using System;
using System.Net;
using System.Net.Http;
using WildwoodComponents.Shared.Http;
using WildwoodComponents.WebForms.Configuration;
using WildwoodComponents.WebForms.Logging;
using WildwoodComponents.WebForms.Session;

namespace WildwoodComponents.WebForms
{
    /// <summary>
    /// Composition root for the WebForms package. Classic ASP.NET has no dependency
    /// injection container, so this static entry point plays the role
    /// <c>AddWildwoodComponentsRazor</c> plays for the Razor package: it holds the
    /// configuration, owns the single <see cref="System.Net.Http.HttpClient"/>, and hands
    /// out services.
    /// </summary>
    /// <example>
    /// In <c>Global.asax</c>:
    /// <code>
    /// void Application_Start(object sender, EventArgs e)
    /// {
    ///     WildwoodWebForms.Configure();                       // reads web.config appSettings
    ///     // or: WildwoodWebForms.Configure(o => o.AppId = "...");
    /// }
    /// </code>
    /// Calling <c>Configure</c> is optional: the first component to need configuration
    /// loads it from <c>appSettings</c> itself. Configure explicitly when you want a
    /// misconfigured site to fail at startup rather than at first render, or when settings
    /// come from somewhere other than <c>web.config</c>.
    /// </example>
    public static class WildwoodWebForms
    {
        private static readonly object Gate = new object();

        private static WildwoodOptions? _options;
        private static HttpClient? _httpClient;
        private static IWildwoodLogger _logger = new TraceWildwoodLogger();

        /// <summary>
        /// Where the package writes diagnostics. Defaults to
        /// <see cref="TraceWildwoodLogger"/>; assign in <c>Application_Start</c> to route
        /// them into the host's own logging.
        /// </summary>
        public static IWildwoodLogger Logger
        {
            get { return _logger; }
            set { _logger = value ?? NullWildwoodLogger.Instance; }
        }

        /// <summary>
        /// Whether configuration has been established, by an explicit
        /// <c>Configure</c> call or by the lazy load from <c>appSettings</c>.
        /// </summary>
        public static bool IsConfigured
        {
            get { lock (Gate) { return _options != null; } }
        }

        /// <summary>
        /// The active configuration, loading it from <c>web.config</c> on first use.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The configuration is incomplete — most often a missing
        /// <c>WildwoodAPI:BaseUrl</c>.
        /// </exception>
        public static WildwoodOptions Options
        {
            get { return EnsureConfigured().Options; }
        }

        /// <summary>
        /// Reads options from <c>web.config</c> <c>appSettings</c>, applies
        /// <paramref name="configure"/>, and builds the HTTP client. Safe to call once from
        /// <c>Application_Start</c>; a second call replaces the configuration and the
        /// client.
        /// </summary>
        /// <param name="configure">Optional overrides applied after the appSettings load.</param>
        public static void Configure(Action<WildwoodOptions>? configure = null)
        {
            var options = AppSettingsOptionsLoader.Load();
            configure?.Invoke(options);
            Configure(options);
        }

        /// <summary>Configures the package from an options instance built by the host.</summary>
        /// <param name="options">Fully populated options; validated before use.</param>
        public static void Configure(WildwoodOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.Validate();

            var client = BuildHttpClient(options);

            HttpClient? previous;
            lock (Gate)
            {
                previous = _httpClient;
                _options = options;
                _httpClient = client;
            }

            // Disposed outside the lock, and only after it is unreachable. In-flight requests
            // on the old client will fault; that is confined to a reconfiguration, which
            // belongs in Application_Start.
            previous?.Dispose();

            Logger.Info("Wildwood configured for " + options.BaseUrl + ".");
        }

        /// <summary>
        /// Discards the current configuration and client. Intended for tests; a running
        /// site has no reason to call it.
        /// </summary>
        public static void Reset()
        {
            HttpClient? previous;
            lock (Gate)
            {
                previous = _httpClient;
                _options = null;
                _httpClient = null;
            }

            previous?.Dispose();
        }

        /// <summary>
        /// A session manager bound to the current request. Cheap to construct; the tokens
        /// live in ASP.NET session state, not on this object.
        /// </summary>
        public static IWildwoodSessionManager Session
        {
            get { return new WildwoodSessionManager(new HttpSessionTokenStore(), Logger); }
        }

        /// <summary>
        /// The shared client. Internal because callers must not attach per-user state to
        /// it — services set the <c>Authorization</c> header on each request instead.
        /// </summary>
        internal static HttpClient HttpClient
        {
            get { return EnsureConfigured().Client; }
        }

        private static (WildwoodOptions Options, HttpClient Client) EnsureConfigured()
        {
            // Monitor is re-entrant, so the Configure call below re-takes this lock on the
            // same thread. Holding it across the lazy load is what stops two concurrent
            // first-requests from each building a client and disposing the other's.
            lock (Gate)
            {
                if (_options == null || _httpClient == null)
                {
                    // Nothing configured yet: fall back to web.config so a site that only
                    // drops controls onto a page works without touching Global.asax.
                    Configure(AppSettingsOptionsLoader.Load());
                }

                return (_options!, _httpClient!);
            }
        }

        private static HttpClient BuildHttpClient(WildwoodOptions options)
        {
            EnsureModernTls();

            var primary = new HttpClientHandler();
            if (options.EnableDetailedErrors)
            {
                // Lets the package be pointed at a developer's local API over an untrusted
                // certificate. Scoped to loopback: any other host is validated normally,
                // whatever this flag says.
                primary.ServerCertificateCustomValidationCallback =
                    (request, certificate, chain, errors) =>
                        errors == System.Net.Security.SslPolicyErrors.None
                        || (request?.RequestUri != null && request.RequestUri.IsLoopback);
            }

            HttpMessageHandler pipeline = primary;
            if (options.EnableRetry)
            {
                pipeline = new WildwoodRetryHandler(options.MaxRetryAttempts)
                {
                    InnerHandler = primary
                };
            }

            // A trailing slash keeps relative service paths ("auth/login") from replacing
            // the last segment of the base URL.
            var baseUrl = options.BaseUrl.TrimEnd('/') + "/";

            var client = new HttpClient(pipeline, disposeHandler: true)
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds)
            };

            if (!string.IsNullOrEmpty(options.ApiKey))
            {
                // App-wide and identical for every caller, so unlike a bearer token this is
                // safe to keep on the shared client.
                client.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
            }

            return client;
        }

        /// <summary>
        /// .NET Framework 4.7 and later negotiate TLS from the OS configuration, which
        /// already covers TLS 1.2 and 1.3. Only a host that pinned an explicit, older set
        /// — common in sites that hard-coded <c>Tls | Tls11</c> years ago — is corrected,
        /// and then only by adding 1.2 rather than replacing what it chose.
        /// </summary>
        private static void EnsureModernTls()
        {
            var configured = ServicePointManager.SecurityProtocol;
            if (configured == 0)
            {
                return; // SystemDefault
            }

            if ((configured & SecurityProtocolType.Tls12) == 0)
            {
                ServicePointManager.SecurityProtocol = configured | SecurityProtocolType.Tls12;
            }
        }
    }
}
