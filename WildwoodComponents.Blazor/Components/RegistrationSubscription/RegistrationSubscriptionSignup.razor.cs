using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;
using static WildwoodComponents.Blazor.Components.Registration.TokenRegistrationComponent;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription
{
    /// <summary>
    /// The signup surface of the registration + subscription component: an account, a plan, packs
    /// and a card, in the order that keeps them consistent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from
    /// packages/wildwood-react/src/components/registrationSubscription/views/SignupView.tsx, and
    /// named after it so the same surface is called the same thing on every stack.
    /// </para>
    /// <para>
    /// The order is PAY-FIRST, as every web stack ships it: the plan's card is taken before the
    /// account exists, because a declined card then leaves nothing behind rather than an account
    /// sitting on a plan nobody paid for. Packs are bought AFTER the login, because they are
    /// bought as the user — which is also what makes the card taken minutes ago the saved card the
    /// pack checkout finds, so nothing is asked for twice.
    /// </para>
    /// <para>
    /// The view never navigates: <see cref="ReturnUrl"/> is carried for the host's benefit and
    /// nothing here follows it, and the finished signup is handed to
    /// <see cref="OnSignupComplete"/> exactly once.
    /// </para>
    /// <para>
    /// Failures are reported through the INHERITED <see cref="BaseWildwoodComponent.OnError"/>
    /// callback, with the JS error code as the <c>ComponentErrorEventArgs.Context</c> — the .NET
    /// spelling of <c>{ code, message }</c>. It is never redeclared here: two <c>[Parameter]</c>
    /// properties of one name make a component impossible to render at all.
    /// </para>
    /// </remarks>
    public partial class RegistrationSubscriptionSignup : BaseWildwoodComponent
    {
        [Inject] private IPublicCatalogService CatalogService { get; set; } = default!;

        [Inject] private IAuthenticationService AuthService { get; set; } = default!;

        [Inject] private IFeatureEntitlementService EntitlementService { get; set; } = default!;

        [Inject] private IDisclaimerService DisclaimerService { get; set; } = default!;

        [Inject] private ISignupAccountCreator AccountCreator { get; set; } = default!;

        [Inject] private IWildwoodSessionManager SessionManager { get; set; } = default!;

        #region Parameters — common to every view

        /// <summary>The app the account is being created for.</summary>
        [Parameter, EditorRequired] public string AppId { get; set; } = string.Empty;

        /// <summary>
        /// Overrides the currency the catalog is quoted in. Rarely needed: the server names the
        /// currency, and this only wins for display.
        /// </summary>
        [Parameter] public string? Currency { get; set; }

        /// <summary>
        /// Copy the host may replace. An instance the host only partially set keeps the shipped
        /// word for every string it left alone.
        /// </summary>
        [Parameter] public RegistrationSubscriptionLabels? Labels { get; set; }

        /// <summary>Where "contact us" and enterprise plans point.</summary>
        [Parameter] public string? ContactUrl { get; set; }

        #endregion

        #region Parameters — the signup view

        /// <summary>The plan a pricing page already chose. Ignored when the app does not sell it.</summary>
        [Parameter] public string? PreSelectedTierId { get; set; }

        /// <summary>The pricing option within that plan (the annual one, typically).</summary>
        [Parameter] public string? PreSelectedPricingId { get; set; }

        /// <summary>
        /// Packs a pricing page already chose. Checked against the catalog and capped at
        /// <see cref="CatalogHelpers.MaxAddOnSelection"/>.
        /// </summary>
        [Parameter] public IReadOnlyList<string>? PreSelectedAddOnIds { get; set; }

        /// <summary>An invitation token from the signup link.</summary>
        [Parameter] public string? RegistrationToken { get; set; }

        /// <summary>Pre-fills the username and email fields, e.g. from an invitation.</summary>
        [Parameter] public string? PrefillEmail { get; set; }

        /// <summary>
        /// <see cref="SignupPlanSelection.Skip"/> leaves the plan to the app: a single-plan
        /// product, or one chosen elsewhere.
        /// </summary>
        [Parameter] public SignupPlanSelection PlanSelection { get; set; } = SignupPlanSelection.Choose;

        /// <summary>
        /// Whether the visitor may pick packs on the way in. Default
        /// <see cref="PricingPackSelection.None"/>, which only removes the STEP: packs a signup
        /// link chose are still bought.
        /// </summary>
        [Parameter] public PricingPackSelection PackSelection { get; set; } = PricingPackSelection.None;

        /// <summary>
        /// <see cref="SignupTokenMode.Required"/> is invite redemption: a token is the only way
        /// in, it decides the plan and the packs, and it overrides a closed configuration.
        /// </summary>
        [Parameter] public SignupTokenMode TokenMode { get; set; } = SignupTokenMode.Auto;

        /// <summary>Collect a billing address with the card.</summary>
        [Parameter] public bool RequireBillingAddress { get; set; }

        /// <summary>
        /// Where the host means to send the visitor afterwards. CARRIED, never navigated to: the
        /// view hands the outcome back and the host decides where it leads.
        /// </summary>
        [Parameter] public string? ReturnUrl { get; set; }

        /// <summary>
        /// Headings to file packs under on the pack step, matched on the add-on's catalog category.
        /// </summary>
        [Parameter] public IReadOnlyList<AddOnGroup>? AddOnGroups { get; set; }

        /// <summary>
        /// Host marketing copy for a pack, replacing the catalog's own description. The Blazor
        /// analog of React's <c>describeAddOn</c>: a template rather than a callback, because
        /// markup is what it returns.
        /// </summary>
        [Parameter] public RenderFragment<AppTierAddOnModel>? DescribeAddOn { get; set; }

        /// <summary>Replaces the built-in "registration is closed" notice.</summary>
        [Parameter] public RenderFragment<RegistrationClosedContext>? RenderClosed { get; set; }

        /// <summary>
        /// A catalog the host already has — read during a server prerender, or shared with another
        /// surface. The Blazor analog of React's <c>initialCatalog</c>.
        /// </summary>
        [Parameter] public PublicCatalog? PreloadedCatalog { get; set; }

        /// <summary>
        /// Raised once when the visitor turned out to have a session already. Latched at the first
        /// initialisation, so the sign-in this very flow performs cannot trigger it.
        /// </summary>
        [Parameter] public EventCallback OnAlreadySignedIn { get; set; }

        /// <summary>Raised exactly once, when the visitor leaves the finished signup.</summary>
        [Parameter] public EventCallback<SignupOutcome> OnSignupComplete { get; set; }

        /// <summary>Raised when the visitor backs out of the flow.</summary>
        [Parameter] public EventCallback OnCancel { get; set; }

        /// <summary>
        /// Raised after the new account's entitlements change, with the reason
        /// (<see cref="EntitlementsChangedReasons.Signup"/>), so the host can refresh its own gates.
        /// </summary>
        [Parameter] public EventCallback<string> OnEntitlementsChanged { get; set; }

        #endregion

        #region State

        private SignupFlowDriver? _flow;

        internal SignupFlowDriver? Flow
        {
            get { return _flow; }
        }

        #endregion

        #region Derived state

        /// <summary>
        /// The copy the markup renders from: the host's instance, or the shipped words. Never
        /// <see cref="Labels"/> directly — that parameter is nullable by design.
        /// </summary>
        private RegistrationSubscriptionLabels ResolvedLabels
        {
            get { return RegistrationSubscriptionLabels.Resolve(Labels); }
        }

        private RegistrationClosedContext ClosedContext
        {
            get { return new RegistrationClosedContext(ResolvedLabels.RegistrationClosed, ContactUrl); }
        }

        /// <summary>
        /// What the register form's submit button says. NOT a label on any stack: React hard-codes
        /// both words, and a live site's suite clicks them by name.
        /// </summary>
        private string SubmitButtonText
        {
            get { return SignupViewDecisions.SubmitButtonText(_flow is not null && _flow.PlanStepAhead); }
        }

        private IReadOnlyList<AppTierModel> PlanTiers
        {
            get
            {
                var catalog = _flow?.Catalog;
                return catalog is null ? new List<AppTierModel>() : catalog.Tiers;
            }
        }

        /// <summary>The plan the flow is carrying, else the one the link asked for.</summary>
        private string? HighlightedTierId
        {
            get
            {
                var chosen = _flow?.State.Selection.TierId;
                return chosen is { Length: > 0 } ? chosen : PreSelectedTierId;
            }
        }

        private decimal PlanAmount
        {
            get { return _flow?.Plan?.Pricing?.Price ?? 0m; }
        }

        /// <summary>
        /// The currency the card is charged in: the plan's own, then the catalog's, then the
        /// platform's, exactly as every other amount in the library resolves it.
        /// </summary>
        private string PaymentCurrency
        {
            get
            {
                var resolved = CatalogHelpers.ResolveCurrency(_flow?.Plan?.Tier, _flow?.Currency);
                return resolved is { Length: > 0 } ? resolved : "USD";
            }
        }

        private string PlanPriceText
        {
            get
            {
                var plan = _flow?.Plan;
                if (plan?.Pricing is null) return string.Empty;
                return CatalogHelpers.FormatPrice(plan.Tier, plan.Pricing.Price, _flow?.Currency);
            }
        }

        private string PlanFrequency
        {
            get
            {
                var frequency = _flow?.Plan?.Pricing?.BillingFrequency;
                return frequency is { Length: > 0 } ? frequency.ToLowerInvariant() : string.Empty;
            }
        }

        /// <summary>Null rather than zero, so PaymentComponent only starts a trial when there is one.</summary>
        private int? PlanTrialDays
        {
            get
            {
                var days = _flow?.TrialDays ?? 0;
                return days > 0 ? days : (int?)null;
            }
        }

        private string ProcessingHeading
        {
            get
            {
                var status = _flow?.ProcessingStatus;
                return status is { Length: > 0 } ? status : ResolvedLabels.StatusCreatingAccount;
            }
        }

        private string SuccessMessage
        {
            get
            {
                if (_flow is null) return ResolvedLabels.SignupCompletePlain;

                return SignupViewDecisions.SuccessMessage(
                    ResolvedLabels,
                    _flow.TokenGrant,
                    _flow.Plan is not null,
                    _flow.SubscriptionFailed,
                    _flow.TrialDays);
            }
        }

        private IReadOnlyList<SignupPackOutcome> OutcomePacks
        {
            get
            {
                var outcome = _flow?.Outcome;
                return outcome is null ? new List<SignupPackOutcome>() : outcome.Packs;
            }
        }

        /// <summary>
        /// "Change plan" is offered only when the plan is the visitor's to change: a skip flow's
        /// plan belongs to the app.
        /// </summary>
        private EventCallback ChangePlanCallback
        {
            get
            {
                return PlanSelection == SignupPlanSelection.Skip
                    ? default
                    : EventCallback.Factory.Create(this, ChangePlanAsync);
            }
        }

        #endregion

        #region Lifecycle

        protected override async Task OnComponentInitializedAsync()
        {
            _flow = new SignupFlowDriver(
                CatalogService,
                AuthService,
                EntitlementService,
                DisclaimerService,
                AccountCreator,
                SessionManager,
                new SignupFlowSettings
                {
                    AppId = AppId,
                    Currency = Currency,
                    PreSelectedTierId = PreSelectedTierId,
                    PreSelectedPricingId = PreSelectedPricingId,
                    PreSelectedAddOnIds = PreSelectedAddOnIds,
                    RegistrationToken = RegistrationToken,
                    PrefillEmail = PrefillEmail,
                    PlanSelection = PlanSelection,
                    PackSelection = PackSelection == PricingPackSelection.Multi
                        ? SignupPackSelection.Choose
                        : SignupPackSelection.None,
                    TokenMode = TokenMode,
                    Labels = ResolvedLabels,
                    PreloadedCatalog = PreloadedCatalog
                },
                Logger)
            {
                StateChanged = StateHasChanged,
                ErrorReported = ReportAsync,
                AlreadySignedInDetected = RaiseAlreadySignedInAsync,
                EntitlementsChanged = RaiseEntitlementsChangedAsync,
                SignupCompleted = RaiseSignupCompleteAsync
            };

            await _flow.StartAsync();
        }

        /// <summary>
        /// Ends the flow with the view, so a server answer still in flight cannot re-render or
        /// report into a component that has left the page.
        /// </summary>
        /// <remarks>
        /// An OVERRIDE of the base's synchronous disposal rather than an
        /// <see cref="IAsyncDisposable"/> of its own: Blazor calls <c>DisposeAsync</c> instead of
        /// <c>Dispose</c> on a component that implements it, and the base's <c>ThemeChanged</c>
        /// unsubscription lives in <c>Dispose</c>. There is nothing asynchronous to do here — the
        /// only browser resource in the signup is the pack checkout's Stripe instance, and
        /// <c>PackCheckout</c> disposes that itself.
        /// </remarks>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var flow = _flow;
                _flow = null;
                flow?.Stop();
            }

            base.Dispose(disposing);
        }

        #endregion

        #region Event handlers

        private Task HandleFormDataCollectedAsync(RegistrationFormData data)
        {
            return _flow is null ? Task.CompletedTask : _flow.SubmitFormAsync(data);
        }

        private Task HandleBillingChangeAsync(PricingBilling billing)
        {
            return _flow is null ? Task.CompletedTask : _flow.SetBillingAsync(billing);
        }

        private Task HandleSelectTierAsync(AppTierModel tier)
        {
            return _flow is null ? Task.CompletedTask : _flow.ChoosePlanAsync(tier);
        }

        private Task HandleTogglePackAsync(AppTierAddOnModel addOn)
        {
            return _flow is null ? Task.CompletedTask : _flow.TogglePackAsync(addOn);
        }

        /// <summary>
        /// The pack grid's single-select call to action. The signup's grid is multi-select, so
        /// this only fires if a host re-configures it; either way it means "go on with what is
        /// ticked", which is what React passes here too.
        /// </summary>
        private Task HandleChoosePackAsync(AppTierAddOnModel addOn)
        {
            return HandleChoosePacksAsync();
        }

        private Task HandleChoosePacksAsync()
        {
            return _flow is null ? Task.CompletedTask : _flow.ChoosePacksAsync();
        }

        private Task HandleSkipPacksAsync()
        {
            return _flow is null ? Task.CompletedTask : _flow.SkipPacksAsync();
        }

        private Task ChangePlanAsync()
        {
            return _flow is null ? Task.CompletedTask : _flow.ChangePlanAsync();
        }

        private Task BackToRegisterAsync()
        {
            return _flow is null ? Task.CompletedTask : _flow.BackAsync(SignupStep.Register);
        }

        private Task HandlePaymentSuccessAsync(PaymentSuccessEventArgs args)
        {
            return _flow is null ? Task.CompletedTask : _flow.PaymentSucceededAsync(args);
        }

        /// <summary>
        /// Backing out of the card is a step back, not an abandoned signup: nothing has been
        /// created yet. A skip flow has no plan step to go back to, so it goes to the form.
        /// </summary>
        private Task HandlePaymentCancelAsync()
        {
            if (_flow is null) return Task.CompletedTask;

            return _flow.BackAsync(PlanSelection == SignupPlanSelection.Skip
                ? SignupStep.Register
                : SignupStep.Plan);
        }

        private Task HandleDisclaimersAcceptedAsync(List<DisclaimerAcceptanceResult> acceptances)
        {
            return _flow is null ? Task.CompletedTask : _flow.AcceptDisclaimersAsync(acceptances);
        }

        private Task HandlePacksBoughtAsync(IReadOnlyList<SignupPackOutcome> packs)
        {
            return _flow is null ? Task.CompletedTask : _flow.PacksBoughtAsync(packs);
        }

        private Task HandleRetryAsync()
        {
            return _flow is null ? Task.CompletedTask : _flow.RetryAsync();
        }

        private Task HandleStartOverAsync()
        {
            return _flow is null ? Task.CompletedTask : _flow.StartOverAsync();
        }

        private Task HandleCompleteAsync()
        {
            return _flow is null ? Task.CompletedTask : _flow.CompleteAsync();
        }

        private Task HandleCancelAsync()
        {
            return OnCancel.HasDelegate ? OnCancel.InvokeAsync() : Task.CompletedTask;
        }

        /// <summary>A child part's failure is this component's failure: it is re-raised as one.</summary>
        private Task HandleChildErrorAsync(ComponentErrorEventArgs args)
        {
            return InvokeOnErrorAsync(args.Exception, args.Context);
        }

        #endregion

        #region Raising the host's callbacks

        private Task ReportAsync(string code, string message)
        {
            return InvokeOnErrorAsync(new InvalidOperationException(message), code);
        }

        private Task RaiseAlreadySignedInAsync()
        {
            return OnAlreadySignedIn.HasDelegate ? OnAlreadySignedIn.InvokeAsync() : Task.CompletedTask;
        }

        private Task RaiseEntitlementsChangedAsync()
        {
            return OnEntitlementsChanged.HasDelegate
                ? OnEntitlementsChanged.InvokeAsync(EntitlementsChangedReasons.Signup)
                : Task.CompletedTask;
        }

        private Task RaiseSignupCompleteAsync(SignupOutcome outcome)
        {
            return OnSignupComplete.HasDelegate ? OnSignupComplete.InvokeAsync(outcome) : Task.CompletedTask;
        }

        #endregion
    }
}
