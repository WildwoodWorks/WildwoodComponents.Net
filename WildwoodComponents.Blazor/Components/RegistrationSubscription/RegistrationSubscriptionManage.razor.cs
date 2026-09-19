using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Components.Subscription.Admin;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.RegistrationSubscription
{
    /// <summary>
    /// The manage surface of the registration + subscription component: what a customer already
    /// pays for, and every way of changing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from
    /// packages/wildwood-react/src/components/registrationSubscription/views/ManageView.tsx, and
    /// named after it so the same surface is called the same thing on every stack.
    /// </para>
    /// <para>
    /// The panels are the platform's own — the same status card, plan grid, features, packs, usage
    /// and overrides the admin surface has always rendered — so a site swapping its hand-built
    /// plan page for this keeps its markup and its locators. What is new is the middle: the plan
    /// change runs through <see cref="PlanChangeDriver"/>, so a preview is confirmed in EVERY
    /// layout, a card is asked for by the component itself when the host did not bring its own
    /// modal, and a prorated charge the bank wants to see is authenticated and the parked change
    /// completed instead of being refused.
    /// </para>
    /// <para>
    /// Packs are the other half. A pack is bought through the same card-once checkout the signup
    /// uses, cancelled at the end of the period it is paid up to, and a scheduled cancellation can
    /// be taken back. A pack nobody paid for says so, shows no renewal date and is simply removed
    /// when it is cancelled.
    /// </para>
    /// <para>
    /// The view never navigates: <see cref="ReturnUrl"/> is carried for a redirect-flow provider
    /// and nothing here follows it.
    /// </para>
    /// <para>
    /// Failures are reported through the INHERITED <see cref="BaseWildwoodComponent.OnError"/>
    /// callback, with the JS error code as the <c>ComponentErrorEventArgs.Context</c>. It is never
    /// redeclared here: two <c>[Parameter]</c> properties of one name make a component impossible
    /// to render at all.
    /// </para>
    /// </remarks>
    public partial class RegistrationSubscriptionManage : BaseWildwoodComponent, IAsyncDisposable
    {
        [Inject] private IAppTierComponentService AppTierService { get; set; } = default!;

        [Inject] private IPaymentProviderService PaymentProviderService { get; set; } = default!;

        [Inject] private IFeatureEntitlementService EntitlementService { get; set; } = default!;

        #region Parameters — common to every view

        /// <summary>The app whose subscription is being managed.</summary>
        [Parameter, EditorRequired] public string AppId { get; set; } = string.Empty;

        /// <summary>
        /// The currency a pack or plan that carries none of its own is quoted in. Each catalog row
        /// still wins with its own.
        /// </summary>
        [Parameter] public string? Currency { get; set; }

        /// <summary>
        /// Copy the host may replace. An instance the host only partially set keeps the shipped
        /// word for every string it left alone.
        /// </summary>
        [Parameter] public RegistrationSubscriptionLabels? Labels { get; set; }

        /// <summary>Where an enterprise plan's "contact us" points.</summary>
        [Parameter] public string? ContactUrl { get; set; }

        #endregion

        #region Parameters — the manage view

        /// <summary>Tabs (the default) or every section down the page.</summary>
        [Parameter] public ManageLayout Layout { get; set; } = ManageLayout.Tabs;

        /// <summary>
        /// Which panels to render, in what order. Null means all of them; overrides are dropped
        /// unless <see cref="IsAdmin"/>, packs unless <see cref="ShowAddOns"/>.
        /// </summary>
        [Parameter] public IReadOnlyList<ManageSection>? Sections { get; set; }

        /// <summary>Lift the subscription card out of the section list and above the tab bar.</summary>
        [Parameter] public bool ShowStatusAboveTabs { get; set; }

        /// <summary>Whether the viewer may see overrides and edit usage limits.</summary>
        [Parameter] public bool IsAdmin { get; set; }

        /// <summary>An admin acting on one user's subscription. Such a change never collects a card.</summary>
        [Parameter] public string? UserId { get; set; }

        /// <summary>An admin acting on a company's subscription. Never collects a card either.</summary>
        [Parameter] public string? CompanyId { get; set; }

        /// <summary>Whether the customer may buy packs for themselves, through the pack checkout.</summary>
        [Parameter] public bool AllowPackSelfService { get; set; }

        /// <summary>Whether this surface offers cancelling the subscription and its packs at all.</summary>
        [Parameter] public bool AllowCancel { get; set; } = true;

        /// <summary>Whether the packs section is offered.</summary>
        [Parameter] public bool ShowAddOns { get; set; } = true;

        /// <summary>
        /// The host's own real-time usage, overlaid on the server's statuses. Given one, the view
        /// reads the statuses itself and hands the merged list to the usage panel — the same seam
        /// <c>UsageDashboardComponent</c> offers.
        /// </summary>
        [Parameter]
        public Func<List<AppTierLimitStatusModel>, UserTierSubscriptionModel?, Task<List<AppTierLimitStatusModel>>>?
            OnMergeUsage { get; set; }

        /// <summary>
        /// The host's own card modal. Given one, it is used INSTEAD of the built-in modal and its
        /// answer is final: a transaction id completes the change, null or empty abandons it.
        /// </summary>
        [Parameter] public Func<PaymentRequiredArgs, Task<string?>>? OnPaymentRequired { get; set; }

        /// <summary>Raised after every mutation that changed what the subscription is.</summary>
        [Parameter] public EventCallback OnSubscriptionChanged { get; set; }

        /// <summary>
        /// Raised after every mutation that changes what the account is entitled to, carrying one
        /// of <see cref="EntitlementsChangedReasons"/>. Additive: the view already invalidates the
        /// shared entitlement cache, and a host that only needs that can keep ignoring this.
        /// </summary>
        [Parameter] public EventCallback<string> OnEntitlementsChanged { get; set; }

        /// <summary>
        /// Where a redirect-flow payment provider should come back to. CARRIED, never navigated
        /// to: the view hands control back and the host decides where it leads.
        /// </summary>
        [Parameter] public string? ReturnUrl { get; set; }

        #endregion

        #region State

        private PlanChangeDriver? _flow;
        private UserTierSubscriptionModel? _subscription;
        private ManageSection? _activeTab;
        private bool _pickingPacks;
        private bool _disposedAsync;

        /// <summary>The host's merged usage, or null when the host supplied no merge callback.</summary>
        private List<AppTierLimitStatusModel>? _mergedLimitStatuses;

        /// <summary>The last cancellation, while its notice is still on screen.</summary>
        private AppTierCancelResultModel? _cancelResult;

        private SubscriptionStatusPanel? _statusPanel;
        private TierPlansPanel? _plansPanel;
        private FeaturesPanel? _featuresPanel;
        private AddOnsPanel? _addOnsPanel;
        private UsageLimitsPanel? _usagePanel;
        private OverridesPanel? _overridesPanel;

        internal PlanChangeDriver? Flow
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

        /// <summary>The panels' fallback currency. Each catalog row's own still wins.</summary>
        private string ResolvedCurrency
        {
            get { return Currency is { Length: > 0 } ? Currency : "USD"; }
        }

        /// <summary>A user id wins over a company id, exactly as the JS view's scope chain does.</summary>
        private bool IsCompanyMode
        {
            get { return ManageViewDecisions.IsCompanyMode(UserId, CompanyId); }
        }

        private string? ScopedUserId
        {
            get { return IsCompanyMode ? null : UserId; }
        }

        private List<ManageSection> VisibleSections
        {
            get { return ManageViewDecisions.VisibleSections(Sections, IsAdmin, ShowAddOns); }
        }

        private bool StatusAbove
        {
            get { return ManageViewDecisions.StatusAbove(VisibleSections, ShowStatusAboveTabs); }
        }

        private List<ManageSection> BodySections
        {
            get { return ManageViewDecisions.BodySections(VisibleSections, StatusAbove); }
        }

        private ManageSection? CurrentTab
        {
            get { return ManageViewDecisions.CurrentTab(_activeTab, BodySections); }
        }

        /// <summary>
        /// The data layer's own error, unless the plan change already said what went wrong — the
        /// notice carries a failed change's message, so it is not shown a second time.
        /// </summary>
        private bool ShowErrorAlert
        {
            get
            {
                if (!(ErrorMessage is { Length: > 0 })) return false;
                return _flow is null || _flow.Step != PlanChangeStep.Failed;
            }
        }

        /// <summary>
        /// The packs the picker may sell: what the panel already counts as on offer — everything
        /// the account does not hold, a cancelled or expired row being on offer again — minus
        /// anything the current plan bundles, which is included rather than sold. A pack a
        /// registration token granted is held, so it never reaches here.
        /// </summary>
        private IReadOnlyList<AppTierAddOnModel> AvailablePacks
        {
            get
            {
                var offered = new List<AppTierAddOnModel>();
                if (_addOnsPanel is null) return offered;

                foreach (var addOn in _addOnsPanel.AvailableAddOns)
                {
                    if (addOn is null || AddOnRowRules.IsBundledInTier(addOn, CurrentTierId)) continue;
                    offered.Add(addOn);
                }

                return offered;
            }
        }

        /// <summary>The plan change's step, for <c>data-ww-step</c>. Idle before the flow exists.</summary>
        private string StepName
        {
            get { return _flow is null ? "idle" : _flow.StepName; }
        }

        private string? CurrentTierId
        {
            get { return _subscription?.AppTierId; }
        }

        /// <summary>The pack rows' copy, taken from the shared labels so every stack says the same.</summary>
        private AddOnsPanelLabels PackLabels
        {
            get
            {
                var labels = ResolvedLabels;
                return new AddOnsPanelLabels
                {
                    Included = labels.PackIncluded,
                    CancelIncluded = labels.PackCancelIncluded,
                    CancelBilled = labels.PackCancelBilled,
                    CancelConfirm = labels.PackCancelConfirm,
                    CancelKeep = labels.PackCancelKeep,
                    Reactivate = labels.PackReactivate,
                    AddPacks = labels.AddPacks
                };
            }
        }

        /// <summary>
        /// Cancelling is offered only when the host allows it: an unset callback is what the
        /// status panel reads as "this surface does not cancel".
        /// </summary>
        private EventCallback CancelSubscriptionCallback
        {
            get
            {
                return AllowCancel
                    ? EventCallback.Factory.Create(this, HandleCancelSubscriptionAsync)
                    : default;
            }
        }

        /// <summary>"Add packs" appears only when the host turned self-service on.</summary>
        private EventCallback AddPacksCallback
        {
            get
            {
                return AllowPackSelfService
                    ? EventCallback.Factory.Create(this, OpenPackPicker)
                    : default;
            }
        }

        /// <summary>Only a successful cancellation is worth a notice.</summary>
        private bool ShowCancelNotice
        {
            get { return _cancelResult is not null && _cancelResult.Success; }
        }

        private string CancelNoticeText
        {
            get
            {
                return _cancelResult is null
                    ? string.Empty
                    : ManageViewDecisions.CancelNotice(_cancelResult.IsScheduled, _cancelResult.EffectiveDate);
            }
        }

        /// <summary>
        /// The part the platform cannot do for the customer: a store keeps charging until the
        /// subscription is cancelled in its own settings.
        /// </summary>
        private string? CancelActionInstructions
        {
            get
            {
                if (_cancelResult is null || !_cancelResult.RequiresUserAction) return null;

                return _cancelResult.UserActionInstructions is { Length: > 0 } instructions
                    ? instructions
                    : ManageViewDecisions.StoreCancelInstructions;
            }
        }

        private string? CancelActionUrl
        {
            get
            {
                return _cancelResult is not null && _cancelResult.RequiresUserAction
                    ? _cancelResult.UserActionUrl
                    : null;
            }
        }

        private string SectionTitle(ManageSection section)
        {
            return ManageViewDecisions.SectionTitle(section, ResolvedLabels);
        }

        private static string SectionName(ManageSection section)
        {
            return ManageViewDecisions.SectionName(section);
        }

        private string TabCssClass(ManageSection section)
        {
            return CurrentTab == section ? "ww-sub-admin-tab ww-sub-admin-tab-active" : "ww-sub-admin-tab";
        }

        #endregion

        #region Lifecycle

        protected override async Task OnComponentInitializedAsync()
        {
            _flow = new PlanChangeDriver(
                AppTierService,
                PaymentProviderService,
                new StripePaymentActions(JSRuntime, PaymentInstanceKey),
                new PlanChangeSettings
                {
                    AppId = AppId,
                    UserId = ScopedUserId,
                    CompanyId = IsCompanyMode ? CompanyId : null,
                    Labels = ResolvedLabels
                },
                EntitlementService,
                Logger)
            {
                StateChanged = StateHasChanged,
                PaymentRequested = OnPaymentRequired,
                Changed = RefreshAsync,
                EntitlementsChanged = ForwardEntitlementsChangedAsync,
                ErrorReported = ReportAsync
            };

            if (!(AppId is { Length: > 0 })) return;

            await LoadSubscriptionAsync();
            await MergeUsageAsync();
        }

        /// <summary>
        /// The host may wire its own card modal after the first render, and the driver reads that
        /// handler at the moment a card is needed — so the two are kept in step here.
        /// </summary>
        protected override void OnParametersSet()
        {
            base.OnParametersSet();

            if (_flow is not null) _flow.PaymentRequested = OnPaymentRequired;
        }

        /// <summary>
        /// The script instance this view's 3-D Secure challenges run on. Unique per component, so
        /// a plan change's authentication never touches a card form's instance or
        /// <c>PaymentComponent</c>'s default one — and so the key this component disposes is the
        /// key it created.
        /// </summary>
        internal string PaymentInstanceKey
        {
            get { return "ww-plan-change-" + ComponentId; }
        }

        /// <summary>
        /// Detaches the plan change from this component and drops the Stripe instance its 3-D
        /// Secure challenges created.
        /// </summary>
        /// <remarks>
        /// The driver's own disposal returns straight away when a bank challenge is still in
        /// flight — it drains that change to its completion in the background and drops the script
        /// instance afterwards — so leaving the page is never held up behind a customer finishing
        /// a challenge. See <see cref="PlanChangeDriver.DisposeAsync"/>.
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

            var flow = _flow;
            _flow = null;

            if (flow is not null) await flow.DisposeAsync();

            Dispose();
        }

        #endregion

        #region Loading and refreshing

        private async Task LoadSubscriptionAsync()
        {
            try
            {
                if (IsCompanyMode)
                {
                    _subscription = await AppTierService.GetCompanySubscriptionAsync(AppId, CompanyId!);
                }
                else if (ScopedUserId is { Length: > 0 })
                {
                    _subscription = await AppTierService.GetUserSubscriptionAsync(AppId, ScopedUserId);
                }
                else
                {
                    _subscription = await AppTierService.GetMySubscriptionAsync(AppId);
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Loading subscription data");
            }
        }

        /// <summary>
        /// Reloads every panel and the subscription behind them, then tells the host. The analog
        /// of JS's <c>refresh()</c> — there the data lives in one hook, here each panel owns its
        /// own reads.
        /// </summary>
        private async Task RefreshAsync()
        {
            await LoadSubscriptionAsync();

            _statusPanel?.UpdateSubscription(_subscription);
            _plansPanel?.UpdateSubscription(_subscription);

            if (_featuresPanel is not null) await _featuresPanel.LoadFeaturesAsync();
            if (_addOnsPanel is not null) await _addOnsPanel.LoadDataAsync();
            if (_overridesPanel is not null) await _overridesPanel.LoadDataAsync();

            await MergeUsageAsync();
            if (_usagePanel is not null) await _usagePanel.LoadDataAsync();

            if (OnSubscriptionChanged.HasDelegate) await OnSubscriptionChanged.InvokeAsync();
        }

        /// <summary>
        /// Overlays the host's real-time usage on the server's statuses. Only when the host
        /// supplied a callback: otherwise the usage panel reads them itself, as it always has.
        /// </summary>
        private async Task MergeUsageAsync()
        {
            var merge = OnMergeUsage;
            if (merge is null)
            {
                _mergedLimitStatuses = null;
                return;
            }

            try
            {
                List<AppTierLimitStatusModel> raw;
                if (IsCompanyMode)
                {
                    raw = await AppTierService.GetCompanyLimitStatusesAsync(AppId, CompanyId!);
                }
                else if (ScopedUserId is { Length: > 0 })
                {
                    raw = await AppTierService.GetUserLimitStatusesAsync(AppId, ScopedUserId);
                }
                else
                {
                    raw = await AppTierService.GetAllLimitStatusesAsync(AppId);
                }

                // A merge that answers with nothing leaves the server's own statuses standing: an
                // empty usage panel reads as "no limits", which is not what a failed merge means.
                var merged = await merge(raw, _subscription);
                _mergedLimitStatuses = merged is not null && merged.Count > 0 ? merged : raw;
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "The host's usage merge failed; the server's statuses stand");
                _mergedLimitStatuses = null;
            }
        }

        #endregion

        #region Tabs

        private void SetActiveTab(ManageSection section)
        {
            _activeTab = section;
            StateHasChanged();
        }

        #endregion

        #region The plan change

        private Task HandleTierSelectedAsync(TierSelectedEventArgs args)
        {
            ClearError();
            return _flow is null ? Task.CompletedTask : _flow.SelectTierAsync(args);
        }

        private Task HandleConfirmChangeAsync(TierChangeConfirmOptions options)
        {
            ClearError();
            return _flow is null ? Task.CompletedTask : _flow.ConfirmAsync(options);
        }

        private Task HandleCancelChangeAsync()
        {
            return _flow is null ? Task.CompletedTask : _flow.CancelAsync();
        }

        private Task HandlePaymentSettledAsync(string? paymentTransactionId)
        {
            return _flow is null ? Task.CompletedTask : _flow.ProvidePaymentAsync(paymentTransactionId);
        }

        private Task HandleRetryChangeAsync()
        {
            return _flow is null ? Task.CompletedTask : _flow.RetryAsync();
        }

        private Task HandleDismissChangeAsync()
        {
            return _flow is null ? Task.CompletedTask : _flow.ResetAsync();
        }

        #endregion

        #region Cancelling the subscription

        private async Task HandleCancelSubscriptionAsync()
        {
            try
            {
                AppTierCancelResultModel result;
                if (IsCompanyMode)
                {
                    result = await AppTierService.CancelCompanySubscriptionAsync(AppId, CompanyId!);
                }
                else if (ScopedUserId is { Length: > 0 })
                {
                    result = await AppTierService.CancelUserSubscriptionAsync(AppId, ScopedUserId);
                }
                else
                {
                    result = await AppTierService.CancelSubscriptionAsync(AppId);
                }

                _cancelResult = result;

                // A failed cancel must not look successful: the notice renders nothing for one.
                if (!result.Success)
                {
                    await ReportAsync(
                        ManageViewDecisions.CancelFailedCode,
                        result.ErrorMessage is { Length: > 0 }
                            ? result.ErrorMessage
                            : ManageViewDecisions.CancelFailedFallback);
                    return;
                }

                await RaiseEntitlementsChangedAsync(EntitlementsChangedReasons.Cancel);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                await ReportAsync(
                    ManageViewDecisions.CancelFailedCode,
                    ex.Message is { Length: > 0 } ? ex.Message : ManageViewDecisions.CancelFailedFallback);
            }
        }

        private void DismissCancelNotice()
        {
            _cancelResult = null;
            StateHasChanged();
        }

        #endregion

        #region Packs

        private void OpenPackPicker()
        {
            _pickingPacks = true;
            StateHasChanged();
        }

        private void ClosePackPicker()
        {
            _pickingPacks = false;
            StateHasChanged();
        }

        /// <summary>Pack names for the picker's outcomes, so a pack the quote never priced still reads.</summary>
        private Dictionary<string, string> PackNames()
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var addOn in AvailablePacks)
            {
                if (addOn is not null) names[addOn.Id] = addOn.Name;
            }

            return names;
        }

        private async Task HandlePacksBoughtAsync(IReadOnlyList<SignupPackOutcome> outcomes)
        {
            await RaiseEntitlementsChangedAsync(EntitlementsChangedReasons.AddOn);
            await RefreshAsync();
        }

        /// <summary>
        /// A pack row finished. The panel says WHICH action it was, because the three do not share
        /// one reason: buying is <c>addOn</c>, cancelling is <c>cancel</c> and taking a scheduled
        /// cancellation back is <c>reactivate</c>.
        /// </summary>
        private async Task HandlePackChangedAsync(string reason)
        {
            await RaiseEntitlementsChangedAsync(reason);
            await RefreshAsync();
        }

        #endregion

        #region Features, overrides and usage

        private async Task HandleOverrideToggledAsync()
        {
            if (_overridesPanel is not null) await _overridesPanel.LoadDataAsync();

            // An override is granted or revoked by hand, outside any plan — JS's "manual".
            await RaiseEntitlementsChangedAsync(EntitlementsChangedReasons.Manual);
            StateHasChanged();
        }

        private async Task HandleOverrideRemovedAsync()
        {
            if (_featuresPanel is not null) await _featuresPanel.LoadFeaturesAsync();

            await RaiseEntitlementsChangedAsync(EntitlementsChangedReasons.Manual);
            StateHasChanged();
        }

        private async Task HandleLimitChangedAsync()
        {
            await MergeUsageAsync();
            StateHasChanged();
        }

        #endregion

        #region Raising the host's callbacks

        /// <summary>
        /// Drops the shared entitlement cache with the reason attached, then tells the host. Both
        /// halves always happen together: a cache dropped without saying why leaves a host unable
        /// to tell a plan change from a pack purchase, and a callback without the eviction leaves
        /// every FeatureGate in the circuit serving the old plan for the cache TTL.
        /// </summary>
        private async Task RaiseEntitlementsChangedAsync(string reason)
        {
            EntitlementService.Invalidate(AppId, reason);

            if (OnEntitlementsChanged.HasDelegate) await OnEntitlementsChanged.InvokeAsync(reason);
        }

        /// <summary>
        /// The plan change's own entitlement announcement. The DRIVER dropped the cache for it —
        /// it has to, because a change drained after this component has gone has no callback left
        /// — so this only forwards the reason.
        /// </summary>
        private Task ForwardEntitlementsChangedAsync(string reason)
        {
            return OnEntitlementsChanged.HasDelegate
                ? OnEntitlementsChanged.InvokeAsync(reason)
                : Task.CompletedTask;
        }

        private Task ReportAsync(string code, string message)
        {
            return InvokeOnErrorAsync(new InvalidOperationException(message), code);
        }

        /// <summary>A child part's failure is this component's failure: it is re-raised as one.</summary>
        private Task HandleChildErrorAsync(ComponentErrorEventArgs args)
        {
            return InvokeOnErrorAsync(args.Exception, args.Context);
        }

        #endregion
    }
}
