using System;
using System.Threading.Tasks;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// The registration funnel reporter the signup components share (the Blazor counterpart of
    /// <c>useSignupFunnel</c> in <c>@wildwood/react-shared</c>). It sends <c>signup_view</c>,
    /// <c>signup_start</c>, <c>signup_submit</c>, <c>signup_error</c>, <c>plan_selected</c> and
    /// <c>checkout_start</c> through <see cref="IAttributionService.TrackAsync"/>.
    /// </summary>
    /// <remarks>
    /// The engine does the gating: nothing is sent while the app has funnel tracking or signup steps
    /// off, and the one-shot steps go out once per session, so a form and the flow around it may both
    /// report a step without counting it twice. Two rules hold everywhere: a call never throws into the
    /// form, and a <c>signup_error</c> label is a fixed category (<see cref="AttributionRules.SignupErrorCategories"/>),
    /// never what the visitor typed or a server's message.
    /// </remarks>
    public sealed class SignupFunnel
    {
        private readonly IAttributionService? _attribution;
        private bool _viewed;
        private bool _started;

        /// <summary>Creates a reporter over the attribution service; null makes every call a no-op.</summary>
        public SignupFunnel(IAttributionService? attribution)
        {
            _attribution = attribution;
        }

        /// <summary>The registration form rendered. Sent once per reporter.</summary>
        public Task ViewAsync()
        {
            if (_viewed) return Task.CompletedTask;
            _viewed = true;
            return SendAsync("signup_view", null);
        }

        /// <summary>The visitor started filling the form (the first focus on a field). Sent once per reporter.</summary>
        public Task StartAsync()
        {
            if (_started) return Task.CompletedTask;
            _started = true;
            return SendAsync("signup_start", null);
        }

        /// <summary>
        /// The account form was submitted; call before the request goes out. A submit without a focus first
        /// (autofill, a password manager) still started the form, so it reports <c>signup_start</c> too.
        /// </summary>
        public async Task SubmitAsync()
        {
            await StartAsync();
            await SendAsync("signup_submit", null);
        }

        /// <summary>Registration failed with this category (one of <see cref="AttributionRules.SignupErrorCategories"/>).</summary>
        public Task ErrorAsync(string category) => SendAsync("signup_error", AttributionRules.SignupErrorLabel(category));

        /// <summary>
        /// Registration failed with this error code (WildwoodAPI's <c>errorCode</c>, e.g. <c>USERNAME_EXISTS</c>)
        /// and/or HTTP status (0 for a network failure); only the mapped category is sent.
        /// </summary>
        public Task ErrorFromCodeAsync(string? code, int? status = null)
            => ErrorAsync(AttributionRules.SignupErrorCategoryFromCode(code, status));

        /// <summary>A plan was chosen. The label is the tier id, else a slug of its name.</summary>
        public Task PlanSelectedAsync(string? tierId, string? tierName = null)
            => SendAsync("plan_selected", AttributionRules.SignupPlanKey(tierId, tierName));

        /// <summary>The payment step opened for a paid plan. The label is the pricing option id, else the tier.</summary>
        public Task CheckoutStartAsync(string? pricingId, string? tierId, string? tierName = null)
            => SendAsync(
                "checkout_start",
                AttributionRules.SignupPlanKey(pricingId) ?? AttributionRules.SignupPlanKey(tierId, tierName));

        private async Task SendAsync(string name, string? label)
        {
            if (_attribution is null) return;
            try
            {
                await _attribution.TrackAsync(name, label);
            }
            catch (Exception)
            {
                // Funnel tracking must never break registration.
            }
        }
    }
}
