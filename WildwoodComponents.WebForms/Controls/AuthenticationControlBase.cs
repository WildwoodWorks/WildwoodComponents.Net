using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WildwoodComponents.WebForms.Models;

namespace WildwoodComponents.WebForms.Controls
{
    /// <summary>
    /// Code behind <c>AuthenticationControl.ascx</c>: sign-in, registration, password
    /// reset and two-factor entry. The WebForms counterpart of the Razor package's
    /// <c>AuthenticationViewComponent</c>, exposing the same parameters as control
    /// properties.
    /// </summary>
    /// <example>
    /// <code>
    /// &lt;%@ Register TagPrefix="ww" TagName="Authentication"
    ///              Src="~/Controls/Wildwood/AuthenticationControl.ascx" %&gt;
    /// ...
    /// &lt;ww:Authentication runat="server" ReturnUrl="~/Default.aspx" Title="Welcome back" /&gt;
    /// </code>
    /// </example>
    /// <remarks>
    /// Only one instance belongs on a page: the markup carries fixed element ids, the same
    /// constraint the Razor component has.
    /// </remarks>
    public abstract class AuthenticationControlBase : WildwoodControlBase
    {
        private AuthConfigResponse? _config;

        /// <summary>Where the browser goes once signed in. Must be a local path.</summary>
        public string ReturnUrl { get; set; } = "/";

        /// <summary>
        /// The site's proxy endpoint. Points at the handler the package installs; change
        /// it only if you moved or replaced that handler.
        /// </summary>
        public string ProxyBaseUrl { get; set; } = "~/Handlers/Wildwood/WildwoodAuthProxy.ashx";

        /// <summary>
        /// Whether to offer registration. Registration is shown only when this is true AND
        /// the app's own configuration allows it.
        /// </summary>
        public bool AllowRegistration { get; set; } = true;

        /// <summary>Heading on the sign-in card.</summary>
        public string Title { get; set; } = "Welcome";

        /// <summary>Sub-heading on the sign-in card.</summary>
        public string Subtitle { get; set; } = "Sign in to your account";

        /// <summary>
        /// Page that starts a social sign-in. Receives <c>provider</c> and
        /// <c>returnUrl</c> in the query string.
        /// </summary>
        public string ExternalLoginPath { get; set; } = "~/Account/ExternalLogin.aspx";

        /// <summary>The app's authentication configuration, read during initialisation.</summary>
        protected AuthConfigResponse Config
        {
            get { return _config ?? (_config = new AuthConfigResponse { AllowRegistration = true }); }
        }

        /// <summary>
        /// Whether the registration view is rendered: both this control and the app must
        /// permit it.
        /// </summary>
        protected bool EffectiveAllowRegistration
        {
            get { return AllowRegistration && Config.AllowRegistration; }
        }

        /// <summary>Whether the app has two-factor authentication switched on.</summary>
        protected bool EffectiveEnableTwoFactor
        {
            get { return Config.EnableTwoFactor; }
        }

        /// <summary>Social providers to offer, or an empty list.</summary>
        protected List<string> ExternalProviders
        {
            get { return Config.ExternalProviders ?? new List<string>(); }
        }

        /// <summary>The proxy URL as the browser should call it.</summary>
        protected string ResolvedProxyUrl
        {
            get { return ResolvePath(ProxyBaseUrl).TrimEnd('/'); }
        }

        /// <summary>The post-sign-in destination as the browser should use it.</summary>
        protected string ResolvedReturnUrl
        {
            get
            {
                var resolved = ResolvePath(ReturnUrl);
                return string.IsNullOrEmpty(resolved) ? "/" : resolved;
            }
        }

        /// <inheritdoc />
        protected override void OnInit(EventArgs e)
        {
            base.OnInit(e);
            WarnIfInsideUpdatePanel();

            // Read-only, and tolerant: GetAuthConfigAsync answers with permissive defaults
            // when the API cannot be reached, so the sign-in form still renders.
            QueueDataFetch(async () =>
            {
                _config = await WildwoodWebForms.Auth.GetAuthConfigAsync().ConfigureAwait(false);
            });
        }

        /// <inheritdoc />
        protected override void OnPreRender(EventArgs e)
        {
            base.OnPreRender(e);
            RegisterComponentAssets("authentication");
        }

        /// <summary>
        /// Builds the href for one social provider's sign-in link.
        /// </summary>
        /// <param name="provider">Provider name as the API reported it.</param>
        protected string ExternalLoginUrl(string provider)
        {
            var separator = ResolvePath(ExternalLoginPath).IndexOf('?') >= 0 ? "&" : "?";
            return ResolvePath(ExternalLoginPath)
                   + separator
                   + "provider=" + Uri.EscapeDataString(provider ?? string.Empty)
                   + "&returnUrl=" + Uri.EscapeDataString(ResolvedReturnUrl);
        }

        /// <summary>
        /// Bootstrap Icons class for a provider, matching the Razor view's mapping.
        /// </summary>
        /// <param name="provider">Provider name as the API reported it.</param>
        protected static string ExternalProviderIcon(string provider)
        {
            switch ((provider ?? string.Empty).ToLowerInvariant())
            {
                case "google": return "bi-google";
                case "microsoft": return "bi-microsoft";
                case "facebook": return "bi-facebook";
                case "apple": return "bi-apple";
                default: return "bi-box-arrow-in-right";
            }
        }
    }
}
