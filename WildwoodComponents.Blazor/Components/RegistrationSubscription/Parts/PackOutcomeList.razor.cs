using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts
{
    /// <summary>
    /// One line per pack the signup asked for, saying what became of it.
    /// </summary>
    /// <remarks>
    /// Ported from
    /// packages/wildwood-react/src/components/registrationSubscription/parts/PackOutcomeList.tsx.
    /// A basket is not a transaction, so a failed pack is listed beside the ones that worked
    /// rather than collapsing the whole list into one verdict.
    /// </remarks>
    public partial class PackOutcomeList : BaseWildwoodComponent
    {
        /// <summary>Every requested pack's outcome, granted ones first.</summary>
        [Parameter] public IReadOnlyList<SignupPackOutcome>? Packs { get; set; }

        /// <summary>Copy the host may replace.</summary>
        [Parameter] public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

        private string StatusLabel(string status)
        {
            if (string.Equals(status, SignupPackStatuses.Trialing, StringComparison.Ordinal))
            {
                return Labels.PackStatusTrialing;
            }

            if (string.Equals(status, SignupPackStatuses.Active, StringComparison.Ordinal))
            {
                return Labels.PackStatusActive;
            }

            if (string.Equals(status, SignupPackStatuses.Granted, StringComparison.Ordinal))
            {
                return Labels.PackStatusGranted;
            }

            return Labels.PackStatusFailed;
        }

        /// <summary>
        /// A trial end date in the visitor's own locale, or nothing when the server sent none.
        /// </summary>
        private static string TrialEndText(DateTime? trialEnd)
        {
            return trialEnd is null ? string.Empty : trialEnd.Value.ToShortDateString();
        }
    }
}
