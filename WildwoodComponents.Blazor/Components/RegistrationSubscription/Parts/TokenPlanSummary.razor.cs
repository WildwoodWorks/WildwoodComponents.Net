using System.Collections.Generic;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Components.Registration;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts
{
    /// <summary>
    /// What a registration token grants: the tier, its pricing option, the packs and the extra
    /// features — never priced.
    /// </summary>
    /// <remarks>
    /// Ported from
    /// packages/wildwood-react/src/components/registrationSubscription/parts/TokenPlanSummary.tsx.
    /// Names come from the server by INDEX against the granted ids, falling back to the id, which
    /// is what <see cref="SignupPlanDecisions.DisplayNames"/> already does for the signup wizard.
    /// </remarks>
    public partial class TokenPlanSummary : BaseWildwoodComponent
    {
        /// <summary>The grant for THIS app, as the detailed token validation reported it.</summary>
        [Parameter] public RegistrationTokenAppGrant? Grant { get; set; }

        /// <summary>Copy the host may replace.</summary>
        [Parameter] public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

        /// <summary>The plan's name as the server named it, falling back to its id.</summary>
        private string TierName
        {
            get
            {
                if (Grant is null) return string.Empty;
                if (Grant.AppTierName is { Length: > 0 } name) return name;
                return Grant.AppTierId;
            }
        }

        private List<string> Packs
        {
            get { return SignupPlanDecisions.DisplayNames(Grant?.AddOnIds, Grant?.AddOnNames); }
        }

        private List<string> Features
        {
            get { return SignupPlanDecisions.DisplayNames(Grant?.FeatureCodes, Grant?.FeatureNames); }
        }
    }
}
