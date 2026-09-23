using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;
using static WildwoodComponents.Blazor.Components.Registration.TokenRegistrationComponent;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription
{
    /// <summary>How a signup flow is configured. The view copies its parameters onto one of these.</summary>
    internal sealed class SignupFlowSettings
    {
        public string AppId { get; set; } = string.Empty;

        public string? Currency { get; set; }

        public string? PreSelectedTierId { get; set; }

        public string? PreSelectedPricingId { get; set; }

        public IReadOnlyList<string>? PreSelectedAddOnIds { get; set; }

        public string? RegistrationToken { get; set; }

        public string? PrefillEmail { get; set; }

        public SignupPlanSelection PlanSelection { get; set; } = SignupPlanSelection.Choose;

        /// <summary>
        /// The plan the grid OPENS on when nothing has chosen one. A highlight only: the machine
        /// never sees it, and the visitor still confirms with a click.
        /// </summary>
        public SignupPlanDefault PlanDefault { get; set; } = SignupPlanDefault.None;

        /// <summary>
        /// Whether the pack step is offered. Default <see cref="SignupPackSelection.None"/>, which
        /// is React's default too — and which only removes the STEP: packs a signup link chose are
        /// still bought.
        /// </summary>
        public SignupPackSelection PackSelection { get; set; } = SignupPackSelection.None;

        public SignupTokenMode TokenMode { get; set; } = SignupTokenMode.Auto;

        public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

        /// <summary>A catalog the host already read, so the first paint carries real prices.</summary>
        public PublicCatalog? PreloadedCatalog { get; set; }
    }

    /// <summary>
    /// The half of the signup that has to touch the world: it reads the app's registration mode and
    /// catalog, runs the server calls each step asks for, and feeds the results back into
    /// <see cref="SignupMachine"/>. The view only renders <see cref="State"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from packages/wildwood-react-shared/src/registrationSubscription/useSignupFlow.ts.
    /// Two rules keep it honest, and they are the same two the TypeScript states:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <b>Every async step is claimed by the machine's step token before it starts.</b> A double
    /// click, a re-render and a callback that fires twice all land on a token that has already
    /// been claimed, so nothing registers, charges or buys twice.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>The account creation is <see cref="ISignupAccountCreator"/>'s</b> — the signup wizard's
    /// <c>ProcessSignupAsync</c>, lifted rather than copied — and the sub-steps it completed live
    /// on a <see cref="SignupAccountAttempt"/> that survives a failure, so "Try Again" resumes
    /// instead of registering the same person again or charging a second card.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// Blazor is a WEB stack, so the order is PAY-FIRST
    /// (<see cref="SignupPaymentOrder.BeforeAccount"/>): the plan's card is taken before the
    /// account exists, because a declined card then leaves nothing behind. The machine's
    /// account-first option is for the store-billed stacks and is not offered here.
    /// </para>
    /// <para>No LINQ: this runs inside Blazor components and LINQ breaks iOS/MAUI at runtime.</para>
    /// </remarks>
    internal sealed class SignupFlowDriver
    {
        /// <summary>
        /// A ceiling on one pump: every iteration either advances the machine or stops, so this is
        /// only reached by a bug. Breaking beats spinning a circuit forever.
        /// </summary>
        private const int MaxPumpIterations = 32;

        private readonly IPublicCatalogService _catalogService;
        private readonly IAuthenticationService _authService;
        private readonly IFeatureEntitlementService _entitlements;
        private readonly IDisclaimerService _disclaimerService;
        private readonly ISignupAccountCreator _accountCreator;
        private readonly IWildwoodSessionManager _sessionManager;
        private readonly ILogger? _logger;

        private readonly SignupAccountAttempt _attempt = new SignupAccountAttempt();
        private readonly Dictionary<string, string?> _runs = new Dictionary<string, string?>(StringComparer.Ordinal);

        private SignupState _state;
        private bool _dirty;
        private bool _pumping;
        private bool _started;

        /// <summary>The view has gone. See <see cref="Stop"/>: detach is not abandon.</summary>
        private bool _detached;

        /// <summary>The detached run has been settled up. Once only.</summary>
        private bool _detachFinished;

        private SignupRegistrationMode.Result _mode;
        private PublicCatalog? _catalog;
        private RegistrationFormData? _formData;
        private RegistrationTokenAppGrant? _tokenGrant;
        private string? _tokenMessage;
        private string _processingStatus = string.Empty;
        private SignupPlanView? _chosenPlan;
        private List<string> _selectedPackIds = new List<string>();
        private PricingBilling _billing = PricingBilling.Monthly;
        private string? _paymentTransactionId;
        private string? _paymentExternalId;
        private List<PendingDisclaimerModel>? _pendingDisclaimers;
        private bool _signedInNotified;
        private bool _entitlementsNotified;
        private bool _completeNotified;

        public SignupFlowDriver(
            IPublicCatalogService catalogService,
            IAuthenticationService authService,
            IFeatureEntitlementService entitlements,
            IDisclaimerService disclaimerService,
            ISignupAccountCreator accountCreator,
            IWildwoodSessionManager sessionManager,
            SignupFlowSettings settings,
            ILogger? logger = null)
        {
            _catalogService = catalogService;
            _authService = authService;
            _entitlements = entitlements;
            _disclaimerService = disclaimerService;
            _accountCreator = accountCreator;
            _sessionManager = sessionManager;
            Settings = settings;
            _logger = logger;

            _mode = SignupRegistrationMode.Resolve(null, settings.TokenMode);
            _state = SignupMachine.InitialState(new SignupMachineOptions
            {
                TokenMode = settings.TokenMode,
                PlanSelection = settings.PlanSelection,
                PackSelection = settings.PackSelection,

                // Web: nothing is created until the money is in.
                PaymentOrder = SignupPaymentOrder.BeforeAccount
            });
        }

        #region Host wiring

        /// <summary>What the view configured this flow with.</summary>
        public SignupFlowSettings Settings { get; }

        /// <summary>Re-render. Synchronous, because <c>StateHasChanged</c> is.</summary>
        public Action? StateChanged { get; set; }

        /// <summary>Raised with a code and a message whenever the flow gives up on something.</summary>
        public Func<string, string, Task>? ErrorReported { get; set; }

        /// <summary>Raised once, when the visitor turned out to be signed in already.</summary>
        public Func<Task>? AlreadySignedInDetected { get; set; }

        /// <summary>Raised once the new account's entitlements have been invalidated.</summary>
        public Func<Task>? EntitlementsChanged { get; set; }

        /// <summary>Raised when the visitor leaves the finished signup.</summary>
        public Func<SignupOutcome, Task>? SignupCompleted { get; set; }

        /// <summary>
        /// DETACHES the flow from the view: no more re-renders, no more host callbacks, and no new
        /// step work — but nothing already under way is abandoned.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Called when the view leaves the page. A signup makes long server calls — a catalog, a
        /// registration, a sign-in — and any of them can answer after the component is gone;
        /// clearing the delegates here is what keeps that answer from re-rendering or reporting
        /// into a component that no longer exists.
        /// </para>
        /// <para>
        /// What it deliberately does NOT do is cut the <c>creating</c> step short. Account creation
        /// is one awaited <see cref="ISignupAccountCreator.CreateAsync"/> call that registers, signs
        /// in, LINKS THE PLAN'S PAYMENT and subscribes; the card was taken before any of it
        /// (pay-first), so a teardown between the charge and the link would strand a paid
        /// transaction attached to nobody. Nothing here cancels that call and nothing waits on it:
        /// it runs to its end in the background on the task that started it, writing its progress
        /// onto the retry-safe <see cref="SignupAccountAttempt"/> as usual, while
        /// <see cref="Notify"/> and every host callback stay silent. The pump then stops at the
        /// next step, because every one of those needs a person who has left.
        /// </para>
        /// <para>
        /// The one thing still owed afterwards is the entitlement cache: an account that got
        /// created is entitled to what it paid for, so <see cref="FinishDetached"/> tells the
        /// scoped service — a service call, not a component callback — wherever the flow had got
        /// to. Synchronous, because this driver holds no browser resources: the only one in the
        /// signup is the pack checkout's Stripe instance, and <c>PackCheckout</c> owns that.
        /// </para>
        /// </remarks>
        public void Stop()
        {
            if (_detached) return;
            _detached = true;

            StateChanged = null;
            ErrorReported = null;
            AlreadySignedInDetected = null;
            EntitlementsChanged = null;
            SignupCompleted = null;

            // Nothing in flight: settle up now. Otherwise the pump's own finally does it when the
            // call that is running finishes.
            if (!_pumping) FinishDetached();
        }

        #endregion

        #region What the view renders from

        public SignupState State
        {
            get { return _state; }
        }

        /// <summary>The <c>data-ww-step</c> value: the machine's step, with <c>done</c> spelled out.</summary>
        public string StepName
        {
            get { return SignupViewDecisions.StepName(_state.Step); }
        }

        public RegistrationSubscriptionLabels Labels
        {
            get { return Settings.Labels; }
        }

        /// <summary>The visitor already had a session when the flow started.</summary>
        public bool AlreadySignedIn
        {
            get { return _state.Initialized && _state.AlreadySignedInLatched; }
        }

        public SignupRegistrationMode.Result Mode
        {
            get { return _state.Mode ?? _mode; }
        }

        public PublicCatalog? Catalog
        {
            get { return _catalog; }
        }

        /// <summary>The currency every price on screen is quoted in.</summary>
        public string Currency
        {
            get { return Settings.Currency ?? _catalog?.Currency ?? string.Empty; }
        }

        /// <summary>The plan the signup is carrying, or null until there is one.</summary>
        public SignupPlanView? Plan
        {
            get
            {
                if (_chosenPlan is not null) return _chosenPlan;
                return SignupViewDecisions.ResolvePlan(_catalog, _state.Selection.TierId, _state.Selection.PricingId);
            }
        }

        /// <summary>
        /// The plan the grid opens on when nothing has chosen one. A highlight only: the machine
        /// never sees it. TS <c>flow.defaultTierId</c>.
        /// </summary>
        public string? DefaultTierId
        {
            get
            {
                return SignupViewDecisions.DefaultTierId(
                    Settings.PlanDefault,
                    Settings.TokenMode == SignupTokenMode.Required,
                    _catalog);
            }
        }

        /// <summary>Whether a plan is still to be chosen, which is what the form's submit says.</summary>
        public bool PlanStepAhead
        {
            get
            {
                return SignupViewDecisions.PlanStepAhead(
                    Settings.PlanSelection,
                    Settings.TokenMode == SignupTokenMode.Required,
                    _state.PlanPreset);
            }
        }

        /// <summary>The grant this app's registration token carries, with the server's display names.</summary>
        public RegistrationTokenAppGrant? TokenGrant
        {
            get { return _tokenGrant; }
        }

        /// <summary>A rejected token, shown above the form.</summary>
        public string? TokenMessage
        {
            get { return _tokenMessage; }
        }

        public int TrialDays
        {
            get { return SignupViewDecisions.TrialDays(Plan); }
        }

        /// <summary>The account was created but the plan could not be activated.</summary>
        public bool SubscriptionFailed
        {
            get { return _attempt.SubscriptionFailed; }
        }

        /// <summary>What the flow is doing while the account is being created.</summary>
        public string ProcessingStatus
        {
            get { return _processingStatus; }
        }

        /// <summary>The registration form's initial values: the last attempt's, or the invitation's.</summary>
        public RegistrationFormData? InitialFormData
        {
            get
            {
                return SignupViewDecisions.InitialFormData(
                    _formData, Settings.PrefillEmail, Settings.RegistrationToken);
            }
        }

        /// <summary>The disclaimers the sign-in said are outstanding.</summary>
        public List<PendingDisclaimerModel>? PendingDisclaimers
        {
            get { return _pendingDisclaimers; }
        }

        /// <summary>The new account's id, once it exists.</summary>
        public string? UserId
        {
            get { return _attempt.AuthResponse?.Id; }
        }

        /// <summary>The packs still to buy after login.</summary>
        public List<AddOnCheckoutItemInput> CheckoutItems
        {
            get { return SignupViewDecisions.CheckoutItems(_state.PacksToBuy); }
        }

        /// <summary>Pack names from the catalog, for outcomes the quote never priced.</summary>
        public IReadOnlyDictionary<string, string>? PackNames
        {
            get { return _state.Names.AddOns; }
        }

        /// <summary>Everything the app sells that the token did not grant.</summary>
        public List<AppTierAddOnModel> AvailablePacks
        {
            get { return SignupViewDecisions.AvailablePacks(_catalog, _tokenGrant?.AddOnIds); }
        }

        public IReadOnlyList<string> SelectedPackIds
        {
            get { return _selectedPackIds; }
        }

        public PricingBilling Billing
        {
            get { return _billing; }
        }

        public SignupOutcome? Outcome
        {
            get { return _state.Outcome; }
        }

        #endregion

        #region What the view calls

        /// <summary>
        /// Reads the app's registration mode and catalog, resolves whatever a signup link already
        /// chose, and opens the form. Runs once per flow.
        /// </summary>
        public async Task StartAsync()
        {
            if (_started || _detached) return;
            _started = true;

            // Latched on the first pass only: a login later in this very flow must not look like
            // "you were already signed in".
            await DispatchAsync(new SignupEvent.Init(_sessionManager.IsAuthenticated));
            await NotifyAlreadySignedInAsync();
            await LoadAsync(false);
        }

        public Task SetBillingAsync(PricingBilling billing)
        {
            _billing = billing;
            Notify();
            return Task.CompletedTask;
        }

        public Task TogglePackAsync(AppTierAddOnModel addOn)
        {
            var kept = new List<string>(_selectedPackIds.Count + 1);
            var removed = false;

            foreach (var id in _selectedPackIds)
            {
                if (string.Equals(id, addOn.Id, StringComparison.Ordinal)) { removed = true; continue; }
                kept.Add(id);
            }

            if (!removed) kept.Add(addOn.Id);
            _selectedPackIds = kept;
            Notify();
            return Task.CompletedTask;
        }

        public Task SubmitFormAsync(RegistrationFormData data)
        {
            _formData = data;
            _tokenMessage = null;
            return DispatchAsync(new SignupEvent.RegisterSubmitted(data.Email ?? string.Empty));
        }

        public Task ChoosePlanAsync(AppTierModel tier)
        {
            var pricing = PricingViewDecisions.PlanPriceOption(tier, _billing);
            _chosenPlan = new SignupPlanView(tier, pricing);

            return DispatchAsync(new SignupEvent.PlanChosen(
                tier.Id, pricing?.Id, SignupViewDecisions.RequiresPayment(tier, pricing)));
        }

        public Task ChoosePacksAsync()
        {
            return DispatchAsync(new SignupEvent.PacksChosen(_selectedPackIds));
        }

        public Task SkipPacksAsync()
        {
            _selectedPackIds = new List<string>();
            return DispatchAsync(new SignupEvent.PacksChosen(new List<string>()));
        }

        public Task ChangePlanAsync()
        {
            return DispatchAsync(new SignupEvent.GoTo(SignupStep.Plan));
        }

        public Task BackAsync(SignupStep step)
        {
            return DispatchAsync(new SignupEvent.GoTo(step));
        }

        /// <summary>
        /// The plan's card went through. Pay-first, so the account does not exist yet: the ids are
        /// kept for the account creation to link and subscribe with.
        /// </summary>
        public Task PaymentSucceededAsync(PaymentSuccessEventArgs args)
        {
            _paymentTransactionId = args.TransactionId ?? args.PaymentIntentId;
            _paymentExternalId = args.PaymentIntentId;

            return DispatchAsync(new SignupEvent.PaymentCompleted(_paymentTransactionId ?? string.Empty));
        }

        /// <summary>
        /// The visitor accepted the pending disclaimers. Persisting them is non-fatal: the account
        /// exists either way, and the server asks again next time.
        /// </summary>
        public async Task AcceptDisclaimersAsync(List<DisclaimerAcceptanceResult> acceptances)
        {
            try
            {
                if (acceptances is not null && acceptances.Count > 0)
                {
                    // accept-bulk is [Authorize]; the signup response carries the JWT for the new user.
                    _disclaimerService.SetAuthToken(_attempt.AuthResponse?.JwtToken);

                    var result = await _disclaimerService.AcceptDisclaimersAsync(Settings.AppId, acceptances);
                    if (!result.Success)
                    {
                        _logger?.LogWarning(
                            "Failed to record disclaimer acceptances: {Message}", result.ErrorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error recording disclaimer acceptances after signup");
            }

            await DispatchAsync(new SignupEvent.DisclaimersAccepted());
        }

        /// <summary>Every requested pack's outcome, from the pack checkout.</summary>
        public Task PacksBoughtAsync(IReadOnlyList<SignupPackOutcome> packs)
        {
            return DispatchAsync(new SignupEvent.PackCheckoutFinished(_state.Token ?? string.Empty, packs));
        }

        /// <summary>Resumes a failed signup at the step that failed.</summary>
        public async Task RetryAsync()
        {
            var fromLoading = _state.RetryFrom == SignupStep.Loading;
            await DispatchAsync(new SignupEvent.Retry());

            // A catalog that could not be read is asked for again, past the cache's TTL.
            if (fromLoading) await LoadAsync(true);
        }

        /// <summary>Throws the attempt away and returns to the form.</summary>
        public async Task StartOverAsync()
        {
            _attempt.Reset();
            _runs.Clear();
            _tokenGrant = null;
            _tokenMessage = null;
            _chosenPlan = null;
            _processingStatus = string.Empty;
            _paymentTransactionId = null;
            _paymentExternalId = null;
            _pendingDisclaimers = null;
            _entitlementsNotified = false;
            _completeNotified = false;

            await DispatchAsync(new SignupEvent.Reset());

            // RESET puts the machine back at Loading with neither the mode nor the catalog marked
            // ready, so the flow is re-opened from what has already been read rather than making
            // the visitor wait for the same two requests again.
            await ResumeLoadAsync();
        }

        /// <summary>The visitor left the finished signup. Answered exactly once.</summary>
        public async Task CompleteAsync()
        {
            if (_completeNotified) return;

            var outcome = _state.Outcome;
            if (outcome is null) return;

            _completeNotified = true;
            if (SignupCompleted is not null) await SignupCompleted(outcome);
        }

        /// <summary>Reports a failure the view itself noticed (the pack checkout's, typically).</summary>
        public Task ReportAsync(string code, string message)
        {
            return ErrorReported is not null ? ErrorReported(code, message) : Task.CompletedTask;
        }

        #endregion

        #region Loading

        private async Task LoadAsync(bool forceRefresh)
        {
            // The registration mode is NEVER cached: what the app will accept has to be read now,
            // because offering a path the server refuses is worse than a request.
            _mode = await ReadModeAsync();
            await DispatchAsync(new SignupEvent.ModeLoaded(_mode));

            if (Settings.PreloadedCatalog is not null)
            {
                _catalog = Settings.PreloadedCatalog;
            }
            else
            {
                try
                {
                    _catalog = await _catalogService.GetAsync(Settings.AppId, Settings.Currency, forceRefresh);
                }
                catch (Exception ex)
                {
                    _catalog = null;
                    var message = ex.Message is { Length: > 0 } ? ex.Message : "Failed to load the catalog";
                    await DispatchAsync(new SignupEvent.LoadFailed(message));
                    return;
                }
            }

            await ResolveSelectionAsync();
        }

        /// <summary>Re-opens the form from the mode and catalog already read, after a start-over.</summary>
        private async Task ResumeLoadAsync()
        {
            if (_catalog is null)
            {
                await LoadAsync(false);
                return;
            }

            await DispatchAsync(new SignupEvent.ModeLoaded(_mode));
            await ResolveSelectionAsync();
        }

        /// <summary>
        /// What a signup link already chose, checked against the live catalog: an unknown plan is
        /// ignored, and the packs are vetted and capped.
        /// </summary>
        private async Task ResolveSelectionAsync()
        {
            var invite = Settings.TokenMode == SignupTokenMode.Required;

            var presetPlan = SignupViewDecisions.ResolvePresetPlan(
                _catalog, invite, Settings.PlanSelection, Settings.PreSelectedTierId, Settings.PreSelectedPricingId);

            var presetPacks = SignupViewDecisions.ResolvePresetPackIds(
                _catalog, invite, Settings.PreSelectedAddOnIds);

            if (presetPacks.Count > 0) _selectedPackIds = presetPacks;

            await DispatchAsync(new SignupEvent.SelectionResolved(
                presetPlan?.Tier.Id,
                presetPlan?.Pricing?.Id,
                presetPacks,
                presetPlan is not null && SignupViewDecisions.RequiresPayment(presetPlan.Tier, presetPlan.Pricing)));

            await DispatchAsync(new SignupEvent.CatalogLoaded(SignupViewDecisions.CatalogNames(_catalog)));
        }

        private async Task<SignupRegistrationMode.Result> ReadModeAsync()
        {
            try
            {
                var config = await _authService.GetAuthenticationConfigurationAsync(Settings.AppId);
                var settings = config is null
                    ? null
                    : new SignupRegistrationSettings
                    {
                        AllowOpenRegistration = config.AllowOpenRegistration,
                        AllowTokenRegistration = config.AllowTokenRegistration
                    };

                return SignupRegistrationMode.Resolve(settings, Settings.TokenMode);
            }
            catch (Exception ex)
            {
                // Unreadable settings fall back to open sign-up with the optional token card. The
                // server still enforces its own, so the fallback can only offer a path that is
                // refused with a clear message, never grant one the server would not.
                _logger?.LogWarning(ex, "Could not read the app's authentication configuration");
                return SignupRegistrationMode.Resolve(null, Settings.TokenMode);
            }
        }

        #endregion

        #region The pump

        /// <summary>
        /// Applies an event and then runs whatever the step it landed on asks for, until the flow
        /// is waiting on a person or on a server.
        /// </summary>
        private async Task DispatchAsync(SignupEvent signupEvent)
        {
            // Detached: every dispatch comes from the view, and there is no view. A step's own
            // result does not come through here — it is applied straight onto the state.
            if (_detached) return;

            Apply(signupEvent);

            // Already inside the pump (this dispatch came from a step's own work): the loop below
            // is about to re-read the state, so it re-evaluates rather than nesting.
            if (_pumping) return;

            await PumpAsync();
        }

        private bool Apply(SignupEvent signupEvent)
        {
            var next = SignupMachine.Transition(_state, signupEvent);
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
                    // A detach that landed while the last step was awaiting ends the pump here.
                    // Everything past this point needs a person — a form, a card, a pack choice,
                    // an accepted disclaimer, or (at Creating) a registration nobody asked for any
                    // more — so the state the in-flight call just wrote is kept and nothing new is
                    // started. The call itself was never cut short; see Stop().
                    if (_detached) break;

                    Notify();
                    if (!await RunStepWorkAsync()) break;
                }
            }
            catch (Exception ex) when (_detached)
            {
                // Detached, the task this pump belongs to may have nobody left to await it, and an
                // escaping throw would be an unobserved task exception rather than an error anyone
                // sees. Attached, the filter does not match and the throw propagates as before.
                _logger?.LogError(ex, "The signup flow could not be settled after teardown");
            }
            finally
            {
                _pumping = false;
                Notify();

                if (_detached) FinishDetached();
            }
        }

        /// <summary>
        /// The end of a detached run. An account that got created before the view left is entitled
        /// to whatever it signed up for, wherever the flow then stopped — so the scoped entitlement
        /// service is told directly, the <see cref="EntitlementsChanged"/> callback that normally
        /// accompanies it having been cleared by <see cref="Stop"/>. Runs once, from whichever of
        /// <c>Stop</c> and the pump gets there last.
        /// </summary>
        private void FinishDetached()
        {
            if (_detachFinished) return;
            _detachFinished = true;

            if (_attempt.AuthResponse is null || _entitlementsNotified) return;
            _entitlementsNotified = true;

            try
            {
                _entitlements.Invalidate(Settings.AppId, EntitlementsChangedReasons.Signup);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not invalidate entitlements after a detached signup");
            }
        }

        /// <summary>
        /// One step's work. Answers whether the machine moved, so the pump knows to look again.
        /// </summary>
        private async Task<bool> RunStepWorkAsync()
        {
            switch (_state.Step)
            {
                case SignupStep.Token:
                    return await RunTokenStepAsync();

                case SignupStep.Payment:
                    // A payment step with nothing to charge for — no plan left in the catalog, or a
                    // form that was never submitted — cannot be paid, and a card taken there would
                    // be stranded. Back to the form.
                    if (_state.FormSubmitted && Plan is not null) return false;
                    return Apply(new SignupEvent.GoTo(SignupStep.Register));

                case SignupStep.Creating:
                    // Belt and braces over the machine's own gate: an account is never created
                    // from no details.
                    if (_formData is null) return Apply(new SignupEvent.GoTo(SignupStep.Register));
                    if (!Claim("creating", _state.Token)) return false;
                    await RunSignupAsync(_state.Token!);
                    return true;

                case SignupStep.Done:
                    return await NotifyEntitlementsAsync();

                default:
                    return false;
            }
        }

        private async Task<bool> RunTokenStepAsync()
        {
            // A rejected token is a form error: back to the form, with the server's words above it.
            if (_state.TokenError is { Length: > 0 })
            {
                return Apply(new SignupEvent.GoTo(SignupStep.Register));
            }

            if (!_state.TokenChecking)
            {
                var carriesToken = _formData?.Token is { Length: > 0 };
                return Apply(carriesToken
                    ? (SignupEvent)new SignupEvent.TokenCheckStarted()
                    : new SignupEvent.TokenSkipped());
            }

            if (!Claim("token", _state.Token)) return false;

            await CheckTokenAsync(_state.Token!);
            return true;
        }

        /// <summary>
        /// Reads what the token grants. A NULL answer is "the details could not be read", not
        /// "invalid": the token still grants app access, so the signup carries on as an ordinary
        /// one.
        /// </summary>
        private async Task CheckTokenAsync(string stepToken)
        {
            var value = _formData?.Token ?? string.Empty;

            RegistrationTokenDetails? details = null;
            try
            {
                details = await _authService.GetRegistrationTokenDetailsAsync(value, Settings.AppId);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not read registration token details; continuing with the normal flow");
            }

            if (details is not null && !details.IsValid)
            {
                var message = details.ErrorMessage is { Length: > 0 }
                    ? details.ErrorMessage
                    : SignupViewDecisions.TokenRejectedFallback;

                await ReportAsync(SignupViewDecisions.TokenRejectedCode, message);
                _tokenMessage = message;
                Apply(new SignupEvent.TokenRejected(stepToken, message));
                return;
            }

            var grant = Registration.SignupPlanDecisions.FindGrantForApp(details, Settings.AppId);
            _tokenGrant = grant;

            // A granted pack is neither charged for nor shown as chosen.
            if (grant is not null)
            {
                _selectedPackIds = SignupViewDecisions.WithoutGranted(_selectedPackIds, grant.AddOnIds);
            }

            Apply(new SignupEvent.TokenAccepted(stepToken, value, SignupViewDecisions.ToMachineGrant(grant)));
        }

        /// <summary>
        /// Register, sign in, link the plan's payment and start the subscription — all of it the
        /// shared <see cref="ISignupAccountCreator"/>, with the attempt carried across retries.
        /// </summary>
        private async Task RunSignupAsync(string stepToken)
        {
            var data = _formData;
            if (data is null)
            {
                Apply(new SignupEvent.GoTo(SignupStep.Register));
                return;
            }

            try
            {
                var result = await _accountCreator.CreateAsync(
                    new SignupAccountRequest
                    {
                        AppId = Settings.AppId,
                        FormData = data,
                        TierId = _state.Selection.TierId,
                        PricingId = _state.Selection.PricingId,
                        PaymentTransactionId = _paymentTransactionId,
                        PaymentExternalId = _paymentExternalId,
                        TokenGrant = _tokenGrant,
                        Labels = Settings.Labels,
                        OnStatus = status =>
                        {
                            // The status is the only thing that changes while the account is being
                            // created — the machine stays on `creating` throughout — so the render
                            // has to be marked dirty by hand. Without it Notify() reads a clean
                            // flag and returns, and the panel says "Creating your account..." for
                            // the whole of the sign-in and the subscription too.
                            _processingStatus = status;
                            _dirty = true;
                            Notify();
                        }
                    },
                    _attempt);

                if (!result.Success)
                {
                    var message = result.ErrorMessage is { Length: > 0 }
                        ? result.ErrorMessage
                        : SignupViewDecisions.SignupFailedFallback;

                    await ReportAsync(result.ErrorCode ?? SignupViewDecisions.SignupFailedCode, message);
                    Apply(new SignupEvent.AccountFailed(stepToken, message));
                    return;
                }

                _pendingDisclaimers = result.PendingDisclaimers;
                _processingStatus = string.Empty;

                Apply(new SignupEvent.AccountCreated(stepToken, result.UserId, result.RequiresDisclaimers));
            }
            catch (Exception ex)
            {
                var message = ex.Message is { Length: > 0 } ? ex.Message : SignupViewDecisions.SignupFailedFallback;
                _logger?.LogError(ex, "Error during signup processing");

                await ReportAsync(SignupViewDecisions.SignupFailedCode, message);
                Apply(new SignupEvent.AccountFailed(stepToken, message));
            }
        }

        /// <summary>
        /// The gates in the rest of the app are holding the anonymous answer; drop it and say why.
        /// Once per finished signup.
        /// </summary>
        private async Task<bool> NotifyEntitlementsAsync()
        {
            if (_entitlementsNotified) return false;
            _entitlementsNotified = true;

            _entitlements.Invalidate(Settings.AppId, EntitlementsChangedReasons.Signup);
            if (EntitlementsChanged is not null) await EntitlementsChanged();
            return false;
        }

        private async Task NotifyAlreadySignedInAsync()
        {
            if (!AlreadySignedIn || _signedInNotified) return;

            _signedInNotified = true;
            if (AlreadySignedInDetected is not null) await AlreadySignedInDetected();
        }

        /// <summary>One run per step token: a doubled effect carries a token already claimed.</summary>
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
