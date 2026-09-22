using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Components.Subscription.Admin;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription
{
    /// <summary>Whose subscription a plan change is acting on, and with what copy.</summary>
    internal sealed class PlanChangeSettings
    {
        public string AppId { get; set; } = string.Empty;

        /// <summary>
        /// Set for an admin acting on one user's subscription. Such a change is authorised
        /// server-side and never collects a card.
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>Set for an admin acting on a company's subscription. Never collects a card either.</summary>
        public string? CompanyId { get; set; }

        public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

        /// <summary>
        /// How long a <c>Processing</c> answer is left alone before the completion is asked again.
        /// Settable so a test does not have to wait out the real one.
        /// </summary>
        public TimeSpan CompleteRetryDelay { get; set; } =
            TimeSpan.FromMilliseconds(PlanChangeDriver.CompleteRetryDelayMs);
    }

    /// <summary>
    /// Changing an existing subscriber's plan: price it, confirm it, take a card if one is needed,
    /// post the change, answer the bank if it asks, and finish the parked change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from
    /// packages/wildwood-react-shared/src/registrationSubscription/usePlanChangeFlow.ts. The order
    /// lives in the shared <see cref="PlanChangeMachine"/>; this is the half that touches the
    /// world, and it is renderer-free so the whole flow can be tested without a browser.
    /// </para>
    /// <para>Two rules keep it honest, the same two the pack checkout and the signup follow:</para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Every async step is claimed by the machine's step token before it starts.</b> A double
    /// click, a re-render and a payment callback that fires twice all land on a token the machine
    /// has moved past, so nothing is previewed, charged or changed twice.
    /// </description></item>
    /// <item><description>
    /// <b>A <c>Processing</c> completion is a "not yet", not a failure.</b> The money is in and the
    /// server is still applying the change, so it is asked again on the machine's bounded budget
    /// (<see cref="PlanChangeMachine.MaxPlanChangeCompleteAttempts"/>). A manual "Try Again" starts
    /// that budget over.
    /// </description></item>
    /// </list>
    /// <para>
    /// The card, when one is needed before the change is posted, comes from the host's own
    /// <see cref="PaymentRequested"/> handler when it supplied one — that handler predates this
    /// driver and keeps winning — and otherwise from the view's built-in payment modal, which is
    /// what <see cref="PaymentRequest"/> is for. The two answer differently on purpose: a host's
    /// modal is outside this flow, so closing it abandons the change; the built-in one is inside
    /// it, so closing it returns to the confirmation with the priced change intact.
    /// </para>
    /// <para>
    /// Blazor is a WEB stack, so a Stripe adapter always exists: the change is posted in the
    /// OPTIONS form with <c>SupportsPaymentAction</c>, which lets the server park a change on a
    /// 3-D Secure challenge rather than refusing it.
    /// </para>
    /// <para>No LINQ: this runs inside Blazor components and LINQ breaks iOS/MAUI at runtime.</para>
    /// </remarks>
    internal sealed class PlanChangeDriver : IAsyncDisposable
    {
        /// <summary>TS <c>COMPLETE_RETRY_DELAY_MS</c>.</summary>
        public const int CompleteRetryDelayMs = 500;

        /// <summary>The <c>onError</c> codes, by the step that failed. TS <c>codeForFailure</c>.</summary>
        public const string PreviewFailedCode = "tier_preview_failed";

        public const string AuthenticationFailedCode = "tier_change_authentication_failed";

        public const string CompletionFailedCode = "tier_change_completion_failed";

        public const string ChangeFailedCode = "tier_change_failed";

        /// <summary>Said when the server would not price the change and sent no reason.</summary>
        public const string PreviewRefused = "The plan change could not be priced.";

        /// <summary>Said when the change was refused and no reason came with it.</summary>
        public const string ChangeRefused = "The plan change was refused.";

        /// <summary>Said when the completion threw rather than answering.</summary>
        public const string CompletionRefused = "The plan change could not be completed.";

        /// <summary>Said when the bank challenge could not be put to the customer, or came back refused.</summary>
        public const string ChargeUnconfirmed =
            "The charge could not be confirmed with your bank. Your plan has not changed.";

        /// <summary>Said when the host's own payment handler threw.</summary>
        public const string PaymentNotTaken = "The payment could not be taken.";

        /// <summary>
        /// A ceiling on one pump: every iteration either advances the machine or stops, so this is
        /// only reached by a bug. Breaking beats spinning a circuit forever.
        /// </summary>
        private const int MaxPumpIterations = 64;

        private readonly IAppTierComponentService _appTier;
        private readonly IPaymentProviderService _payments;
        private readonly IPaymentActionAdapter _paymentActions;
        private readonly IFeatureEntitlementService? _entitlements;
        private readonly ILogger? _logger;
        private readonly Dictionary<string, string?> _runs = new Dictionary<string, string?>(StringComparer.Ordinal);

        private PlanChangeState _state;
        private TierSelectedEventArgs? _selection;
        private string? _publishableKey;
        private bool _keyLookupDone;
        private bool _dirty;
        private bool _pumping;

        /// <summary>The steps the machine does not tokenise, each claimed once per entry.</summary>
        private bool _paymentAsked;

        private bool _doneHandled;
        private bool _failureReported;

        /// <summary>The entitlement cache has been dropped for the change that landed. Once only.</summary>
        private bool _entitlementsInvalidated;

        /// <summary>The view has gone. See <see cref="DisposeAsync"/>: detach is not stop.</summary>
        private bool _detached;

        /// <summary>The drain is over and the payment script instance has been dropped.</summary>
        private bool _detachFinished;

        /// <param name="appTier">Previews, posts and completes the change.</param>
        /// <param name="payments">Reads the app's payment configuration, for the publishable key.</param>
        /// <param name="paymentActions">Puts a 3-D Secure challenge to the customer.</param>
        /// <param name="settings">Whose subscription this is, and with what copy.</param>
        /// <param name="entitlements">
        /// The scoped entitlement cache. The driver drops it itself rather than leaving it to the
        /// host: JS's data hook invalidates inside its own mutation, and a change DRAINED after
        /// teardown has no host callback left to do it.
        /// </param>
        /// <param name="logger">Where a drain's own failures go, there being no view left to show them.</param>
        public PlanChangeDriver(
            IAppTierComponentService appTier,
            IPaymentProviderService payments,
            IPaymentActionAdapter paymentActions,
            PlanChangeSettings settings,
            IFeatureEntitlementService? entitlements = null,
            ILogger? logger = null)
        {
            _appTier = appTier;
            _payments = payments;
            _paymentActions = paymentActions;
            _entitlements = entitlements;
            _logger = logger;
            Settings = settings;

            _state = PlanChangeMachine.InitialState(new PlanChangeMachineOptions { AppId = settings.AppId });
        }

        #region Host wiring

        public PlanChangeSettings Settings { get; }

        /// <summary>Re-render. Synchronous, because <c>StateHasChanged</c> is.</summary>
        public Action? StateChanged { get; set; }

        /// <summary>
        /// The host's own card modal. Given one, it is used INSTEAD of the built-in modal and its
        /// answer is final: a transaction id completes the change, null or empty abandons it.
        /// </summary>
        public Func<PaymentRequiredArgs, Task<string?>>? PaymentRequested { get; set; }

        /// <summary>Reload whatever shows the subscription, once a change has landed.</summary>
        public Func<Task>? Changed { get; set; }

        /// <summary>Told after a change lands, with the reason, so the host can refresh its own gates.</summary>
        public Func<string, Task>? EntitlementsChanged { get; set; }

        /// <summary>Told about every failure, with a stable code. Raised once per failure.</summary>
        public Func<string, string, Task>? ErrorReported { get; set; }

        /// <summary>
        /// DETACHES the flow from the view, and lets whatever the bank has already been asked drain
        /// to its end in the background.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The same rule the pack checkout follows, for the same reason. Clearing the host
        /// callbacks is the easy half: an answer still in flight must not re-render or report into
        /// a component that has left the page, and no NEW work that needs a person — a fresh
        /// preview, a card, a change not yet posted, a challenge not yet put to anyone — may be
        /// started for a customer who is gone.
        /// </para>
        /// <para>
        /// The hard half is what has ALREADY moved money. A 3-D Secure confirmation is a live
        /// interop call: teardown does not cancel it, and the bank answers it minutes later
        /// regardless. Abandoning that answer would charge the customer's card and then never call
        /// <c>CompleteTierChangeAsync</c>, so the proration is paid and the plan never moves, with
        /// no message either way. So an authenticated change is DRAINED — server-side only, no
        /// re-render, no host callback — through its completion, and the entitlement cache is
        /// dropped afterwards because the plan really did change. See <see cref="MayRunNextStep"/>.
        /// </para>
        /// <para>
        /// This never waits on a bank: with nothing in flight it drops the keyed Stripe instance
        /// and returns; with a step in flight it returns at once and the pump drops the instance
        /// when the drain stops — disposing it sooner would pull the instance out from under a
        /// <c>confirmCardPayment</c> that is still running on it. Idempotent.
        /// </para>
        /// </remarks>
        public ValueTask DisposeAsync()
        {
            if (_detached) return default;
            _detached = true;

            StateChanged = null;
            PaymentRequested = null;
            Changed = null;
            EntitlementsChanged = null;
            ErrorReported = null;

            // Nothing is in flight, so there is nothing to drain: the script instance goes now.
            if (!_pumping) return new ValueTask(FinishDetachedAsync());

            // A step IS in flight. Its task is already running and the pump's own finally finishes
            // the detach when it stops, so the component's disposal is not held up behind it.
            return default;
        }

        #endregion

        #region What the view renders from

        public PlanChangeState State
        {
            get { return _state; }
        }

        public PlanChangeStep Step
        {
            get { return _state.Step; }
        }

        /// <summary>
        /// The <c>data-ww-step</c> value: the machine's step with a lower-cased first letter, the
        /// vocabulary JS's test-hook table pins. The table is <see cref="StepNames.ForPlanChange"/>.
        /// </summary>
        public string StepName
        {
            get { return StepNames.ForPlanChange(_state.Step); }
        }

        /// <summary>The preview to confirm, or null when nothing is waiting on the customer.</summary>
        public TierChangePreviewModel? Preview
        {
            get { return _state.Step == PlanChangeStep.Confirm ? _state.Preview : null; }
        }

        /// <summary>
        /// What the BUILT-IN card modal should collect, or null — either because no card is needed
        /// right now, or because the host brought its own modal.
        /// </summary>
        public PaymentRequiredArgs? PaymentRequest
        {
            get
            {
                if (PaymentRequested is not null) return null;
                return BuildPaymentRequest();
            }
        }

        /// <summary>Whether a server call is in flight: the confirmation's button reads "Processing...".</summary>
        public bool Busy
        {
            get
            {
                return _state.Step == PlanChangeStep.Previewing
                    || _state.Step == PlanChangeStep.Changing
                    || _state.Step == PlanChangeStep.Authenticating
                    || _state.Step == PlanChangeStep.Completing;
            }
        }

        /// <summary>Why the change stopped, in words. Null unless the flow failed.</summary>
        public string? Error
        {
            get
            {
                if (_state.Step != PlanChangeStep.Failed) return null;
                return MessageForCode(Settings.Labels, _state.ErrorCode, _state.Error);
            }
        }

        /// <summary>The server's machine-readable refusal reason, when it sent one.</summary>
        public string? ErrorCode
        {
            get { return _state.ErrorCode; }
        }

        /// <summary>Whether a failure can be retried from where it stopped.</summary>
        public bool CanRetry
        {
            get { return _state.Step == PlanChangeStep.Failed && _state.RetryFrom is not null; }
        }

        #endregion

        #region What the view calls

        /// <summary>Prices the chosen plan and opens the confirmation.</summary>
        public Task SelectTierAsync(TierSelectedEventArgs args)
        {
            if (args is null) return Task.CompletedTask;

            _selection = args;
            _entitlementsInvalidated = false;

            return DispatchAsync(new PlanChangeEvent.PreviewRequested(
                Settings.AppId, args.TierId, args.PricingId));
        }

        /// <summary>The customer confirmed the preview.</summary>
        public Task ConfirmAsync(TierChangeConfirmOptions options)
        {
            var confirmOptions = options ?? new TierChangeConfirmOptions();

            // An admin-scoped change bypasses payment server-side, so a card is only ever asked for
            // on the customer's own subscription.
            var adminScoped = Settings.UserId is { Length: > 0 } || Settings.CompanyId is { Length: > 0 };
            var preview = _state.Preview;

            var collectPayment = !adminScoped
                && preview is not null
                && preview.PaymentRequired
                && !confirmOptions.BypassPayment
                && !(_state.PaymentTransactionId is { Length: > 0 });

            return DispatchAsync(new PlanChangeEvent.Confirmed(collectPayment, confirmOptions.Immediate));
        }

        /// <summary>The customer backed out of the confirmation.</summary>
        public Task CancelAsync()
        {
            return DispatchAsync(new PlanChangeEvent.Reset());
        }

        /// <summary>
        /// The built-in modal's answer: a transaction id, or null when the customer closed it.
        /// Closing returns to the confirmation the customer came from, so the priced change is not
        /// thrown away.
        /// </summary>
        public Task ProvidePaymentAsync(string? paymentTransactionId)
        {
            return paymentTransactionId is { Length: > 0 }
                ? DispatchAsync(new PlanChangeEvent.PaymentCompleted(paymentTransactionId))
                : DispatchAsync(new PlanChangeEvent.PaymentCancelled());
        }

        /// <summary>Runs the failed step again, with the completion budget started over.</summary>
        public Task RetryAsync()
        {
            return DispatchAsync(new PlanChangeEvent.Retry());
        }

        /// <summary>Forgets the whole attempt.</summary>
        public Task ResetAsync()
        {
            return DispatchAsync(new PlanChangeEvent.Reset());
        }

        #endregion

        #region What the card modal is asked to collect

        /// <summary>
        /// What the host needs to collect payment for a tier change.
        /// </summary>
        /// <remarks>
        /// The payment starts the NEW plan's own subscription, billed at the plan's price, so the
        /// handler gets the pricing MODEL (not the tier-pricing link id) and the price and trial
        /// that subscription will have. Hosts used to be handed the link id and the prorated
        /// charge: the server found no pricing model for it, charged the prorated amount once, and
        /// left no recurring subscription, renewal or trial behind. The preview's numbers are the
        /// fallback for a plan whose own price did not reach us.
        /// </remarks>
        public static PaymentRequiredArgs BuildPaymentRequiredArgs(
            TierSelectedEventArgs args, TierChangePreviewModel? preview)
        {
            return new PaymentRequiredArgs
            {
                TierId = args.TierId,
                TierName = args.TierName,
                PricingId = args.PricingId,
                PricingModelId = args.PricingModelId,
                Price = args.Price > 0m
                    ? args.Price
                    : (preview?.NewPrice ?? preview?.ProratedChargeToday ?? 0m),
                TrialDays = args.TrialDays
            };
        }

        private PaymentRequiredArgs? BuildPaymentRequest()
        {
            if (_selection is null || _state.Step != PlanChangeStep.CollectingPayment) return null;
            return BuildPaymentRequiredArgs(_selection, _state.Preview);
        }

        #endregion

        #region Messages

        /// <summary>The message for a refusal the server gave a code for. TS <c>messageForCode</c>.</summary>
        public static string? MessageForCode(
            RegistrationSubscriptionLabels labels, string? errorCode, string? fallback)
        {
            switch (errorCode)
            {
                case TierChangeErrorCodes.PendingChangeExpired:
                    return labels.PlanChangeExpired;

                case TierChangeErrorCodes.PendingChangePaymentFailed:
                    return labels.PlanChangePaymentFailed;

                case TierChangeErrorCodes.PendingChangeSuperseded:
                    return labels.PlanChangeSuperseded;

                case TierChangeErrorCodes.PendingChangeNotFound:
                    return labels.PlanChangeNotFound;

                case TierChangeErrorCodes.TierChangeAlreadyInProgress:
                    return labels.PlanChangeInProgress;

                default:
                    return fallback;
            }
        }

        /// <summary>The <c>onError</c> code for a failure, preferring the server's own.</summary>
        public static string CodeForFailure(PlanChangeState state)
        {
            if (state.ErrorCode is { Length: > 0 }) return state.ErrorCode;

            switch (state.RetryFrom)
            {
                case PlanChangeStep.Previewing:
                    return PreviewFailedCode;

                case PlanChangeStep.Authenticating:
                    return AuthenticationFailedCode;

                case PlanChangeStep.Completing:
                    return CompletionFailedCode;

                default:
                    return ChangeFailedCode;
            }
        }

        #endregion

        #region The pump

        private async Task DispatchAsync(PlanChangeEvent changeEvent)
        {
            // Every dispatch comes from the VIEW (a plan click, a confirmation, a card answer, a
            // retry). Detached, there is no view, so there is nothing left that could raise one. A
            // step's own result does not come through here — it is applied straight onto the state.
            if (_detached) return;

            Apply(changeEvent);
            if (_pumping) return;

            await PumpAsync();
        }

        private bool Apply(PlanChangeEvent changeEvent)
        {
            var next = PlanChangeMachine.Transition(_state, changeEvent);
            if (ReferenceEquals(next, _state)) return false;

            _state = next;
            _dirty = true;

            // The steps the machine does not tokenise are claimed once PER ENTRY, so leaving one
            // arms it again — a second upgrade asks for a card again, and a second failure is
            // reported again.
            if (_state.Step != PlanChangeStep.CollectingPayment) _paymentAsked = false;
            if (_state.Step != PlanChangeStep.Done) _doneHandled = false;
            if (_state.Step != PlanChangeStep.Failed) _failureReported = false;

            return true;
        }

        private async Task PumpAsync()
        {
            _pumping = true;
            try
            {
                for (var iteration = 0; iteration < MaxPumpIterations; iteration++)
                {
                    // A dispose that landed while the last step was awaiting decides here what
                    // still runs: the drain, and nothing else.
                    if (!MayRunNextStep()) break;

                    Notify();
                    if (!await RunStepWorkAsync()) break;
                }
            }
            catch (Exception ex) when (_detached)
            {
                // A drain outlives the component, so the task this pump belongs to may have nobody
                // left to await it: an escaping throw would be an unobserved task exception rather
                // than an error anyone sees. Attached, the filter does not match and the throw
                // still propagates to the caller exactly as it always did.
                _logger?.LogError(ex, "The plan change could not be drained after teardown");
            }
            finally
            {
                _pumping = false;
                Notify();

                // The drain has stopped: settle the entitlement cache and drop the script instance.
                if (_detached) await FinishDetachedAsync();
            }
        }

        /// <summary>
        /// Whether the step the machine has landed on may run now.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Attached, always. Detached, only the two steps that are pure consequences of money that
        /// has ALREADY moved:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        /// <b>Completing</b> — the bank has said yes to the prorated charge, so it exists whether
        /// or not the browser is still there. <c>CompleteTierChangeAsync</c> is what turns it into
        /// the new plan, and skipping it is how a customer pays for an upgrade they never get.
        /// </description></item>
        /// <item><description>
        /// <b>Done</b> — the entitlement cache still has to be dropped; every host callback it
        /// would raise is already null.
        /// </description></item>
        /// </list>
        /// <para>
        /// Everything else needs a person who has left: <b>Previewing</b> prices a change nobody is
        /// watching, <b>CollectingPayment</b> wants a card, <b>Changing</b> is a change not yet
        /// posted, and <b>Authenticating</b> is a challenge not yet put to anyone. A result that
        /// was already in flight is still applied when it lands — the state stays true — it simply
        /// goes no further.
        /// </para>
        /// </remarks>
        private bool MayRunNextStep()
        {
            if (!_detached) return true;

            switch (_state.Step)
            {
                case PlanChangeStep.Completing:
                case PlanChangeStep.Done:
                    return true;

                default:
                    return false;
            }
        }

        private async Task<bool> RunStepWorkAsync()
        {
            switch (_state.Step)
            {
                case PlanChangeStep.Previewing:
                    if (!Claim("preview", _state.Token)) return false;
                    await RunPreviewAsync(_state.Token!);
                    return true;

                case PlanChangeStep.CollectingPayment:
                {
                    // Only the host's handler is driven from here; the built-in modal answers
                    // through ProvidePaymentAsync when the view renders it.
                    var handler = PaymentRequested;
                    if (handler is null || _paymentAsked) return false;

                    var request = BuildPaymentRequest();
                    if (request is null) return false;

                    _paymentAsked = true;
                    await RunHostPaymentAsync(handler, request);
                    return true;
                }

                case PlanChangeStep.Changing:
                    if (!Claim("change", _state.Token)) return false;
                    await RunChangeAsync(_state.Token!, _state.Immediate, _state.PaymentTransactionId);
                    return true;

                case PlanChangeStep.Authenticating:
                    if (!Claim("authenticate", _state.Token)) return false;
                    await RunAuthenticateAsync(_state.Token!, _state.ClientSecret);
                    return true;

                case PlanChangeStep.Completing:
                    if (!Claim("complete", _state.Token)) return false;
                    await RunCompleteAsync(_state.Token!, _state.PendingChangeId, _state.CompleteAttempts);
                    return true;

                case PlanChangeStep.Done:
                    if (_doneHandled) return false;
                    _doneHandled = true;
                    await RunDoneAsync();
                    return false;

                case PlanChangeStep.Failed:
                    if (_failureReported) return false;
                    _failureReported = true;
                    await ReportAsync(CodeForFailure(_state), Error ?? string.Empty);
                    return false;

                default:
                    return false;
            }
        }

        private async Task RunPreviewAsync(string token)
        {
            var chosen = _selection;
            if (chosen is null) return;

            try
            {
                // An admin managing one user previews against that user; self and company mode both
                // fall back to the self preview, there being no company-scoped preview endpoint.
                var preview = Settings.UserId is { Length: > 0 }
                    ? await _appTier.PreviewTierChangeAdminAsync(
                        Settings.AppId, Settings.UserId, chosen.TierId, chosen.PricingId)
                    : await _appTier.PreviewTierChangeAsync(Settings.AppId, chosen.TierId, chosen.PricingId);

                if (preview is null || !preview.Success)
                {
                    Apply(new PlanChangeEvent.PreviewFailed(
                        token,
                        preview?.ErrorMessage is { Length: > 0 } message ? message : PreviewRefused));
                    return;
                }

                Apply(new PlanChangeEvent.PreviewReceived(token, preview));
            }
            catch (Exception ex)
            {
                Apply(new PlanChangeEvent.PreviewFailed(
                    token, ex.Message is { Length: > 0 } ? ex.Message : PreviewRefused));
            }
        }

        /// <summary>
        /// Puts the card to the customer through the HOST's own modal. Its answer is final: the
        /// host owns that modal, so closing it is the customer walking away from the whole change,
        /// not a step back to a confirmation they have already dismissed.
        /// </summary>
        private async Task RunHostPaymentAsync(Func<PaymentRequiredArgs, Task<string?>> handler, PaymentRequiredArgs request)
        {
            try
            {
                var paymentTransactionId = await handler(request);

                if (!(paymentTransactionId is { Length: > 0 }))
                {
                    Apply(new PlanChangeEvent.Reset());
                    return;
                }

                Apply(new PlanChangeEvent.PaymentCompleted(paymentTransactionId));
            }
            catch (Exception ex)
            {
                Apply(new PlanChangeEvent.PaymentFailed(
                    ex.Message is { Length: > 0 } ? ex.Message : PaymentNotTaken));
            }
        }

        /// <summary>
        /// Posts the change. A self-service change goes through the OPTIONS form with
        /// <c>SupportsPaymentAction</c>, because this driver can confirm the charge and complete
        /// the parked change — so the server may park one instead of refusing it.
        /// </summary>
        private async Task RunChangeAsync(string token, bool immediate, string? paymentTransactionId)
        {
            var chosen = _selection;
            if (chosen is null) return;

            try
            {
                AppTierChangeResultModel result;

                // An admin-scoped change is authorised server-side and its endpoints carry no
                // transaction id, so no card is ever collected for one.
                if (Settings.UserId is { Length: > 0 })
                {
                    result = chosen.IsChange
                        ? await _appTier.ChangeUserTierAsync(
                            Settings.AppId, Settings.UserId, chosen.TierId, chosen.PricingId, immediate)
                        : await _appTier.SubscribeUserToTierAsync(
                            Settings.AppId, Settings.UserId, chosen.TierId, chosen.PricingId);
                }
                else if (Settings.CompanyId is { Length: > 0 })
                {
                    result = chosen.IsChange
                        ? await _appTier.ChangeCompanyTierAsync(
                            Settings.AppId, Settings.CompanyId, chosen.TierId, chosen.PricingId, immediate)
                        : await _appTier.SubscribeCompanyToTierAsync(
                            Settings.AppId, Settings.CompanyId, chosen.TierId, chosen.PricingId);
                }
                else if (chosen.IsChange)
                {
                    result = await _appTier.ChangeTierAsync(Settings.AppId, new SelfChangeTierOptions
                    {
                        NewTierId = chosen.TierId,
                        NewPricingId = chosen.PricingId,
                        Immediate = immediate,
                        PaymentTransactionId = paymentTransactionId,
                        SupportsPaymentAction = true
                    });
                }
                else
                {
                    result = await _appTier.SubscribeToTierAsync(
                        Settings.AppId, chosen.TierId, chosen.PricingId, paymentTransactionId);
                }

                Apply(new PlanChangeEvent.ChangeResult(token, result));
            }
            catch (Exception ex)
            {
                Apply(new PlanChangeEvent.ChangeFailed(
                    token, ex.Message is { Length: > 0 } ? ex.Message : ChangeRefused));
            }
        }

        /// <summary>
        /// Puts the prorated charge's 3-D Secure to the customer. The one step that can outlive the
        /// page: a bank challenge is a live interop call that teardown neither cancels nor waits
        /// for, so this may well resolve into a detached driver — a success is applied and DRAINED
        /// through <see cref="RunCompleteAsync"/>, a refusal is applied and goes no further.
        /// </summary>
        private async Task RunAuthenticateAsync(string token, string? clientSecret)
        {
            try
            {
                var key = await ResolvePublishableKeyAsync();
                if (!(key is { Length: > 0 }) || !(clientSecret is { Length: > 0 }))
                {
                    Apply(new PlanChangeEvent.AuthFailed(token, ChargeUnconfirmed));
                    return;
                }

                var outcome = await _paymentActions.ConfirmPaymentAsync(clientSecret, key);
                if (outcome.Succeeded)
                {
                    Apply(new PlanChangeEvent.Authenticated(token));
                    return;
                }

                // A cancel carries no message of its own, so the plain "your plan has not changed"
                // is the truthful thing to say.
                Apply(new PlanChangeEvent.AuthFailed(
                    token, outcome.Message is { Length: > 0 } message ? message : ChargeUnconfirmed));
            }
            catch (Exception ex)
            {
                Apply(new PlanChangeEvent.AuthFailed(
                    token, ex.Message is { Length: > 0 } ? ex.Message : ChargeUnconfirmed));
            }
        }

        /// <summary>
        /// Finishes the parked change. Safe to repeat — the server asks the processor whether the
        /// invoice really paid before it moves anything — so a <c>Processing</c> answer is asked
        /// again rather than reported, on the machine's bounded budget.
        /// </summary>
        private async Task RunCompleteAsync(string token, string? pendingChangeId, int attempt)
        {
            if (!(pendingChangeId is { Length: > 0 }))
            {
                Apply(new PlanChangeEvent.CompleteFailed(token, Settings.Labels.PlanChangeNotFound));
                return;
            }

            // Only a "processing" answer waits: the first attempt asks immediately.
            if (attempt > 0 && Settings.CompleteRetryDelay > TimeSpan.Zero)
            {
                await Task.Delay(Settings.CompleteRetryDelay);
            }

            try
            {
                var result = await _appTier.CompleteTierChangeAsync(Settings.AppId, pendingChangeId);
                Apply(new PlanChangeEvent.CompleteResult(token, result));
            }
            catch (Exception ex)
            {
                Apply(new PlanChangeEvent.CompleteFailed(
                    token, ex.Message is { Length: > 0 } ? ex.Message : CompletionRefused));
            }
        }

        /// <summary>
        /// The change landed. The entitlement cache goes first so anything the refresh triggers
        /// reads the new plan, then the host reloads, then the host's own callback is told why.
        /// </summary>
        private async Task RunDoneAsync()
        {
            InvalidateEntitlements();

            var changed = Changed;
            if (changed is not null)
            {
                try
                {
                    await changed();
                }
                catch (Exception ex)
                {
                    // The host's own refresh failing is not this flow's failure: the plan HAS changed.
                    _logger?.LogWarning(ex, "The host's refresh failed after a plan change");
                }
            }

            var entitlements = EntitlementsChanged;
            if (entitlements is not null) await entitlements(EntitlementsChangedReasons.TierChange);
        }

        /// <summary>
        /// The Stripe account the prorated charge was created on: the app's default Stripe
        /// provider, else its first enabled one with a key. Looked up once, then reused.
        /// </summary>
        private async Task<string?> ResolvePublishableKeyAsync()
        {
            if (_keyLookupDone) return _publishableKey;
            _keyLookupDone = true;

            try
            {
                var config = await _payments.GetAppPaymentConfigurationAsync(Settings.AppId);
                var providers = config?.Providers;
                if (providers is null) return _publishableKey;

                var defaultProviderId = config?.DefaultProviderId;
                PaymentProviderDto? isDefault = null;
                PaymentProviderDto? first = null;

                foreach (var provider in providers)
                {
                    if (provider is null
                        || provider.ProviderType != (int)PaymentProviderType.Stripe
                        || !provider.IsEnabled
                        || !(provider.PublishableKey is { Length: > 0 }))
                    {
                        continue;
                    }

                    if (defaultProviderId is { Length: > 0 }
                        && string.Equals(provider.Id, defaultProviderId, StringComparison.Ordinal))
                    {
                        _publishableKey = provider.PublishableKey;
                        return _publishableKey;
                    }

                    if (isDefault is null && provider.IsDefault) isDefault = provider;
                    first ??= provider;
                }

                _publishableKey = (isDefault ?? first)?.PublishableKey;
            }
            catch (Exception ex)
            {
                // Unknowable rather than fatal: the caller says the charge could not be confirmed.
                _logger?.LogWarning(ex, "Could not read the app's payment configuration");
            }

            return _publishableKey;
        }

        /// <summary>
        /// Drops the shared entitlement cache for the change that landed. Once per change, whether
        /// the view was still there or the change was drained after teardown: a plan that moved
        /// while a FeatureGate holds the old answer is a customer paying for something they cannot
        /// see.
        /// </summary>
        private void InvalidateEntitlements()
        {
            if (_entitlementsInvalidated || _entitlements is null) return;
            _entitlementsInvalidated = true;

            try
            {
                _entitlements.Invalidate(Settings.AppId, EntitlementsChangedReasons.TierChange);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not invalidate entitlements after a plan change");
            }
        }

        /// <summary>
        /// The end of a detached run: settle the entitlement cache for anything the drain landed,
        /// then drop the payment script instance. Runs exactly once, from whichever of
        /// <see cref="DisposeAsync"/> and the pump gets there last.
        /// </summary>
        private async Task FinishDetachedAsync()
        {
            if (_detachFinished) return;
            _detachFinished = true;

            if (_state.Step == PlanChangeStep.Done) InvalidateEntitlements();
            await ReleasePaymentActionsAsync();
        }

        /// <summary>
        /// Drops the keyed Stripe instance the adapter owns. Never before the drain: the instance
        /// is what a <c>confirmCardPayment</c> still in flight is running on.
        /// </summary>
        private async Task ReleasePaymentActionsAsync()
        {
            if (_paymentActions is not IAsyncDisposable disposable) return;

            try
            {
                await disposable.DisposeAsync();
            }
            catch (Exception ex)
            {
                // Teardown is not a place to throw from, and there is no view left to tell.
                _logger?.LogWarning(ex, "Could not drop the plan change's payment script instance");
            }
        }

        private Task ReportAsync(string code, string message)
        {
            var reported = ErrorReported;
            return reported is not null ? reported(code, message) : Task.CompletedTask;
        }

        /// <summary>One run per step token: a doubled dispatch carries a token already claimed.</summary>
        private bool Claim(string key, string? token)
        {
            if (!(token is { Length: > 0 })) return false;

            string? claimed;
            if (_runs.TryGetValue(key, out claimed) && string.Equals(claimed, token, StringComparison.Ordinal))
            {
                return false;
            }

            _runs[key] = token;
            return true;
        }

        private void Notify()
        {
            if (_detached || !_dirty) return;

            _dirty = false;
            StateChanged?.Invoke();
        }

        #endregion
    }
}
