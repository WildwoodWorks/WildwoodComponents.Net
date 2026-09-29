using System.Collections.Generic;

namespace WildwoodComponents.Shared.Models
{
    /// <summary>
    /// One campaign touch, as captured by the attribution engine (mirrors @wildwood/core AttributionTouch).
    /// Clients send host and path only; WildwoodAPI re-normalizes every value.
    /// </summary>
    public class AttributionTouchModel
    {
        /// <summary><c>utm_source</c>, or the referrer host for a referral touch.</summary>
        public string? Source { get; set; }

        /// <summary><c>utm_medium</c>, or <c>referral</c>.</summary>
        public string? Medium { get; set; }

        public string? Campaign { get; set; }
        public string? Term { get; set; }
        public string? Content { get; set; }

        /// <summary>Click-id parameter name, e.g. <c>gclid</c> or <c>rdt_cid</c>.</summary>
        public string? ClickIdName { get; set; }

        public string? ClickIdValue { get; set; }
        public string? ReferrerHost { get; set; }
        public string? LandingHost { get; set; }
        public string? LandingPath { get; set; }

        /// <summary>Values of the app's allowlisted extra query parameters.</summary>
        public Dictionary<string, string>? ExtraParams { get; set; }

        /// <summary>ISO-8601 capture time.</summary>
        public string OccurredAt { get; set; } = string.Empty;
    }

    /// <summary>
    /// The attribution payload a registration request carries (the server's AttributionInput), mirroring
    /// @wildwood/core AttributionPayload.
    /// </summary>
    public class AttributionPayloadModel
    {
        public int Version { get; set; } = 1;
        public string VisitorKey { get; set; } = string.Empty;
        public AttributionTouchModel? FirstTouch { get; set; }
        public AttributionTouchModel? LastTouch { get; set; }

        /// <summary><c>web</c>, <c>ios</c>, <c>android</c>, or <c>unknown</c>.</summary>
        public string Platform { get; set; } = "web";

        /// <summary><c>js</c>, <c>dotnet</c>, or <c>swift</c>.</summary>
        public string Sdk { get; set; } = "dotnet";

        /// <summary>
        /// The funnel session the registration happened in, joining the account to that session's funnel
        /// events. Optional: null when the engine keeps no session.
        /// </summary>
        public string? SessionKey { get; set; }

        /// <summary><c>mobile</c>, <c>tablet</c> or <c>desktop</c>; null when unknown.</summary>
        public string? DeviceClass { get; set; }

        /// <summary>Sessions this visitor has started, counting the current one; null when unknown.</summary>
        public int? SessionCount { get; set; }
    }

    /// <summary>
    /// The body of POST /api/attribution/claim: the captured payload plus the app id, which must match the
    /// <c>appId</c> query parameter. Used for sign-in provider signups, which have no registration request to
    /// carry the payload (mirrors @wildwood/core claimAttribution).
    /// </summary>
    public class AttributionClaimRequestModel : AttributionPayloadModel
    {
        public string AppId { get; set; } = string.Empty;

        /// <summary>Copies a captured payload into a claim for <paramref name="appId"/>.</summary>
        public static AttributionClaimRequestModel From(string appId, AttributionPayloadModel payload)
        {
            return new AttributionClaimRequestModel
            {
                AppId = appId,
                Version = payload.Version,
                VisitorKey = payload.VisitorKey,
                FirstTouch = payload.FirstTouch,
                LastTouch = payload.LastTouch,
                Platform = payload.Platform,
                Sdk = payload.Sdk,
                SessionKey = payload.SessionKey,
                DeviceClass = payload.DeviceClass,
                SessionCount = payload.SessionCount
            };
        }
    }

    /// <summary>The response of POST /api/attribution/claim.</summary>
    public class AttributionClaimResultModel
    {
        public bool Recorded { get; set; }

        /// <summary>
        /// Why nothing was recorded: <c>Disabled</c>, <c>WindowExpired</c>, <c>AlreadyRecorded</c>, <c>Empty</c> or
        /// <c>NotAppUser</c>. Null when recorded.
        /// </summary>
        public string? Reason { get; set; }
    }

    /// <summary>The public attribution config returned by GET /api/attribution/config.</summary>
    public class AttributionConfigModel
    {
        public string AppId { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public bool CaptureFirstTouch { get; set; } = true;
        public bool CaptureLastTouch { get; set; } = true;
        public int AttributionWindowDays { get; set; } = 30;

        /// <summary>Consent category that must be granted before touches are persisted (a category name).</summary>
        public string PersistenceConsentCategory { get; set; } = "Analytics";

        public bool CaptureClickIds { get; set; } = true;
        public bool CaptureReferrer { get; set; } = true;
        public List<string> ExtraAllowedParamNames { get; set; } = new();
        public bool BeaconEnabled { get; set; }

        /// <summary>Funnel event tracking. Always false when <see cref="IsEnabled"/> is false.</summary>
        public bool FunnelTrackingEnabled { get; set; }

        /// <summary>Auto-track <c>scroll_depth</c> milestones (25/50/75/100) per page.</summary>
        public bool TrackScrollDepth { get; set; }

        /// <summary>Auto-track <c>engaged</c> (once per session) and <c>time_on_page</c>.</summary>
        public bool TrackEngagement { get; set; }

        /// <summary>Auto-track clicks on elements carrying <c>data-ww-cta</c> as <c>cta_click</c>.</summary>
        public bool AutoTrackCtaClicks { get; set; }

        /// <summary>Accept the <c>signup_view</c>, <c>signup_start</c>, <c>signup_submit</c> and <c>signup_error</c> steps.</summary>
        public bool TrackSignupSteps { get; set; }

        /// <summary>Extra event names (<c>^[a-z0-9_]{1,40}$</c>) the app allows on top of the standard client events.</summary>
        public List<string> CustomEventNames { get; set; } = new();

        /// <summary>
        /// Before consent, mirror the touch, visitor key and session key to sessionStorage
        /// (<c>ww_attribution_session</c>) so a reload in the same tab keeps them.
        /// </summary>
        public bool SessionStoragePersistenceBeforeConsent { get; set; }
    }

    /// <summary>One funnel event inside <see cref="AttributionEventsRequestModel"/> (mirrors @wildwood/core FunnelEvent).</summary>
    public class AttributionFunnelEventModel
    {
        /// <summary>A standard client event or one of the app's custom names (<c>^[a-z0-9_]{1,40}$</c>).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>CTA name, plan, error category. At most 100 characters.</summary>
        public string? Label { get; set; }

        public double? Value { get; set; }

        /// <summary>The page path, never a query string. At most 500 characters.</summary>
        public string? Path { get; set; }

        /// <summary>ISO-8601 time the event happened on the client.</summary>
        public string? ClientTimestamp { get; set; }
    }

    /// <summary>
    /// The body of POST /api/attribution/events?appId= (anonymous; at most 25 events), mirroring
    /// @wildwood/core AttributionEventsRequest. <see cref="Touch"/> is the visitor's current last touch, or
    /// null for a direct visit.
    /// </summary>
    public class AttributionEventsRequestModel
    {
        public string AppId { get; set; } = string.Empty;
        public string VisitorKey { get; set; } = string.Empty;
        public string SessionKey { get; set; } = string.Empty;
        public bool IsReturning { get; set; }

        /// <summary><c>mobile</c>, <c>tablet</c> or <c>desktop</c>; null (omitted) when unknown.</summary>
        public string? DeviceClass { get; set; }

        public string Platform { get; set; } = "web";
        public AttributionTouchModel? Touch { get; set; }
        public List<AttributionFunnelEventModel> Events { get; set; } = new();
    }

    /// <summary>The attribution engine's current state (deserialized from JS interop).</summary>
    public class AttributionStateModel
    {
        public string VisitorKey { get; set; } = string.Empty;
        public AttributionTouchModel? First { get; set; }
        public AttributionTouchModel? Last { get; set; }

        /// <summary>True while the touches are held in browser storage (the consent category allowed it).</summary>
        public bool Persisted { get; set; }

        public AttributionConfigModel? Config { get; set; }
    }
}
