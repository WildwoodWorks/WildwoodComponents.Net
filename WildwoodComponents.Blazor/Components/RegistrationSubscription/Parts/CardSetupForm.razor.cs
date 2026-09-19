using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services.Payment;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts
{
    /// <summary>
    /// One Stripe card field confirming a SetupIntent: the pack checkout's single card entry, shown
    /// once for a basket of any size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from
    /// packages/wildwood-react/src/components/registrationSubscription/parts/CardSetupForm.tsx,
    /// whose <c>useStripeCardElement</c> hook has no .NET counterpart — Blazor reaches Stripe
    /// through the embedded payment script instead.
    /// </para>
    /// <para>
    /// The script used to keep ONE Stripe instance and ONE card element, so a page could host
    /// exactly one card form. It is now keyed, and this component takes a key of its own
    /// (<see cref="InstanceKey"/>), which is what lets a pack checkout's card field and
    /// <c>PaymentComponent</c>'s coexist. Nothing here touches the default instance.
    /// </para>
    /// </remarks>
    public partial class CardSetupForm : BaseWildwoodComponent, IAsyncDisposable
    {
        [Inject] private PaymentScriptLoader ScriptLoader { get; set; } = default!;

        /// <summary>The Stripe account to collect the card for. The form waits while this is unknown.</summary>
        [Parameter] public string? PublishableKey { get; set; }

        /// <summary>The SetupIntent secret to confirm. The form waits while this is unknown.</summary>
        [Parameter] public string? ClientSecret { get; set; }

        /// <summary>Copy the host may replace.</summary>
        [Parameter] public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

        /// <summary>The card was saved.</summary>
        [Parameter] public EventCallback OnConfirmed { get; set; }

        /// <summary>The card was refused, or Stripe could not be reached.</summary>
        [Parameter] public EventCallback<string> OnFailed { get; set; }

        private DotNetObjectReference<CardSetupForm>? _dotNetRef;
        private bool _scriptLoaded;
        private bool _initialising;
        private bool _ready;
        private bool _cardComplete;
        private bool _confirming;
        private string? _cardError;
        private string? _setupError;

        /// <summary>
        /// The card container's DOM id. Unique per component, so two card forms on one page mount
        /// into their own elements.
        /// </summary>
        private string ElementId
        {
            get { return "ww-card-setup-" + ComponentId; }
        }

        /// <summary>
        /// Which Stripe instance the embedded script keeps for this form. The same value as
        /// <see cref="ElementId"/>, because one form owns exactly one element.
        /// </summary>
        private string InstanceKey
        {
            get { return ElementId; }
        }

        private bool IsPreparing
        {
            get { return !_ready && string.IsNullOrEmpty(_setupError); }
        }

        private bool CanConfirm
        {
            get
            {
                return _ready
                    && _cardComplete
                    && !_confirming
                    && ClientSecret is { Length: > 0 };
            }
        }

        protected override async Task OnComponentFirstRenderAsync()
        {
            await EnsureCardElementAsync();
        }

        protected override async Task OnComponentRenderedAsync()
        {
            // The publishable key arrives with the SetupIntent, one round trip after the form is
            // first rendered, so mounting waits for a later render rather than the first.
            await EnsureCardElementAsync();
        }

        /// <summary>
        /// Loads the Stripe script once and mounts this form's own card element on it.
        /// </summary>
        private async Task EnsureCardElementAsync()
        {
            if (_ready || _initialising) return;
            if (!(PublishableKey is { Length: > 0 })) return;

            _initialising = true;
            try
            {
                if (!_scriptLoaded)
                {
                    _scriptLoaded = await ScriptLoader.LoadAsync(PaymentProviderType.Stripe);
                    if (!_scriptLoaded)
                    {
                        await FailSetupAsync("The payment system could not be loaded. Please try again.");
                        return;
                    }
                }

                _dotNetRef ??= DotNetObjectReference.Create(this);

                var mounted = await JSRuntime.InvokeAsync<bool>(
                    "wildwoodPayment.initStripe",
                    PublishableKey,
                    ElementId,
                    _dotNetRef,
                    "card-setup-" + ComponentId,
                    InstanceKey);

                if (!mounted)
                {
                    await FailSetupAsync("The card field could not be loaded. Please try again.");
                    return;
                }

                _ready = true;
                StateHasChanged();
            }
            catch (Exception ex)
            {
                await FailSetupAsync(ex.Message is { Length: > 0 } ? ex.Message : "The card field could not be loaded.");
            }
            finally
            {
                _initialising = false;
            }
        }

        /// <summary>
        /// Confirms the SetupIntent with the card the visitor typed. Nothing is charged: the packs
        /// are bought by the server afterwards, against the card this saves.
        /// </summary>
        private async Task ConfirmAsync()
        {
            if (!CanConfirm) return;

            _confirming = true;
            _setupError = null;
            StateHasChanged();

            try
            {
                var result = await JSRuntime.InvokeAsync<StripeSetupResult?>(
                    "wildwoodPayment.confirmStripeSetup", ClientSecret, InstanceKey);

                if (result is not null && result.Success)
                {
                    if (OnConfirmed.HasDelegate) await OnConfirmed.InvokeAsync();
                    return;
                }

                var message = result?.ErrorMessage is { Length: > 0 } reason ? reason : "Card setup failed";
                await RaiseFailedAsync(message);
            }
            catch (Exception ex)
            {
                await RaiseFailedAsync(ex.Message is { Length: > 0 } ? ex.Message : "Card setup failed");
            }
            finally
            {
                _confirming = false;
                StateHasChanged();
            }
        }

        private async Task FailSetupAsync(string message)
        {
            _setupError = message;
            StateHasChanged();
            await RaiseFailedAsync(message);
        }

        private Task RaiseFailedAsync(string message)
        {
            return OnFailed.HasDelegate ? OnFailed.InvokeAsync(message) : Task.CompletedTask;
        }

        /// <summary>Called from JavaScript as the card input changes.</summary>
        [JSInvokable]
        public void OnCardChange(bool complete, string? error)
        {
            _cardComplete = complete;
            _cardError = error;
            StateHasChanged();
        }

        /// <summary>
        /// Unmounts this form's card element and releases the payment script.
        /// </summary>
        /// <remarks>
        /// The base's <see cref="BaseWildwoodComponent.Dispose()"/> is called by hand at the end:
        /// Blazor calls <c>DisposeAsync</c> INSTEAD of <c>Dispose</c> on a component that
        /// implements <see cref="IAsyncDisposable"/>, so without this the base's
        /// <c>ThemeChanged</c> unsubscription would never run and the theme service would hold
        /// this component for the rest of the circuit.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            try
            {
                // This form's instance only: the default one belongs to PaymentComponent.
                await JSRuntime.InvokeVoidAsync("wildwoodPayment.disposeStripe", InstanceKey);
            }
            catch
            {
                // Ignore disposal errors — the circuit may already be gone.
            }

            if (_scriptLoaded)
            {
                try
                {
                    await ScriptLoader.ReleaseAsync(PaymentProviderType.Stripe);
                }
                catch
                {
                    // Ignore release errors during disposal
                }
            }

            _dotNetRef?.Dispose();
            _dotNetRef = null;

            Dispose();
        }

        /// <summary>The shape <c>confirmStripeSetup</c> answers with.</summary>
        private sealed class StripeSetupResult
        {
            public bool Success { get; set; }

            public string? SetupIntentId { get; set; }

            public string? ErrorMessage { get; set; }

            public string? ErrorCode { get; set; }
        }
    }
}
