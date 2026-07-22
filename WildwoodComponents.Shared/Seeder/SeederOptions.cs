using System;
using System.Net.Http;

namespace WildwoodComponents.Shared.Seeder
{
    /// <summary>
    /// Configuration for the in-app Seeder runner. Supplied by the consuming app
    /// (typically bound from <c>Wildwood:Seeder</c> in appsettings plus environment
    /// variable overrides). The seeder authenticates to WildwoodAPI as a CompanyAdmin
    /// service account (or with a pre-issued bearer token) to seed data and record
    /// the ledger/history.
    /// </summary>
    public sealed class SeederOptions
    {
        /// <summary>WildwoodAPI base URL, e.g. https://localhost:5291. Determines which environment's backend is seeded.</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>The app id being seeded.</summary>
        public string AppId { get; set; } = string.Empty;

        /// <summary>Optional X-API-Key sent with requests (only a few admin routes require it).</summary>
        public string? ApiKey { get; set; }

        /// <summary>CompanyAdmin service-account email/username used to log in.</summary>
        public string? AdminEmail { get; set; }

        /// <summary>CompanyAdmin service-account password.</summary>
        public string? AdminPassword { get; set; }

        /// <summary>
        /// Pre-issued admin JWT used INSTEAD of the email/password login when set (it expires — the
        /// seeder cannot refresh it). Lets token-only environments seed without service-account creds.
        /// </summary>
        public string? BearerToken { get; set; }

        /// <summary>
        /// Optional factory for the HTTP primary handler, for apps with special outbound needs
        /// (e.g. an IPv4-preferred connect callback on nodes with broken IPv6). When set, the
        /// default handler — including its loopback dev-cert bypass — is not used.
        /// </summary>
        public Func<HttpMessageHandler>? PrimaryHandlerFactory { get; set; }

        /// <summary>AppId used for the login call (defaults to <see cref="AppId"/>).</summary>
        public string? LoginAppId { get; set; }

        /// <summary>Environment label recorded in the ledger/history (e.g. "Dev", "Production"). Defaults to "Default".</summary>
        public string Environment { get; set; } = "Default";

        /// <summary>Local hard gate: when false, the runner performs no seeding regardless of server config.</summary>
        public bool RunOnStartup { get; set; } = true;

        /// <summary>When true, no writes are performed (tasks log intended writes only).</summary>
        public bool DryRun { get; set; }

        /// <summary>Absolute path to a resources directory tasks can read (templates, prompts). Optional.</summary>
        public string ResourcesPath { get; set; } = string.Empty;

        /// <summary>Delay before the runner starts, to let the app finish coming up. Default 3s.</summary>
        public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>Fallback for StopOnFirstFailure when no server config row exists yet.</summary>
        public bool StopOnFirstFailureDefault { get; set; } = true;

        /// <summary>Fallback max attempts per task when no server config row exists yet.</summary>
        public int MaxAttemptsDefault { get; set; } = 5;

        /// <summary>Fallback retry delay (seconds) when no server config row exists yet.</summary>
        public int RetryDelaySecondsDefault { get; set; } = 20;

        public string EffectiveLoginAppId => string.IsNullOrWhiteSpace(LoginAppId) ? AppId : LoginAppId!;
        public bool HasCredentials =>
            !string.IsNullOrWhiteSpace(BearerToken)
            || (!string.IsNullOrWhiteSpace(AdminEmail) && !string.IsNullOrWhiteSpace(AdminPassword));
    }
}
