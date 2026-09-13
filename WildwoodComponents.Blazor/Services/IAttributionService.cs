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
    }
}
