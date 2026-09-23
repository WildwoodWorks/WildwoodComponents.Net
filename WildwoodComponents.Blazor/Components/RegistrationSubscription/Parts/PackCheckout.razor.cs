using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts
{
    /// <summary>
    /// Buys a basket of packs as the signed-in user: one quote, one card at most, one purchase,
    /// then any pack the bank wants authenticated, one at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from
    /// packages/wildwood-react/src/components/registrationSubscription/parts/PackCheckout.tsx. The
    /// order and every server call are <see cref="PackCheckoutDriver"/>'s, so this file is the web
    /// half only: the Stripe card field the driver waits on, and the payment-action adapter that
    /// answers a bought pack's 3-D Secure.
    /// </para>
    /// <para>
    /// Failures are reported through the INHERITED <see cref="BaseWildwoodComponent.OnError"/>
    /// callback, with the JS error code as the <c>Context</c>. It is never redeclared here: two
    /// <c>[Parameter]</c> properties of one name make a component impossible to render at all.
    /// </para>
    /// <para>
    /// Disposal is asynchronous because the Stripe instance the 3-D Secure path creates is
    /// dropped through JS interop. See <see cref="DisposeAsync"/> for why the base's synchronous
    /// clean-up is called by hand from there.
    /// </para>
    /// </remarks>
    public partial class PackCheckout : BaseWildwoodComponent, IAsyncDisposable
    {
        [Inject] private IAppTierComponentService AppTierService { get; set; } = default!;

        [Inject] private IPaymentProviderService PaymentProviderService { get; set; } = default!;

        /// <summary>
        /// Handed to the driver so a purchase DRAINED after this component has gone can still drop
        /// the entitlement cache: the <see cref="OnFinished"/> route that normally does it runs
        /// through the signup view, which by then is no longer listening.
        /// </summary>
        [Inject] private IFeatureEntitlementService EntitlementService { get; set; } = default!;

        /// <summary>The app the packs belong to.</summary>
        [Parameter, EditorRequired] public string AppId { get; set; } = string.Empty;

        /// <summary>The packs still to buy — the chosen ones, minus anything a token granted.</summary>
        [Parameter] public IReadOnlyList<AddOnCheckoutItemInput> Items { get; set; } =
            new List<AddOnCheckoutItemInput>();

        /// <summary>Pack names from the catalog, so an outcome can name a pack the quote never priced.</summary>
        [Parameter] public IReadOnlyDictionary<string, string>? Names { get; set; }

        /// <summary>Copy the host may replace.</summary>
        [Parameter] public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

        /// <summary>Every requested pack's outcome, including the ones that failed or were skipped.</summary>
        [Parameter] public EventCallback<IReadOnlyList<SignupPackOutcome>> OnFinished { get; set; }

        private PackCheckoutDriver? _driver;
        private bool _disposedAsync;

        internal PackCheckoutDriver? Driver
        {
            get { return _driver; }
        }

        /// <summary>
        /// The script instance this checkout's 3-D Secure challenges run on. Unique per component,
        /// so a pack's card authentication never touches the card form's instance or
        /// <c>PaymentComponent</c>'s default one — and so the key this component disposes is the
        /// key it created.
        /// </summary>
        internal string PaymentInstanceKey
        {
            get { return "ww-pack-checkout-" + ComponentId; }
        }

        /// <summary>
        /// Whether the status line is showing: while the basket is being priced, and while any
        /// server call is in flight.
        /// </summary>
        private bool ShowStatus
        {
            get
            {
                if (_driver is null) return false;
                return _driver.Step == PackCheckoutStep.Idle
                    || _driver.Step == PackCheckoutStep.Quoting
                    || _driver.Busy;
            }
        }

        private string StatusText
        {
            get
            {
                if (_driver is null) return Labels.BuyingPacks;

                if (_driver.Step == PackCheckoutStep.Authenticating || _driver.Step == PackCheckoutStep.Completing)
                {
                    return RegistrationSubscriptionLabels.Format(
                        Labels.AuthenticatingPack, "name", _driver.AuthenticatingName);
                }

                return Labels.BuyingPacks;
            }
        }

        protected override async Task OnComponentInitializedAsync()
        {
            _driver = new PackCheckoutDriver(
                AppTierService,
                PaymentProviderService,
                new StripePaymentActions(JSRuntime, PaymentInstanceKey),
                new PackCheckoutSettings
                {
                    AppId = AppId,
                    Items = Items,
                    Names = Names,
                    Labels = Labels
                },
                EntitlementService,
                Logger)
            {
                StateChanged = StateHasChanged,
                Finished = RaiseFinishedAsync,
                ErrorReported = ReportAsync
            };

            await _driver.StartAsync();
        }

        private Task HandleCardConfirmedAsync()
        {
            return _driver is null ? Task.CompletedTask : _driver.CardConfirmedAsync();
        }

        private Task HandleCardFailedAsync(string message)
        {
            return _driver is null ? Task.CompletedTask : _driver.CardFailedAsync(message);
        }

        private Task RetryAsync()
        {
            return _driver is null ? Task.CompletedTask : _driver.RetryAsync();
        }

        private Task SkipAsync()
        {
            return _driver is null ? Task.CompletedTask : _driver.SkipAsync();
        }

        private Task RaiseFinishedAsync(IReadOnlyList<SignupPackOutcome> outcomes)
        {
            return OnFinished.HasDelegate ? OnFinished.InvokeAsync(outcomes) : Task.CompletedTask;
        }

        private Task ReportAsync(string code, string message)
        {
            return InvokeOnErrorAsync(new InvalidOperationException(message), code);
        }

        /// <summary>
        /// Detaches the checkout from this component and drops the Stripe instance its 3-D Secure
        /// challenges created.
        /// </summary>
        /// <remarks>
        /// The driver's own disposal returns straight away when a bank challenge is still in
        /// flight — it drains that pack's purchase to its completion in the background and drops
        /// the script instance afterwards — so leaving the page is never held up behind a customer
        /// finishing a challenge. See <see cref="PackCheckoutDriver.DisposeAsync"/>.
        ///
        /// The base's <see cref="BaseWildwoodComponent.Dispose()"/> is called by hand at the end:
        /// Blazor calls <c>DisposeAsync</c> INSTEAD of <c>Dispose</c> on a component that
        /// implements <see cref="IAsyncDisposable"/>, so without this the base's
        /// <c>ThemeChanged</c> unsubscription would never run and the theme service would hold
        /// this component for the rest of the circuit.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            if (_disposedAsync) return;
            _disposedAsync = true;

            var driver = _driver;
            _driver = null;

            if (driver is not null)
            {
                await driver.DisposeAsync();
            }

            Dispose();
        }
    }
}
