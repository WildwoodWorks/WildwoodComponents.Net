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
    }
}
