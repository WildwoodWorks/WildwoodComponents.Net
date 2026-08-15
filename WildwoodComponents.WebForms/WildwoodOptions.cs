using System;

namespace WildwoodComponents.WebForms
{
    /// <summary>
    /// Configuration for the WebForms component package. Mirrors the Razor package's
    /// <c>WildwoodComponentsRazorOptions</c> field-for-field so the two stacks are
    /// configured with the same names and defaults; classic WebForms has no
    /// <c>IConfiguration</c>, so values come from <c>web.config</c> <c>appSettings</c>
    /// (see <see cref="Configuration.AppSettingsOptionsLoader"/>) and may then be
    /// overridden in code from <c>Application_Start</c>.
    /// </summary>
    public class WildwoodOptions
    {
        /// <summary>
        /// Base URL of the WildwoodAPI, e.g. <c>https://api.wildwoodworks.io/api</c>.
        /// Services issue relative paths against this, so a trailing slash is added
        /// when the client is built.
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>App API key, sent as the <c>X-API-Key</c> header on every request.</summary>
        public string? ApiKey { get; set; }

        /// <summary>The Wildwood app id these components belong to.</summary>
        public string? AppId { get; set; }

        /// <summary>
        /// When true, self-signed certificates are accepted for localhost so the package
        /// can be pointed at a developer's local API. Never relaxes validation for any
        /// other host.
        /// </summary>
        public bool EnableDetailedErrors { get; set; } = true;

        /// <summary>HTTP timeout applied to every WildwoodAPI call.</summary>
        public int RequestTimeoutSeconds { get; set; } = 30;

        /// <summary>Whether transient 5xx/network failures are retried on idempotent verbs.</summary>
        public bool EnableRetry { get; set; } = true;

        /// <summary>Maximum attempts including the first.</summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>Reported to the API for diagnostics.</summary>
        public string AppVersion { get; set; } = "1.0.0";

        /// <summary>
        /// Set true to keep <c>WildwoodTokenExpirationModule</c> registered but inert.
        /// Exists so a site can rule the module out while diagnosing a request-pipeline
        /// problem without editing <c>&lt;modules&gt;</c> and recycling the app pool.
        /// </summary>
        public bool DisableTokenModule { get; set; }

        /// <summary>
        /// Where the token module sends a browser navigation whose session has expired.
        /// AJAX requests get a 401 JSON body carrying this instead of a redirect.
        /// </summary>
        public string LoginPath { get; set; } = "~/Account/Login.aspx";

        /// <summary>
        /// Throws when required values are missing. Called by <c>WildwoodWebForms.Configure</c>
        /// so a misconfigured site fails at startup with a precise message rather than on the
        /// first component render with a null-reference or an unhelpful 404.
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(BaseUrl))
            {
                throw new InvalidOperationException(
                    "Wildwood: BaseUrl is not configured. Add <add key=\"WildwoodAPI:BaseUrl\" " +
                    "value=\"https://api.wildwoodworks.io/api\" /> to <appSettings> in web.config, " +
                    "or set it in WildwoodWebForms.Configure(o => o.BaseUrl = ...).");
            }

            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    "Wildwood: BaseUrl must be an absolute http(s) URL, but was '" + BaseUrl + "'.");
            }

            if (RequestTimeoutSeconds <= 0)
            {
                throw new InvalidOperationException(
                    "Wildwood: RequestTimeoutSeconds must be greater than zero.");
            }

            if (MaxRetryAttempts < 1)
            {
                throw new InvalidOperationException(
                    "Wildwood: MaxRetryAttempts must be at least 1.");
            }
        }
    }
}
