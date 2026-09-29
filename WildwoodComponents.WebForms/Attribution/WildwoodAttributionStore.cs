using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.WebForms.Session;

namespace WildwoodComponents.WebForms.Attribution
{
    /// <summary>
    /// Server-side Campaign Attribution capture for a WebForms site: reads the landing URL and referrer
    /// of a request, keeps a first and a last touch for the visit, and hands the payload to
    /// registration. The counterpart of the browser engines' <c>window.wildwoodAttribution</c>, for
    /// hosts that render their own sign-up rather than the packaged control.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Touches live in ASP.NET session state under the SDK's <c>ww_attribution</c> key — the same key
    /// the browser engines use in localStorage, because it is the same data. The session is the
    /// server-side analogue of the SDK's in-memory state: it rides the site's existing session cookie,
    /// writes nothing new to the visitor's device, and dies with the visit.
    /// </para>
    /// <para>
    /// Nothing here throws into the page: a failed capture costs a campaign tag, never a page load or a
    /// signup.
    /// </para>
    /// </remarks>
    public sealed class WildwoodAttributionStore
    {
        /// <summary>The stored blob's schema version, shared with the JS SDK.</summary>
        public const int SchemaVersion = 1;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly ITokenStore _store;
        private readonly Func<WildwoodAttributionConsent> _consent;
        private readonly int _windowDays;

        /// <summary>Creates a store over the given session-backed key/value store.</summary>
        /// <param name="store">Where the blob lives; <see cref="HttpSessionTokenStore"/> in production.</param>
        /// <param name="consent">
        /// The visitor's consent decision, read on every access. Null means
        /// <see cref="WildwoodAttributionConsent.Undecided"/>, the JS SDK's default.
        /// </param>
        /// <param name="windowDays">How long a touch stays current, clamped to the server's range.</param>
        public WildwoodAttributionStore(
            ITokenStore store,
            Func<WildwoodAttributionConsent>? consent = null,
            int windowDays = AttributionRules.DefaultWindowDays)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _consent = consent ?? (() => WildwoodAttributionConsent.Undecided);
            _windowDays = AttributionRules.ClampWindowDays(windowDays);
        }

        /// <summary>
        /// Applies a landing URL as a touch. Returns the touch, or null for a direct visit — which never
        /// overwrites what an earlier campaign captured — or when consent has been declined.
        /// </summary>
        /// <param name="url">The absolute request URL.</param>
        /// <param name="referrer">The referring URL, when the request carried one.</param>
        /// <param name="options">Capture options; the SDK defaults are used when null.</param>
        public AttributionTouchModel? Capture(string? url, string? referrer, AttributionCaptureOptions? options = null)
        {
            try
            {
                if (ConsentDenied())
                {
                    // Decided and not granted: hold nothing, and drop what an earlier request held.
                    Clear();
                    return null;
                }

                var touch = AttributionRules.ParseTouch(url, referrer, options);
                if (touch is null)
                {
                    return null;
                }

                var blob = Read() ?? new StoredAttribution { VisitorKey = AttributionRules.GenerateVisitorKey() };
                if (AttributionRules.IsTouchExpired(blob.First, _windowDays, DateTimeOffset.UtcNow))
                {
                    blob.First = null;
                    blob.Last = null;
                }

                blob.Last = touch;
                blob.First ??= touch;
                Write(blob);
                return touch;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The payload a registration request carries, or null when nothing was captured. Matches
        /// <c>@wildwood/core</c>'s <c>getForRegistration()</c>, including the <c>dotnet</c> SDK tag.
        /// </summary>
        public AttributionPayloadModel? GetForRegistration()
        {
            try
            {
                var blob = Read();
                if (blob is null || (blob.First is null && blob.Last is null))
                {
                    return null;
                }

                // The funnel session joins the new account to its funnel events (a new one when none is live).
                var now = DateTimeOffset.UtcNow;
                EnsureSession(blob, now);
                Write(blob);

                return new AttributionPayloadModel
                {
                    Version = SchemaVersion,
                    VisitorKey = blob.VisitorKey,
                    FirstTouch = blob.First,
                    LastTouch = blob.Last,
                    Platform = "web",
                    Sdk = "dotnet",
                    SessionKey = blob.SessionKey,
                    SessionCount = blob.SessionCount
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Builds the body of one funnel event for <c>POST api/attribution/events</c>, keeping the visitor
        /// and the funnel session (30 minutes of inactivity ends it) in session state. Returns null, and
        /// sends nothing, when consent has been declined (anything held is dropped, as for
        /// <see cref="Capture"/>), when the name is malformed or server-only, or when the event's own
        /// rules refuse it (a <c>cta_click</c> needs a label; <c>scroll_depth</c> takes 25/50/75/100).
        /// </summary>
        /// <param name="appId">The Wildwood app id.</param>
        /// <param name="name">A standard client event or one of the app's custom names. The server drops names the app does not allow.</param>
        /// <param name="label">Optional label; <c>signup_error</c> labels are reduced to a category.</param>
        /// <param name="value">Optional number.</param>
        /// <param name="path">The page path; any query string or fragment is dropped.</param>
        /// <param name="deviceClass"><c>mobile</c>, <c>tablet</c> or <c>desktop</c> when the host knows it; omitted otherwise.</param>
        /// <param name="nowUtc">The event time; <see cref="DateTimeOffset.UtcNow"/> when null.</param>
        public AttributionEventsRequestModel? CreateEventsRequest(
            string? appId,
            string? name,
            string? label = null,
            double? value = null,
            string? path = null,
            string? deviceClass = null,
            DateTimeOffset? nowUtc = null)
        {
            try
            {
                if (ConsentDenied())
                {
                    Clear();
                    return null;
                }

                if (appId is null || appId.Trim().Length == 0
                    || !AttributionRules.IsValidFunnelEventName(name)
                    || AttributionRules.IsServerOnlyFunnelEvent(name))
                {
                    return null;
                }

                var eventLabel = label is null ? null : AttributionRules.NormalizeFunnelLabel(label);
                double? eventValue = value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
                    ? value
                    : null;

                switch (name)
                {
                    case "cta_click":
                        if (eventLabel is null) return null;
                        break;
                    case "signup_error":
                        eventLabel = AttributionRules.SignupErrorLabel(eventLabel);
                        break;
                    case "scroll_depth":
                        if (eventValue is not (25 or 50 or 75 or 100)) return null;
                        break;
                    case "time_on_page":
                        if (eventValue is null || eventValue < 0) return null;
                        eventValue = Math.Min(86400, Math.Round(eventValue.Value));
                        break;
                }

                var now = nowUtc ?? DateTimeOffset.UtcNow;
                var blob = Read() ?? new StoredAttribution { VisitorKey = AttributionRules.GenerateVisitorKey() };
                EnsureSession(blob, now);
                Write(blob);

                var touch = blob.Last is not null && !AttributionRules.IsTouchExpired(blob.First ?? blob.Last, _windowDays, now)
                    ? blob.Last
                    : null;

                return new AttributionEventsRequestModel
                {
                    AppId = appId!.Trim(),
                    VisitorKey = blob.VisitorKey,
                    SessionKey = blob.SessionKey!,
                    IsReturning = (blob.SessionCount ?? 1) >= 2,
                    DeviceClass = AttributionRules.NormalizeDeviceClass(deviceClass),
                    Platform = "web",
                    Touch = touch,
                    Events =
                    {
                        new AttributionFunnelEventModel
                        {
                            Name = name!,
                            Label = eventLabel,
                            Value = eventValue,
                            Path = AttributionRules.NormalizeFunnelPath(path),
                            ClientTimestamp = now.UtcDateTime.ToString(
                                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture)
                        }
                    }
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Continues the funnel session while its last activity is under 30 minutes old, else starts a
        /// new one, counting on from the stored count. Unlike the browser engines' localStorage blob, this
        /// one lives in session state and so belongs to the current visit: a blob without session fields
        /// was written by <see cref="Capture"/> in this visit, not by an earlier one.
        /// </summary>
        private static void EnsureSession(StoredAttribution blob, DateTimeOffset now)
        {
            var nowMs = now.ToUnixTimeMilliseconds();
            var live = AttributionRules.IsValidVisitorKey(blob.SessionKey)
                && blob.LastActivityAt.HasValue
                && nowMs - blob.LastActivityAt.Value <= AttributionRules.FunnelSessionTimeoutMinutes * 60L * 1000L;

            if (live)
            {
                if (nowMs > blob.LastActivityAt!.Value) blob.LastActivityAt = nowMs;
                if (blob.SessionCount is null or < 1) blob.SessionCount = 1;
                return;
            }

            var previous = blob.SessionCount is > 0 ? blob.SessionCount.Value : 0;
            blob.SessionKey = AttributionRules.GenerateVisitorKey();
            blob.LastActivityAt = nowMs;
            blob.SessionCount = previous + 1;
        }

        /// <summary>Drops the captured touches, after a recorded signup or a withdrawal of consent.</summary>
        public void Clear()
        {
            try
            {
                _store.Remove(WildwoodStorageKeys.Attribution);
            }
            catch (Exception)
            {
                // Best-effort: the visit ends with the session in any case.
            }
        }

        private bool ConsentDenied()
        {
            try
            {
                return _consent() == WildwoodAttributionConsent.Denied;
            }
            catch (Exception)
            {
                // A host delegate that throws must not be read as a grant.
                return false;
            }
        }

        private StoredAttribution? Read()
        {
            var raw = _store.Get(WildwoodStorageKeys.Attribution);
            if (raw is null || raw.Length == 0)
            {
                return null;
            }

            try
            {
                var blob = JsonSerializer.Deserialize<StoredAttribution>(raw, JsonOptions);
                if (blob is null || blob.V != SchemaVersion || !AttributionRules.IsValidVisitorKey(blob.VisitorKey))
                {
                    return null;
                }

                return blob;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private void Write(StoredAttribution blob)
        {
            blob.UpdatedAt = DateTimeOffset.UtcNow.UtcDateTime.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                System.Globalization.CultureInfo.InvariantCulture);
            _store.Set(WildwoodStorageKeys.Attribution, JsonSerializer.Serialize(blob, JsonOptions));
        }

        /// <summary>
        /// The stored blob, field for field the shape the browser engines keep under the same key:
        /// <c>{ v, visitorKey, first, last, updatedAt, sessionKey, lastActivityAt, sessionCount }</c>.
        /// </summary>
        private sealed class StoredAttribution
        {
            [JsonPropertyName("v")]
            public int V { get; set; } = SchemaVersion;

            public string VisitorKey { get; set; } = string.Empty;

            public AttributionTouchModel? First { get; set; }

            public AttributionTouchModel? Last { get; set; }

            public string? UpdatedAt { get; set; }

            /// <summary>The funnel session key; continues while the last activity is under 30 minutes old.</summary>
            public string? SessionKey { get; set; }

            /// <summary>Epoch milliseconds of the session's last tracked activity.</summary>
            public long? LastActivityAt { get; set; }

            /// <summary>Sessions this visitor has started, counting the current one.</summary>
            public int? SessionCount { get; set; }
        }
    }
}
