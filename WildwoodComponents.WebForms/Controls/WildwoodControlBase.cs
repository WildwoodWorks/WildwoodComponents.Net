using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Web;
using System.Web.UI;
using System.Web.UI.HtmlControls;
using WildwoodComponents.WebForms.Logging;

namespace WildwoodComponents.WebForms.Controls
{
    /// <summary>
    /// Base for the package's user controls. Provides the two things every Wildwood
    /// control needs from the WebForms page lifecycle: a way to fetch data during render
    /// without deadlocking, and asset registration that emits each stylesheet and script
    /// once per page however many controls ask for it.
    /// </summary>
    /// <remarks>
    /// A control only ever READS from the API while rendering — configuration, the current
    /// user's state — exactly as the Razor ViewComponents do. Everything that changes
    /// state goes through the browser to a proxy handler, so no user credential or bearer
    /// token passes through the page.
    /// </remarks>
    public abstract class WildwoodControlBase : UserControl
    {
        /// <summary>
        /// Virtual folder the package's scripts are copied into by the NuGet install.
        /// Override when the site keeps them elsewhere.
        /// </summary>
        public string ScriptPath { get; set; } = "~/Scripts/wildwood";

        /// <summary>
        /// Virtual folder the package's stylesheets are copied into by the NuGet install.
        /// </summary>
        public string StylePath { get; set; } = "~/Content/wildwood";

        /// <summary>
        /// Set false to suppress the control's own &lt;link&gt; and &lt;script&gt; tags when
        /// the site bundles the assets itself.
        /// </summary>
        public bool RegisterAssets { get; set; } = true;

        /// <summary>Diagnostics sink.</summary>
        protected static IWildwoodLogger Logger
        {
            get { return WildwoodWebForms.Logger; }
        }

        /// <summary>
        /// Runs an API read during the page lifecycle.
        /// </summary>
        /// <remarks>
        /// On a page with <c>Async="true"</c> the work is registered with the page and
        /// awaited by the framework at the right moment. Legacy pages rarely set that, so
        /// otherwise the task runs on the thread pool and is waited on. Handing it to
        /// <see cref="Task.Run(Func{Task})"/> detaches it from the ASP.NET
        /// synchronisation context, which is what stops the classic sync-over-async
        /// deadlock; the package's own code also uses <c>ConfigureAwait(false)</c>
        /// throughout so neither half of that pairing stands alone.
        /// </remarks>
        /// <param name="fetch">The read to perform. Exceptions are logged, not thrown.</param>
        protected void QueueDataFetch(Func<Task> fetch)
        {
            if (fetch == null) throw new ArgumentNullException(nameof(fetch));

            if (Page != null && Page.IsAsync)
            {
                Page.RegisterAsyncTask(new PageAsyncTask(() => RunGuardedAsync(fetch)));
                return;
            }

            try
            {
                Task.Run(() => RunGuardedAsync(fetch)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // RunGuardedAsync already swallows the component's own failures; anything
                // arriving here is a scheduling fault, and it must not take the page down.
                Logger.Error("Wildwood could not run a data fetch for " + GetType().Name + ".", ex);
            }
        }

        private async Task RunGuardedAsync(Func<Task> fetch)
        {
            try
            {
                await fetch().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A component that cannot reach the API renders its empty state, the same
                // way the Razor ViewComponents degrade.
                Logger.Error("Wildwood data fetch failed for " + GetType().Name + "; rendering defaults.", ex);
            }
        }

        /// <summary>
        /// Registers a stylesheet and a script for this control, plus the shared theme and
        /// form shim they depend on. Safe to call from several controls on one page: each
        /// file is emitted once.
        /// </summary>
        /// <param name="name">Base file name without extension, e.g. <c>authentication</c>.</param>
        protected void RegisterComponentAssets(string name)
        {
            if (!RegisterAssets)
            {
                return;
            }

            RegisterStyle("wildwood-razor-themes");
            RegisterStyle(name);

            // The shim must be in place before a component script runs, since the script
            // asks for it as it initialises.
            RegisterScript("wildwood-forms");
            RegisterScript(name);
        }

        /// <summary>Emits one stylesheet link, at most once per page.</summary>
        /// <param name="name">Base file name without extension.</param>
        protected void RegisterStyle(string name)
        {
            var key = "ww-css-" + name;
            if (Page == null || Page.Items.Contains(key))
            {
                return;
            }

            Page.Items[key] = true;
            var href = ResolveUrl(StylePath.TrimEnd('/') + "/" + name + ".css");

            if (Page.Header != null)
            {
                var link = new HtmlLink { Href = href };
                link.Attributes["rel"] = "stylesheet";
                link.Attributes["type"] = "text/css";
                Page.Header.Controls.Add(link);
                return;
            }

            // No <head runat="server"> to add to — a very common shape in older sites.
            // A link element in the body is not valid HTML but every browser honours it,
            // and it beats rendering the component unstyled.
            Page.ClientScript.RegisterClientScriptBlock(
                typeof(WildwoodControlBase),
                key,
                "<link rel=\"stylesheet\" type=\"text/css\" href=\"" + HttpUtility.HtmlAttributeEncode(href) + "\" />",
                false);
        }

        /// <summary>
        /// Emits one script tag, at most once per page, at the end of the form so the
        /// markup it operates on already exists.
        /// </summary>
        /// <param name="name">Base file name without extension.</param>
        protected void RegisterScript(string name)
        {
            var key = "ww-js-" + name;
            if (Page == null || Page.Items.Contains(key))
            {
                return;
            }

            Page.Items[key] = true;
            var src = ResolveUrl(ScriptPath.TrimEnd('/') + "/" + name + ".js");

            Page.ClientScript.RegisterStartupScript(
                typeof(WildwoodControlBase),
                key,
                "<script src=\"" + HttpUtility.HtmlAttributeEncode(src) + "\"></script>",
                false);
        }

        /// <summary>
        /// Renders a boolean as the lower-case literal the components' scripts read from a
        /// <c>data-</c> attribute.
        /// </summary>
        /// <param name="value">The value to render.</param>
        protected static string Attr(bool value)
        {
            return value ? "true" : "false";
        }

        /// <summary>
        /// HTML-encodes a value for use in an attribute, treating null as empty.
        /// </summary>
        /// <param name="value">The value to encode.</param>
        protected static string Attr(string? value)
        {
            return HttpUtility.HtmlAttributeEncode(value ?? string.Empty);
        }

        /// <summary>HTML-encodes text for element content, treating null as empty.</summary>
        /// <param name="value">The value to encode.</param>
        protected static string Text(string? value)
        {
            return HttpUtility.HtmlEncode(value ?? string.Empty);
        }

        /// <summary>
        /// Resolves a path that may be application-relative into one the browser can
        /// request. Absolute and rooted paths are returned unchanged.
        /// </summary>
        /// <param name="path">The path to resolve.</param>
        protected string ResolvePath(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            if (path!.StartsWith("~", StringComparison.Ordinal))
            {
                return ResolveUrl(path);
            }

            return path;
        }

        /// <summary>
        /// Warns once per page when the control sits inside an UpdatePanel, which this
        /// version does not support: a partial postback re-renders the markup but does not
        /// re-run the component's script, leaving the new DOM unbound.
        /// </summary>
        protected void WarnIfInsideUpdatePanel()
        {
            for (var parent = Parent; parent != null; parent = parent.Parent)
            {
                if (!IsUpdatePanel(parent.GetType()))
                {
                    continue;
                }

                Logger.Warn(
                    GetType().Name + " is inside an UpdatePanel. Wildwood controls do not " +
                    "support partial postbacks: after one, the re-rendered markup has no " +
                    "event handlers attached. Place the control outside the UpdatePanel.");
                return;
            }
        }

        /// <summary>
        /// Whether <paramref name="type"/> is an UpdatePanel, tested by name up the
        /// inheritance chain. Matching on the name keeps this package from referencing
        /// System.Web.Extensions for what is only a diagnostic.
        /// </summary>
        private static bool IsUpdatePanel(Type? type)
        {
            for (; type != null; type = type.BaseType)
            {
                if (string.Equals(type.FullName, "System.Web.UI.UpdatePanel", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds a <c>data-</c> attribute string from name/value pairs, skipping empties.
        /// Values are attribute-encoded.
        /// </summary>
        /// <param name="attributes">Attribute names (without the <c>data-</c> prefix) and values.</param>
        protected static string DataAttributes(IEnumerable<KeyValuePair<string, string?>> attributes)
        {
            if (attributes == null)
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder();
            foreach (var pair in attributes)
            {
                if (string.IsNullOrEmpty(pair.Key))
                {
                    continue;
                }

                builder.Append(' ')
                       .Append("data-")
                       .Append(pair.Key)
                       .Append("=\"")
                       .Append(HttpUtility.HtmlAttributeEncode(pair.Value ?? string.Empty))
                       .Append('"');
            }

            return builder.ToString();
        }

        /// <summary>Formats a number for a data attribute, culture-invariantly.</summary>
        /// <param name="value">The value to format.</param>
        protected static string Attr(decimal value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
