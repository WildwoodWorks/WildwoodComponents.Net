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
