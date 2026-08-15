using System;
using System.Collections.Specialized;
using System.Configuration;

namespace WildwoodComponents.WebForms.Configuration
{
    /// <summary>
    /// Builds a <see cref="WildwoodOptions"/> from <c>web.config</c> <c>appSettings</c>.
    /// Keys carry the <c>WildwoodAPI:</c> prefix so they read the same as the Razor
    /// package's <c>WildwoodAPI</c> configuration section:
    /// <code>
    /// &lt;add key="WildwoodAPI:BaseUrl" value="https://api.wildwoodworks.io/api" /&gt;
    /// &lt;add key="WildwoodAPI:ApiKey" value="..." /&gt;
    /// &lt;add key="WildwoodAPI:AppId"  value="..." /&gt;
    /// </code>
    /// Absent keys leave the corresponding default in place; a malformed numeric or
    /// boolean value is treated as absent rather than throwing, matching how the Razor
    /// package's <c>TryParse</c>-based binder behaves.
    /// </summary>
    public static class AppSettingsOptionsLoader
    {
        /// <summary>Prefix every recognised appSettings key carries.</summary>
        public const string KeyPrefix = "WildwoodAPI:";

        /// <summary>Loads options from <see cref="ConfigurationManager.AppSettings"/>.</summary>
        public static WildwoodOptions Load()
        {
            return Load(ConfigurationManager.AppSettings);
        }

        /// <summary>
        /// Loads options from an arbitrary settings collection. The collection is a
        /// parameter rather than read directly so this is unit-testable without an
        /// <c>app.config</c>.
        /// </summary>
        /// <param name="settings">Settings to read; a null collection yields defaults.</param>
        public static WildwoodOptions Load(NameValueCollection? settings)
        {
            var options = new WildwoodOptions();
            if (settings == null)
            {
                return options;
            }

            var baseUrl = Get(settings, "BaseUrl");
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                options.BaseUrl = baseUrl!.Trim();
            }

            var apiKey = Get(settings, "ApiKey");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                options.ApiKey = apiKey!.Trim();
            }

            var appId = Get(settings, "AppId");
            if (!string.IsNullOrWhiteSpace(appId))
            {
                options.AppId = appId!.Trim();
            }

            var appVersion = Get(settings, "AppVersion");
            if (!string.IsNullOrWhiteSpace(appVersion))
            {
                options.AppVersion = appVersion!.Trim();
            }

            var loginPath = Get(settings, "LoginPath");
            if (!string.IsNullOrWhiteSpace(loginPath))
            {
                options.LoginPath = loginPath!.Trim();
            }

            if (bool.TryParse(Get(settings, "EnableDetailedErrors"), out var enableDetailedErrors))
            {
                options.EnableDetailedErrors = enableDetailedErrors;
            }

            if (int.TryParse(Get(settings, "RequestTimeoutSeconds"), out var timeoutSeconds))
            {
                options.RequestTimeoutSeconds = timeoutSeconds;
            }

            if (bool.TryParse(Get(settings, "EnableRetry"), out var enableRetry))
            {
                options.EnableRetry = enableRetry;
            }

            if (int.TryParse(Get(settings, "MaxRetryAttempts"), out var maxRetryAttempts))
            {
                options.MaxRetryAttempts = maxRetryAttempts;
            }

            if (bool.TryParse(Get(settings, "DisableTokenModule"), out var disableTokenModule))
            {
                options.DisableTokenModule = disableTokenModule;
            }

            return options;
        }

        private static string? Get(NameValueCollection settings, string name)
        {
            return settings[KeyPrefix + name];
        }
    }
}
