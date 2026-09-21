using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Drives the Wildwood Consent engine (JS) from Blazor: fetches config, applies the decision
    /// table, and injects consent-gated scripts. Block-before-consent is enforced in the engine.
    /// </summary>
    public interface IConsentService
    {
        /// <summary>Initialize consent for an app. Returns the config, state, and whether to show the banner.</summary>
        Task<ConsentInitResult> InitializeAsync(string appId, string? baseUrlOverride = null);

        /// <summary>Grant all active categories. Injects newly-granted scripts and records the decision.</summary>
        Task<ConsentStateModel?> AcceptAllAsync();

        /// <summary>Reject all - only StrictlyNecessary remains.</summary>
        Task<ConsentStateModel?> RejectAllAsync();

        /// <summary>Apply a custom per-category selection.</summary>
        Task<ConsentStateModel?> SetCategoriesAsync(Dictionary<string, bool> selection);

        /// <summary>Withdraw consent (records reject-all, clears the cookie; caller should prompt a reload).</summary>
        Task<ConsentStateModel?> WithdrawAsync();

        /// <summary>Focus-trap the preferences dialog while open (WCAG 2.2 AA).</summary>
        Task TrapFocusAsync(ElementReference element);

        /// <summary>Release the focus trap and restore focus to the trigger.</summary>
        Task ReleaseFocusAsync();

        /// <summary>
        /// Publish the banner's measured height as <c>--ww-consent-height</c> and, unless the host
        /// opts out, reserve that much room at the edge the banner is anchored to, so the fixed
        /// banner cannot cover the page's own bottom- or top-anchored UI.
        /// </summary>
        /// <param name="element">The banner element to measure and observe.</param>
        /// <param name="reserve">False publishes the height and leaves the page's padding alone.</param>
        /// <param name="key">
        /// A token identifying THIS banner's reservation. Every live banner holds its own, so two
        /// components sharing this scoped service - and so one cached copy of the JS engine -
        /// never release or double-count each other's. Calling this again with the same key
        /// re-measures that one reservation rather than adding a second.
        /// </param>
        /// <remarks>
        /// Only the two bar positions reserve padding. A <c>corner</c> card is a small inset box,
        /// so padding the whole page for it would leave a full-width blank strip under the
        /// content; it publishes the height and pads nothing.
        /// </remarks>
        Task ReserveBannerSpaceAsync(ElementReference element, bool reserve, string key);

        /// <summary>
        /// Give one banner's reserved room back, and - when it was the last one up - the published
        /// height and the page's own inline padding, restored exactly as found.
        /// </summary>
        /// <param name="key">
        /// The token the reservation was taken under. A key that holds nothing is a no-op, so
        /// releasing twice cannot strand or steal another banner's padding. Taken by key rather
        /// than by element because this is also called from <c>Dispose</c>, by which time the
        /// banner's DOM node is gone and an <see cref="ElementReference"/> no longer resolves.
        /// </param>
        Task ReleaseBannerSpaceAsync(string key);
    }
}
