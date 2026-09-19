using System;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts
{
    /// <summary>
    /// "Registration is closed", with an optional way to reach a human.
    /// </summary>
    /// <remarks>
    /// Ported from packages/wildwood-react/src/components/registrationSubscription/parts/ClosedNotice.tsx,
    /// and public for the same reason it is exported there: a landing page can say so without
    /// mounting the signup flow.
    /// </remarks>
    public partial class ClosedNotice : BaseWildwoodComponent
    {
        /// <summary>The sentence to show.</summary>
        [Parameter] public string Message { get; set; } = string.Empty;

        /// <summary>Where a visitor who still wants in should go. Omitted, no link is rendered.</summary>
        [Parameter] public string? ContactUrl { get; set; }

        /// <summary>The link's text. Omitted, no link is rendered.</summary>
        [Parameter] public string? ContactLabel { get; set; }

        /// <summary>
        /// A link that leaves the app opens in a new tab. Blazor omits an attribute whose value is
        /// null, so a relative URL gets neither attribute.
        /// </summary>
        private string? ExternalTarget
        {
            get { return LeavesTheApp(ContactUrl) ? "_blank" : null; }
        }

        private string? ExternalRel
        {
            get { return LeavesTheApp(ContactUrl) ? "noopener noreferrer" : null; }
        }

        /// <remarks>
        /// Case-INSENSITIVE where React's <c>startsWith('http')</c> is not. A URL scheme is
        /// case-insensitive, so this is a strict superset of React's rule and nothing a visitor
        /// reads changes.
        /// </remarks>
        private static bool LeavesTheApp(string? url)
        {
            return url is { Length: > 0 } && url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        }
    }
}
