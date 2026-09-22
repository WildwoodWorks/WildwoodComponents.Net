using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts
{
    /// <summary>
    /// The status line and the failure alert of a plan change.
    /// </summary>
    /// <remarks>
    /// Ported from
    /// packages/wildwood-react/src/components/registrationSubscription/parts/PlanChangeNotice.tsx.
    /// React hands the whole flow object in; this takes the three values it reads, because the
    /// driver is internal to the library and a public component cannot name it.
    /// </remarks>
    public partial class PlanChangeNotice : BaseWildwoodComponent
    {
        /// <summary>Where the change is. Nothing renders unless it is authenticating, applying or failed.</summary>
        [Parameter] public PlanChangeStep Step { get; set; } = PlanChangeStep.Idle;

        /// <summary>Why the change stopped, in words. Shown under the failure heading.</summary>
        [Parameter] public string? Error { get; set; }

        /// <summary>Whether the failed step can be run again.</summary>
        [Parameter] public bool CanRetry { get; set; }

        /// <summary>Copy the host may replace.</summary>
        [Parameter] public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

        /// <summary>Run the failed step again.</summary>
        [Parameter] public EventCallback OnRetry { get; set; }

        /// <summary>Forget the attempt and clear the alert.</summary>
        [Parameter] public EventCallback OnDismiss { get; set; }

        private string StatusText
        {
            get
            {
                return Step == PlanChangeStep.Authenticating
                    ? Labels.AuthenticatingChange
                    : Labels.ApplyingChange;
            }
        }

        private Task HandleRetryAsync()
        {
            return OnRetry.HasDelegate ? OnRetry.InvokeAsync() : Task.CompletedTask;
        }

        private Task HandleDismissAsync()
        {
            return OnDismiss.HasDelegate ? OnDismiss.InvokeAsync() : Task.CompletedTask;
        }
    }
}
