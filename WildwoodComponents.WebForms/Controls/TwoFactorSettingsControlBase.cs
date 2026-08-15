using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.WebForms.Controls
{
    /// <summary>
    /// Code behind <c>TwoFactorSettingsControl.ascx</c>: authenticator and email
    /// enrolment, active methods, trusted devices and recovery codes for the signed-in
    /// user. The WebForms counterpart of the Razor package's
    /// <c>TwoFactorSettingsViewComponent</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// &lt;%@ Register TagPrefix="ww" TagName="TwoFactorSettings"
    ///              Src="~/Controls/Wildwood/TwoFactorSettingsControl.ascx" %&gt;
    /// ...
    /// &lt;ww:TwoFactorSettings runat="server" /&gt;
    /// </code>
    /// </example>
    /// <remarks>
    /// Unlike the Authentication control this one may appear more than once on a page:
    /// every element id is suffixed with <see cref="ComponentId"/> and the script binds
    /// each root separately.
    /// <para>
    /// All four reads require a signed-in user. When nobody is signed in they return
    /// nothing and the control renders its empty state, which is the correct thing to show.
    /// </para>
    /// </remarks>
    public abstract class TwoFactorSettingsControlBase : WildwoodControlBase
    {
        private string? _componentId;

        /// <summary>
        /// The site's proxy endpoint. Points at the handler the package installs; change
        /// it only if you moved or replaced that handler.
        /// </summary>
        public string ProxyBaseUrl { get; set; } = "~/Handlers/Wildwood/WildwoodTwoFactorProxy.ashx";

        /// <summary>
        /// Suffix that keeps element ids unique when the control appears more than once on
        /// a page. Generated per render, as the Razor component does.
        /// </summary>
        protected string ComponentId
        {
            get
            {
                return _componentId ??
                       (_componentId = Guid.NewGuid().ToString("N").Substring(0, 8));
            }
        }

        /// <summary>The user's overall two-factor state, or null when unavailable.</summary>
        protected TwoFactorUserStatus? Status { get; private set; }

        /// <summary>Enrolled credentials; never null.</summary>
        protected List<TwoFactorCredential> Credentials { get; private set; } = new List<TwoFactorCredential>();

        /// <summary>Trusted devices; never null.</summary>
        protected List<TrustedDevice> TrustedDevices { get; private set; } = new List<TrustedDevice>();

        /// <summary>Recovery code counts, or null when unavailable.</summary>
        protected RecoveryCodeInfo? RecoveryCodeInfo { get; private set; }

        /// <summary>Whether two-factor authentication is currently switched on.</summary>
        protected bool IsEnabled
        {
            get { return Status != null && Status.IsEnabled; }
        }

        /// <summary>Whether the app requires two-factor authentication.</summary>
        protected bool IsRequired
        {
            get { return Status != null && Status.IsRequired; }
        }

        /// <summary>How many methods the user has configured.</summary>
        protected int MethodCount
        {
            get { return Status == null ? 0 : Status.MethodCount; }
        }

        /// <summary>The proxy URL as the browser should call it, without a trailing slash.</summary>
        protected string ResolvedProxyUrl
        {
            get { return ResolvePath(ProxyBaseUrl).TrimEnd('/'); }
        }

        /// <inheritdoc />
        protected override void OnInit(EventArgs e)
        {
            base.OnInit(e);
            WarnIfInsideUpdatePanel();

            QueueDataFetch(async () =>
            {
                var service = WildwoodWebForms.TwoFactorSettings;

                // Sequential rather than concurrent: each call reads the bearer token from
                // the same request's session, and ASP.NET session state is not built for
                // parallel access from one request.
                Status = await service.GetStatusAsync().ConfigureAwait(false);
                Credentials = await service.GetCredentialsAsync().ConfigureAwait(false);
                TrustedDevices = await service.GetTrustedDevicesAsync().ConfigureAwait(false);
                RecoveryCodeInfo = await service.GetRecoveryCodeInfoAsync().ConfigureAwait(false);
            });
        }

        /// <inheritdoc />
        protected override void OnPreRender(EventArgs e)
        {
            base.OnPreRender(e);
            RegisterComponentAssets("twofactor-settings");
        }

        /// <summary>Suffixes an element id with this instance's component id.</summary>
        /// <param name="prefix">Id stem, e.g. <c>ww-2fa-message-</c>.</param>
        protected string Id(string prefix)
        {
            return prefix + ComponentId;
        }

        /// <summary>Bootstrap Icons class for a credential's provider type.</summary>
        /// <param name="providerType">Provider type as the API reported it.</param>
        protected static string CredentialIcon(string? providerType)
        {
            switch ((providerType ?? string.Empty).ToLowerInvariant())
            {
                case "authenticator":
                case "totp":
                    return "bi-phone";
                case "email":
                    return "bi-envelope";
                default:
                    return "bi-shield-lock";
            }
        }

        /// <summary>The name to show for a credential, falling back through the options.</summary>
        /// <param name="credential">The credential to describe.</param>
        protected static string CredentialName(TwoFactorCredential credential)
        {
            if (credential == null)
            {
                return "—";
            }

            if (!string.IsNullOrEmpty(credential.DisplayName))
            {
                return credential.DisplayName!;
            }

            if (!string.IsNullOrEmpty(credential.MaskedEmail))
            {
                return credential.MaskedEmail!;
            }

            return "—";
        }

        /// <summary>Short date for a table cell, or null when there is no date.</summary>
        /// <param name="value">The timestamp to format.</param>
        protected static string? ShortDate(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("MMM d, yyyy", CultureInfo.CurrentCulture) : null;
        }

        /// <summary>Full date and time for a tooltip, or null when there is no date.</summary>
        /// <param name="value">The timestamp to format.</param>
        protected static string? LongDate(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("g", CultureInfo.CurrentCulture) : null;
        }
    }
}
