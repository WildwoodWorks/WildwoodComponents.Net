using System;
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
    }
}
