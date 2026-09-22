using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription
{
    /// <summary>
    /// How an intent the customer has to answer is put to them. The pack checkout owns everything
    /// except this: a card challenge needs a payment SDK, and keeping it behind an interface is
    /// what lets the driver be tested without a browser.
    /// </summary>
    internal interface IPaymentActionAdapter
    {
        /// <summary>
        /// Confirms a PaymentIntent for a card ALREADY on file — 3-D Secure on a pack bought
        /// against the saved card. There is no card field to mount for this.
        /// </summary>
        /// <param name="clientSecret">The intent to confirm.</param>
        /// <param name="publishableKey">The Stripe account the intent belongs to.</param>
        Task<PaymentActionOutcome> ConfirmPaymentAsync(string clientSecret, string? publishableKey);
    }

    /// <summary>What a payment action came back with.</summary>
    internal sealed class PaymentActionOutcome
    {
        private PaymentActionOutcome(bool succeeded, string? message)
        {
            Succeeded = succeeded;
            Message = message;
        }

        public static readonly PaymentActionOutcome Success = new PaymentActionOutcome(true, null);

        public static PaymentActionOutcome Failed(string? message)
        {
            return new PaymentActionOutcome(false, message);
        }

        public bool Succeeded { get; }

        public string? Message { get; }
    }

    /// <summary>What a pack checkout is buying, and for whom.</summary>
    internal sealed class PackCheckoutSettings
    {
        public string AppId { get; set; } = string.Empty;

        /// <summary>The packs still to buy — the chosen ones, minus anything a token granted.</summary>
        public IReadOnlyList<AddOnCheckoutItemInput> Items { get; set; } = new List<AddOnCheckoutItemInput>();

        /// <summary>Pack names from the catalog, so an outcome can name a pack the quote never priced.</summary>
        public IReadOnlyDictionary<string, string>? Names { get; set; }

        public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;
    }

    /// <summary>
    /// Buying the packs a new account asked for: one quote, one card at most, one purchase, then
    /// any pack the bank wants authenticated, one at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from packages/wildwood-react-shared/src/registrationSubscription/usePackCheckoutFlow.ts.
    /// It runs AFTER the account exists and is signed in, because packs are bought as the user —
    /// the quote finds the customer the plan's payment already created, so a card taken minutes ago
    /// on the payment step is the saved card here and nothing is asked for twice.
    /// </para>
    /// <para>
    /// Every server call is keyed on the machine's step token, so a re-render, a double click and a
    /// payment callback that fires twice all land on a token the machine has moved past: the basket
    /// is quoted once and bought once.
    /// </para>
    /// <para>
    /// The card itself is the one thing this driver does not own. The card form mounts Stripe
    /// Elements and confirms the SetupIntent itself, then calls <see cref="CardConfirmedAsync"/> —
    /// the .NET spelling of the TypeScript's <c>hostCollectsCard</c>. The 3-D Secure a bought pack
    /// may still need has no field to mount, so that one goes through
    /// <see cref="IPaymentActionAdapter"/>.
    /// </para>
    /// </remarks>
    internal sealed class PackCheckoutDriver : IAsyncDisposable
    {
        /// <summary>Said when a pack's own 3-D Secure could not be put to the customer at all.</summary>
        public const string PackUnconfirmed = "This pack could not be confirmed with your bank.";

        /// <summary>Said when it was put to them and came back refused.</summary>
        public const string CardUnconfirmed = "Your card was not confirmed.";

        /// <summary>Said when the server would not hand out a SetupIntent.</summary>
        public const string CardUnavailable = "A card could not be collected right now.";

        public const string QuoteFailedCode = "pack_quote_failed";
        public const string CardFailedCode = "pack_card_failed";
        public const string CheckoutFailedCode = "pack_checkout_failed";

        private const int MaxPumpIterations = 64;

        private readonly IAppTierComponentService _appTier;
        private readonly IPaymentProviderService _payments;
        private readonly IPaymentActionAdapter _paymentActions;
        private readonly IFeatureEntitlementService? _entitlements;
        private readonly ILogger? _logger;
        private readonly Dictionary<string, string?> _runs = new Dictionary<string, string?>(StringComparer.Ordinal);

        private PackCheckoutState _state;
        private string? _publishableKey;
        private bool _keyLookupDone;
        private bool _dirty;
        private bool _pumping;
        private bool _started;
        private bool _finished;

        /// <summary>The view has gone. See <see cref="DisposeAsync"/>: detach is not stop.</summary>
        private bool _detached;

        /// <summary>The drain is over and the payment script instance has been dropped.</summary>
        private bool _detachFinished;

        /// <summary>The host's <see cref="Finished"/> callback actually ran.</summary>
        private bool _outcomeDelivered;

        /// <param name="appTier">Quotes, buys and completes the packs.</param>
        /// <param name="payments">Reads the app's payment configuration, for the publishable key.</param>
        /// <param name="paymentActions">Puts a bought pack's 3-D Secure to the customer.</param>
        /// <param name="settings">What is being bought, and for whom.</param>
        /// <param name="entitlements">
        /// The scoped entitlement cache, told when a purchase this driver DRAINED after teardown
        /// granted something — the host callback that would normally say so is gone by then. Null
        /// in tests that do not exercise the drain.
        /// </param>
        /// <param name="logger">Where a drain's own failures go, there being no view left to show them.</param>
        public PackCheckoutDriver(
            IAppTierComponentService appTier,
            IPaymentProviderService payments,
            IPaymentActionAdapter paymentActions,
            PackCheckoutSettings settings,
            IFeatureEntitlementService? entitlements = null,
            ILogger? logger = null)
        {
            _appTier = appTier;
            _payments = payments;
            _paymentActions = paymentActions;
            _entitlements = entitlements;
            _logger = logger;
            Settings = settings;

            _state = PackCheckoutMachine.InitialState(new PackCheckoutMachineOptions
            {
                AppId = settings.AppId,
                Items = settings.Items
            });
        }

        #region Host wiring

        public PackCheckoutSettings Settings { get; }

        /// <summary>Re-render. Synchronous, because <c>StateHasChanged</c> is.</summary>
        public Action? StateChanged { get; set; }

        /// <summary>Every requested pack's outcome, including the ones that failed or were skipped.</summary>
        public Func<IReadOnlyList<SignupPackOutcome>, Task>? Finished { get; set; }

        public Func<string, string, Task>? ErrorReported { get; set; }

        /// <summary>
        /// DETACHES the checkout from the view, and lets whatever the bank has already been asked
        /// drain to its end in the background.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Detach is not stop. Clearing the host callbacks is the easy half: a server answer still
        /// in flight must not re-render or report into a component that has left the page, and no
        /// NEW work that needs a person — a fresh quote, a new card, a purchase not yet issued, a
        /// 3-D Secure challenge not yet put to anyone — may be started for a visitor who is gone.
        /// </para>
        /// <para>
        /// The hard half is what has ALREADY moved money. A 3-D Secure confirmation is a live
        /// interop call: teardown does not cancel it, and the bank answers it minutes later
        /// regardless. Abandoning that answer would charge the customer's card and then never call
        /// <c>CompleteAddOnCheckoutAsync</c>, so the pack is paid for and never granted, with no
        /// message either way. So an authenticated pack is DRAINED — server-side only, no
        /// re-render, no host callback — through its completion; see
        /// <see cref="MayRunNextStep"/> for exactly which steps that covers.
        /// </para>
        /// <para>
        /// Packs still waiting on a challenge are left as the server has them: not authenticated,
        /// not completed. That is settled without the browser — the platform cancels orphaned
        /// add-on checkout transactions at Stripe after 24 h, and the Stripe webhook reconciles any
        /// invoice that did get paid (GrantContractManagement,
        /// <c>Plan/20260917-0903-regsub-component-platform/plan.md</c>, Phase 1 section B,
        /// <c>TrialExpirationService</c>).
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
            Finished = null;
            ErrorReported = null;

            // Nothing is in flight, so there is nothing to drain: the script instance goes now.
            if (!_pumping) return new ValueTask(FinishDetachedAsync());

            // A step IS in flight. Its task is already running and the pump's own finally finishes
            // the detach when it stops, so the component's disposal is not held up behind it.
            return default;
        }

        #endregion

        #region What the view renders from

        public PackCheckoutState State
        {
            get { return _state; }
        }

        public PackCheckoutStep Step
        {
            get { return _state.Step; }
        }

        /// <summary>The Stripe account the SetupIntent belongs to, for the card form to mount with.</summary>
        public string? PublishableKey
        {
            get { return _publishableKey; }
        }

        /// <summary>The SetupIntent the card form has to confirm.</summary>
        public string? CardClientSecret
        {
            get { return _state.CardClientSecret; }
        }

        /// <summary>Whether a server call is in flight.</summary>
        public bool Busy
        {
            get
            {
                return _state.Step == PackCheckoutStep.CheckingOut
                    || _state.Step == PackCheckoutStep.Authenticating
                    || _state.Step == PackCheckoutStep.Completing;
            }
        }

        /// <summary>
        /// The pack whose card is being authenticated, named, for the status line. Empty rather
        /// than an id when nothing named it: a raw GUID in "Confirming … with your bank" reads as
        /// a bug, and the sentence still says what is happening without it.
        /// </summary>
        public string AuthenticatingName
        {
            get
            {
                var item = PackCheckoutMachine.CurrentPackCheckoutItem(_state);
                if (item is null) return string.Empty;

                if (Settings.Names is not null)
                {
                    string? known;
                    if (Settings.Names.TryGetValue(item.AddOnId, out known) && known is { Length: > 0 }) return known;
                }

                return QuotedName(item.AddOnId) ?? string.Empty;
            }
        }

        #endregion

        #region What the view calls

        /// <summary>Prices the basket and buys it. Runs once per checkout.</summary>
        public async Task StartAsync()
        {
            if (_started || _detached) return;
            _started = true;

            await DispatchAsync(new PackCheckoutEvent.QuoteRequested(Settings.AppId, Settings.Items));
        }

        /// <summary>The card form confirmed the SetupIntent.</summary>
        public Task CardConfirmedAsync()
        {
            return DispatchAsync(new PackCheckoutEvent.CardConfirmed(
                _state.Token ?? string.Empty, _state.PaymentTransactionId));
        }

        /// <summary>The card form could not confirm it.</summary>
        public async Task CardFailedAsync(string message)
        {
            await ReportAsync(CardFailedCode, message);
            await DispatchAsync(new PackCheckoutEvent.CardFailed(_state.Token ?? string.Empty, message));
        }

        /// <summary>Runs the failed step again. A card is always collected afresh.</summary>
        public Task RetryAsync()
        {
            return DispatchAsync(new PackCheckoutEvent.Retry());
        }

        /// <summary>
        /// Gives up on the packs and lets the signup finish: they are reported as failed, not
        /// forgotten.
        /// </summary>
        public async Task SkipAsync()
        {
            if (_finished) return;
            _finished = true;

            await RaiseFinishedAsync(ToOutcomes(
                Settings.Items, _state.Results, _state.Quote, Settings.Names, _state.Error));
        }

        #endregion

        #region Outcomes

        /// <summary>
        /// One outcome per requested pack, so nothing the visitor asked for goes unmentioned. TS
        /// <c>toPackOutcomes</c>.
        /// </summary>
        public static List<SignupPackOutcome> ToOutcomes(
            IReadOnlyList<AddOnCheckoutItemInput> items,
            IReadOnlyList<AddOnCheckoutItemResultModel> results,
            AddOnCheckoutQuoteModel? quote,
            IReadOnlyDictionary<string, string>? names,
            string? fallbackMessage)
        {
            var outcomes = new List<SignupPackOutcome>();
            if (items is null) return outcomes;

            foreach (var item in items)
            {
                AddOnCheckoutItemResultModel? result = null;
                if (results is not null)
                {
                    foreach (var candidate in results)
                    {
                        if (string.Equals(candidate.AddOnId, item.AddOnId, StringComparison.Ordinal))
                        {
                            result = candidate;
                            break;
                        }
                    }
                }

                AddOnCheckoutQuoteLineModel? line = null;
                if (quote is not null)
                {
                    foreach (var candidate in quote.Lines)
                    {
                        if (string.Equals(candidate.AddOnId, item.AddOnId, StringComparison.Ordinal))
                        {
                            line = candidate;
                            break;
                        }
                    }
                }

                var name = ResolveName(names, item.AddOnId, line?.Name);

                var status = result?.Status;
                if (string.Equals(status, AddOnCheckoutItemStatuses.Trialing, StringComparison.Ordinal)
                    || string.Equals(status, AddOnCheckoutItemStatuses.Active, StringComparison.Ordinal))
                {
                    outcomes.Add(new SignupPackOutcome
                    {
                        AddOnId = item.AddOnId,
                        Name = name,
                        Status = status!,
                        TrialEnd = result?.TrialEnd
                    });
                    continue;
                }

                outcomes.Add(new SignupPackOutcome
                {
                    AddOnId = item.AddOnId,
                    Name = name,
                    Status = SignupPackStatuses.Failed,
                    ErrorMessage = result?.ErrorMessage ?? fallbackMessage
                });
            }

            return outcomes;
        }

        private static string ResolveName(
            IReadOnlyDictionary<string, string>? names,
            string addOnId,
            string? quotedName)
        {
            if (names is not null)
            {
                string? known;
                if (names.TryGetValue(addOnId, out known) && known is { Length: > 0 }) return known;
            }

            if (quotedName is { Length: > 0 }) return quotedName;
            return addOnId;
        }

        private string? QuotedName(string addOnId)
        {
            if (_state.Quote is null) return null;

            foreach (var line in _state.Quote.Lines)
            {
                if (string.Equals(line.AddOnId, addOnId, StringComparison.Ordinal)) return line.Name;
            }

            return null;
        }

        #endregion

        #region The pump

        private async Task DispatchAsync(PackCheckoutEvent checkoutEvent)
        {
            // Every dispatch comes from the VIEW (start, a confirmed card, retry). Detached, there
            // is no view, so there is nothing left that could have raised one. The drain does not
            // come through here: a step's own result is applied straight onto the state.
            if (_detached) return;

            Apply(checkoutEvent);
            if (_pumping) return;

            await PumpAsync();
        }

        private bool Apply(PackCheckoutEvent checkoutEvent)
        {
            var next = PackCheckoutMachine.Transition(_state, checkoutEvent);
            if (ReferenceEquals(next, _state)) return false;

            _state = next;
            _dirty = true;
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
                _logger?.LogError(ex, "The pack checkout could not be drained after teardown");
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
        /// <b>Completing</b> — the bank has said yes to this pack, so the charge exists whether or
        /// not the browser is still there. <c>CompleteAddOnCheckoutAsync</c> is what turns it into
        /// a subscription, and skipping it is how a customer pays for a pack they never get.
        /// </description></item>
        /// <item><description>
        /// <b>Done</b> — bookkeeping only; the host callback it would raise is already null.
        /// </description></item>
        /// </list>
        /// <para>
        /// Everything else needs a person who has left: <b>Quoting</b> and <b>Quoted</b> would
        /// price and then BUY a basket nobody is watching, <b>CollectingCard</b> wants a card
        /// field, <b>CheckingOut</b> is a purchase not yet issued, and <b>Authenticating</b> is a
        /// challenge not yet put to anyone. A result that was already in flight is still applied
        /// when it lands — the state stays true — it simply goes no further.
        /// </para>
        /// </remarks>
        private bool MayRunNextStep()
        {
            if (!_detached) return true;

            switch (_state.Step)
            {
                case PackCheckoutStep.Completing:
                case PackCheckoutStep.Done:
                    return true;

                default:
                    return false;
            }
        }

        private async Task<bool> RunStepWorkAsync()
        {
            switch (_state.Step)
            {
                case PackCheckoutStep.Quoting:
                    if (!Claim("quote", _state.Token)) return false;
                    await RunQuoteAsync(_state.Token!);
                    return true;

                case PackCheckoutStep.Quoted:
                    // A quote that needs no new card is bought against the one already on file.
                    return Apply(_state.Quote is not null && _state.Quote.RequiresPaymentMethod
                        ? (PackCheckoutEvent)new PackCheckoutEvent.CardRequested()
                        : new PackCheckoutEvent.CheckoutRequested());

                case PackCheckoutStep.CollectingCard:
                    if (!Claim("card", _state.Token)) return false;
                    await RunCardIntentAsync(_state.Token!);
                    return true;

                case PackCheckoutStep.CheckingOut:
                    if (_state.Quote is null || !Claim("checkout", _state.Token)) return false;
                    await RunCheckoutAsync(_state.Token!, _state.Quote);
                    return true;

                case PackCheckoutStep.Authenticating:
                {
                    var item = PackCheckoutMachine.CurrentPackCheckoutItem(_state);
                    if (item is null || !Claim("auth:" + _state.PendingPosition, _state.Token)) return false;
                    await RunAuthenticateAsync(_state.Token!, item);
                    return true;
                }

                case PackCheckoutStep.Completing:
                {
                    var item = PackCheckoutMachine.CurrentPackCheckoutItem(_state);
                    if (item is null || !Claim("complete:" + _state.PendingPosition, _state.Token)) return false;
                    await RunCompleteAsync(_state.Token!, item);
                    return true;
                }

                case PackCheckoutStep.Done:
                    if (_finished) return false;
                    _finished = true;
                    await RaiseFinishedAsync(ToOutcomes(
                        Settings.Items, _state.Results, _state.Quote, Settings.Names, null));
                    return false;

                default:
                    return false;
            }
        }

        private async Task RunQuoteAsync(string token)
        {
            try
            {
                var quote = await _appTier.QuoteAddOnCheckoutAsync(Settings.AppId, Settings.Items);
                if (quote is null || !quote.Success)
                {
                    await ReportAsync(
                        QuoteFailedCode,
                        quote?.ErrorMessage is { Length: > 0 } message ? message : Settings.Labels.PacksUnavailable);
                }

                Apply(new PackCheckoutEvent.QuoteReceived(token, quote));
            }
            catch (Exception ex)
            {
                var message = ex.Message is { Length: > 0 } ? ex.Message : Settings.Labels.PacksUnavailable;
                await ReportAsync(QuoteFailedCode, message);
                Apply(new PackCheckoutEvent.QuoteFailed(token, message));
            }
        }

        /// <summary>
        /// Starts the one-off card entry: a SetupIntent and the Stripe account it belongs to, both
        /// of which the card form needs before it can mount anything.
        /// </summary>
        private async Task RunCardIntentAsync(string token)
        {
            try
            {
                var providerId = _state.Quote?.ProviderId;

                // The key first: without one there is no card field to mount, and creating the
                // SetupIntent before finding that out leaves an intent on the account that nobody
                // will ever confirm.
                var key = await ResolvePublishableKeyAsync(providerId);
                if (!(key is { Length: > 0 }))
                {
                    await ReportAsync(CardFailedCode, CardUnavailable);
                    Apply(new PackCheckoutEvent.CardIntentFailed(token, CardUnavailable));
                    return;
                }

                var intent = await _appTier.CreateCheckoutPaymentMethodAsync(Settings.AppId, providerId ?? string.Empty);

                if (intent is null
                    || !intent.Success
                    || !(intent.ClientSecret is { Length: > 0 })
                    || !(intent.PaymentTransactionId is { Length: > 0 }))
                {
                    var message = intent?.ErrorMessage is { Length: > 0 } reason ? reason : CardUnavailable;
                    await ReportAsync(CardFailedCode, message);
                    Apply(new PackCheckoutEvent.CardIntentFailed(token, message, intent?.ErrorCode));
                    return;
                }

                _publishableKey = key;
                Apply(new PackCheckoutEvent.CardIntentReceived(token, intent.ClientSecret, intent.PaymentTransactionId));
            }
            catch (Exception ex)
            {
                var message = ex.Message is { Length: > 0 } ? ex.Message : CardUnavailable;
                await ReportAsync(CardFailedCode, message);
                Apply(new PackCheckoutEvent.CardIntentFailed(token, message));
            }
        }

        /// <summary>
        /// Buys the basket — ONE call, whatever its size, carrying the quote's checkout id so a
        /// repeat cannot buy the same pack twice.
        /// </summary>
        private async Task RunCheckoutAsync(string token, AddOnCheckoutQuoteModel quote)
        {
            try
            {
                var request = new AddOnCheckoutRequestModel
                {
                    CheckoutId = quote.CheckoutId,
                    ProviderId = quote.ProviderId ?? string.Empty,
                    Items = new List<AddOnCheckoutItemInput>(Settings.Items)
                };

                if (_state.UseSavedCard)
                {
                    request.UseSavedCard = true;
                }
                else
                {
                    request.PaymentTransactionId = _state.PaymentTransactionId;
                }

                var result = await _appTier.CheckoutAddOnsAsync(Settings.AppId, request);

                var attempted = result is not null && result.Results is not null && result.Results.Count > 0;
                if ((result is null || !result.Success) && !attempted)
                {
                    await ReportAsync(
                        CheckoutFailedCode,
                        result?.ErrorMessage is { Length: > 0 } message ? message : Settings.Labels.PacksUnavailable);
                }

                Apply(new PackCheckoutEvent.CheckoutReceived(token, result));
            }
            catch (Exception ex)
            {
                var message = ex.Message is { Length: > 0 } ? ex.Message : Settings.Labels.PacksUnavailable;
                await ReportAsync(CheckoutFailedCode, message);
                Apply(new PackCheckoutEvent.CheckoutFailed(token, message));
            }
        }

        /// <summary>
        /// Puts one pack's 3-D Secure to the customer. A refusal marks only THAT pack: a basket is
        /// not a transaction.
        /// </summary>
        /// <remarks>
        /// The one step that can outlive the page. A bank challenge is a live interop call that
        /// teardown neither cancels nor waits for, so this may well resolve into a detached driver:
        /// a success is applied and DRAINED through <see cref="RunCompleteAsync"/>, a refusal is
        /// applied and goes no further. See <see cref="DisposeAsync"/>.
        /// </remarks>
        private async Task RunAuthenticateAsync(string token, AddOnCheckoutItemResultModel item)
        {
            try
            {
                var key = await ResolvePublishableKeyAsync(_state.Quote?.ProviderId);
                if (!(key is { Length: > 0 }) || !(item.ClientSecret is { Length: > 0 }))
                {
                    Apply(new PackCheckoutEvent.ItemAuthFailed(token, PackUnconfirmed));
                    return;
                }

                var outcome = await _paymentActions.ConfirmPaymentAsync(item.ClientSecret, key);
                if (outcome.Succeeded)
                {
                    Apply(new PackCheckoutEvent.ItemAuthenticated(token));
                    return;
                }

                Apply(new PackCheckoutEvent.ItemAuthFailed(
                    token, outcome.Message is { Length: > 0 } message ? message : CardUnconfirmed));
            }
            catch (Exception ex)
            {
                var message = ex.Message is { Length: > 0 } ? ex.Message : CardUnconfirmed;
                Apply(new PackCheckoutEvent.ItemAuthFailed(token, message));
            }
        }

        /// <summary>
        /// Finishes one authenticated pack. Like every other step's work, an unexpected throw is
        /// reported as THAT pack failing rather than escaping the pump — the service swallows its
        /// own failures, so anything arriving here is a surprise, and a surprise that leaves the
        /// pump would strand the basket mid-flight with no outcome for any pack in it.
        /// </summary>
        private async Task RunCompleteAsync(string token, AddOnCheckoutItemResultModel item)
        {
            try
            {
                var result = await _appTier.CompleteAddOnCheckoutAsync(
                    Settings.AppId, item.PaymentTransactionId ?? string.Empty);

                // The server answers a refusal with the item itself, so its addOnId is kept
                // whatever happened.
                if (!(result.AddOnId is { Length: > 0 })) result.AddOnId = item.AddOnId;

                Apply(new PackCheckoutEvent.ItemCompleted(token, result));
            }
            catch (Exception ex)
            {
                // Completing is the step AFTER the bank said yes, so the machine has no "failed"
                // event of its own here: the pack is completed AS failed, which marks only this
                // pack and moves the basket on, exactly as a refused 3-D Secure does.
                Apply(new PackCheckoutEvent.ItemCompleted(token, new AddOnCheckoutItemResultModel
                {
                    AddOnId = item.AddOnId,
                    Status = AddOnCheckoutItemStatuses.Failed,
                    ErrorMessage = ex.Message is { Length: > 0 } message ? message : PackUnconfirmed
                }));
            }
        }

        /// <summary>The Stripe account the quote's provider belongs to. Looked up once, then reused.</summary>
        private async Task<string?> ResolvePublishableKeyAsync(string? providerId)
        {
            if (_keyLookupDone) return _publishableKey;
            _keyLookupDone = true;

            try
            {
                var config = await _payments.GetAppPaymentConfigurationAsync(Settings.AppId);
                var providers = config?.Providers;
                if (providers is null) return _publishableKey;

                foreach (var provider in providers)
                {
                    if (provider is not null && string.Equals(provider.Id, providerId, StringComparison.Ordinal))
                    {
                        _publishableKey = provider.PublishableKey;
                        return _publishableKey;
                    }
                }

                foreach (var provider in providers)
                {
                    if (provider is not null && provider.IsEnabled && provider.PublishableKey is { Length: > 0 })
                    {
                        _publishableKey = provider.PublishableKey;
                        return _publishableKey;
                    }
                }
            }
            catch
            {
                // Unknowable rather than fatal: the caller reports "a card could not be collected".
            }

            return _publishableKey;
        }

        private Task RaiseFinishedAsync(IReadOnlyList<SignupPackOutcome> outcomes)
        {
            var finished = Finished;
            if (finished is null) return Task.CompletedTask;

            _outcomeDelivered = true;
            return finished(outcomes);
        }

        /// <summary>
        /// The end of a detached run: tell the entitlement cache what the drain bought, then drop
        /// the payment script instance. Runs exactly once, from whichever of
        /// <see cref="DisposeAsync"/> and the pump gets there last.
        /// </summary>
        private async Task FinishDetachedAsync()
        {
            if (_detachFinished) return;
            _detachFinished = true;

            InvalidateDrainedEntitlements();
            await ReleasePaymentActionsAsync();
        }

        /// <summary>
        /// A pack that ended up bought after the view left still changed what the account is
        /// entitled to, and the callback that normally says so was cleared by the detach — so the
        /// scoped entitlement service is told directly, and gates elsewhere in the app stop
        /// serving the old plan.
        /// </summary>
        private void InvalidateDrainedEntitlements()
        {
            // The host DID get the outcome, so whoever owns it (the signup flow) invalidates:
            // saying it twice for one purchase would be noise.
            if (_outcomeDelivered || _entitlements is null) return;

            var granted = false;
            foreach (var result in _state.Results)
            {
                if (string.Equals(result.Status, AddOnCheckoutItemStatuses.Active, StringComparison.Ordinal)
                    || string.Equals(result.Status, AddOnCheckoutItemStatuses.Trialing, StringComparison.Ordinal))
                {
                    granted = true;
                    break;
                }
            }

            if (!granted) return;

            try
            {
                _entitlements.Invalidate(Settings.AppId, EntitlementsChangedReasons.AddOn);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not invalidate entitlements after a drained pack checkout");
            }
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
                _logger?.LogWarning(ex, "Could not drop the pack checkout's payment script instance");
            }
        }

        private Task ReportAsync(string code, string message)
        {
            return ErrorReported is not null ? ErrorReported(code, message) : Task.CompletedTask;
        }

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
