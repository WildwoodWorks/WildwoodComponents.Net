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

                return new AttributionPayloadModel
                {
                    Version = SchemaVersion,
                    VisitorKey = blob.VisitorKey,
                    FirstTouch = blob.First,
                    LastTouch = blob.Last,
                    Platform = "web",
                    Sdk = "dotnet"
                };
            }
            catch (Exception)
            {
                return null;
            }
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
        /// <c>{ v, visitorKey, first, last, updatedAt }</c>.
        /// </summary>
        private sealed class StoredAttribution
        {
            [JsonPropertyName("v")]
            public int V { get; set; } = SchemaVersion;

            public string VisitorKey { get; set; } = string.Empty;

            public AttributionTouchModel? First { get; set; }

            public AttributionTouchModel? Last { get; set; }

            public string? UpdatedAt { get; set; }
        }
    }
}
