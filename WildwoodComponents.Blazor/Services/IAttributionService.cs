using System.Threading.Tasks;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Campaign Attribution. The engine runs in the browser (wwwroot/js/wildwood-attribution.js, loaded via JS
    /// isolation): it reads the UTM tags, click id and referrer from the landing URL, keeps a first and a last
    /// touch, persists them only once the app's consent category is granted, and beacons the landing when the
    /// app has the beacon on. Every call degrades to null or a no-op when interop is unavailable (prerendering,
    /// a disconnected circuit), so attribution can never break a page or a signup.
    /// </summary>
    public interface IAttributionService
    {
        /// <summary>
        /// Captures the landing URL, loads the app's attribution config and applies the consent gate. Idempotent;
        /// call once, as early as possible (AttributionBootstrap does this from the layout).
        /// </summary>
        Task<AttributionStateModel?> InitializeAsync(string appId, string? baseUrlOverride = null);

        /// <summary>Parses a URL and applies it as a touch. Null for a direct visit or when unavailable.</summary>
        Task<AttributionTouchModel?> CaptureUrlAsync(string url, string? referrer = null);

        /// <summary>The payload registration requests carry, or null when nothing was captured.</summary>
        Task<AttributionPayloadModel?> GetForRegistrationAsync();

        /// <summary>Drops the captured touches from memory and browser storage (after a recorded signup).</summary>
        Task ClearAsync();

        /// <summary>The engine's current state, or null when unavailable.</summary>
        Task<AttributionStateModel?> GetStateAsync();

        /// <summary>
        /// Tracks a funnel event (<c>cta_click</c>, <c>signup_start</c>, a configured custom name...). The engine
        /// buffers it until the app config loads and drops it when funnel tracking is off, the name is not
        /// allowed, or it is a one-shot already sent this session. Never throws.
        /// </summary>
        /// <param name="name">A standard client event or one of the app's custom names (<c>^[a-z0-9_]{1,40}$</c>).</param>
        /// <param name="label">Optional label (CTA name, plan id, error category); trimmed and capped at 100 characters.</param>
        /// <param name="value">Optional number.</param>
        Task TrackAsync(string name, string? label = null, double? value = null) => Task.CompletedTask;

        /// <summary>
        /// <see cref="TrackAsync(string, string?, double?)"/> for a named page — @wildwood/core's
        /// <c>track(name, { label, value, path })</c>. The path is normalised by the engine (query and
        /// fragment dropped); null means the current page. Never throws.
        /// </summary>
        /// <param name="name">A standard client event or one of the app's custom names.</param>
        /// <param name="label">Optional label.</param>
        /// <param name="value">Optional number.</param>
        /// <param name="path">The page path the event belongs to, e.g. <c>/pricing</c>.</param>
        Task TrackAsync(string name, string? label, double? value, string? path) => TrackAsync(name, label, value);

        /// <summary>Tracks a <c>cta_click</c> with this label. Never throws.</summary>
        Task TrackCtaAsync(string label) => Task.CompletedTask;

        /// <summary>Sends the queued funnel events now, for example before a hard navigation. Never throws.</summary>
        Task FlushAsync() => Task.CompletedTask;
    }
}
