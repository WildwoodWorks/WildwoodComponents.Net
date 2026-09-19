using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Components.RegistrationSubscription;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.Subscription.Admin
{
    /// <summary>
    /// The subscription admin surface: status, plans, features, packs, usage and overrides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Behaviour change (JS 27daa30, ported here):</b> the plan change now runs through the
    /// shared <see cref="PlanChangeDriver"/>, the same flow
    /// <c>RegistrationSubscriptionManage</c> uses, so the two cannot drift. A change that needs a
    /// card and has no <see cref="OnPaymentRequired"/> handler used to fail with "Payment is
    /// required for this tier change. Wire the OnPaymentRequired callback to collect payment.";
    /// it now opens the component's OWN payment modal instead. A host that passes
    /// <see cref="OnPaymentRequired"/> keeps exactly the old behaviour — its handler still wins,
    /// and a null or empty answer still abandons the change.
    /// </para>
    /// <para>
    /// Two other things came with the shared flow: a prorated charge the bank wants to see is now
    /// authenticated and the parked change completed (it used to be refused outright), and the
    /// confirmation modal renders in every display mode rather than only in the tabbed one.
    /// </para>
    /// <para>
    /// Disposal is asynchronous because the Stripe instance the 3-D Secure path creates is dropped
    /// through JS interop. See <see cref="DisposeAsync"/> for why the base's synchronous clean-up
    /// is called by hand from there.
    /// </para>
    /// </remarks>
    public partial class SubscriptionAdminComponent : BaseWildwoodComponent, IAsyncDisposable
    {
        [Inject] private IAppTierComponentService AppTierService { get; set; } = default!;
        [Inject] private IPaymentProviderService PaymentProviderService { get; set; } = default!;
        [Inject] private IFeatureEntitlementService EntitlementService { get; set; } = default!;

        #region Parameters

        [Parameter, EditorRequired] public string AppId { get; set; } = string.Empty;
        [Parameter] public string? CompanyId { get; set; }
        /// <summary>When set, admin is viewing a specific user's subscription (User tracking mode).</summary>
        [Parameter] public string? UserId { get; set; }
        [Parameter] public SubscriptionDisplayMode DisplayMode { get; set; } = SubscriptionDisplayMode.All;
        [Parameter] public bool IsAdmin { get; set; }
        [Parameter] public string Currency { get; set; } = "USD";
        [Parameter] public bool ShowBillingToggle { get; set; } = true;
        /// <summary>When true, render the subscription status card above the tab bar instead of as a tab.</summary>
        [Parameter] public bool ShowStatusAboveTabs { get; set; }
        [Parameter] public EventCallback<AppTierSubscriptionChangedEventArgs> OnSubscriptionChanged { get; set; }

        /// <summary>
        /// Raised after every mutation here that changes what the user is entitled to, carrying one
        /// of <see cref="EntitlementsChangedReasons"/>. Additive: the panel already invalidates the
        /// shared entitlement cache, and a host that only needs that can keep ignoring this.
        /// </summary>
        /// <remarks>
        /// React reaches the same place two ways — <c>useSubscriptionAdmin</c> emits
        /// <c>entitlementsChanged</c> on the client's emitter, and the manage view forwards it to an
        /// <c>onEntitlementsChanged</c> prop. .NET has both: <c>IFeatureEntitlementService</c>'s
        /// <c>EntitlementsChangedDetailed</c> is the emitter, and this is the prop.
        /// </remarks>
        [Parameter] public EventCallback<string> OnEntitlementsChanged { get; set; }

        /// <summary>
        /// Called when the server confirms a tier change requires payment and no card is on file.
        /// Return a payment transaction id to complete the change, or null/empty to cancel.
        /// Consumers typically wire this to a modal containing PaymentFormComponent.
        /// </summary>
        /// <remarks>
        /// A handler passed here still wins, exactly as it always has. What changed is the
        /// UNWIRED case: the component now opens its own payment modal rather than reporting
        /// "wire the OnPaymentRequired callback". See the remarks on the component itself.
        /// </remarks>
        [Parameter] public Func<PaymentRequiredArgs, Task<string?>>? OnPaymentRequired { get; set; }

        /// <summary>
        /// Copy for the plan change's own messages — the confirmation's failure notice and the
        /// built-in payment modal. Defaults to the shipped words, which are the same strings every
        /// stack ships.
        /// </summary>
        [Parameter] public RegistrationSubscriptionLabels? Labels { get; set; }

        /// <summary>Override the internally-fetched limit statuses (e.g. with locally-merged real-time usage data).</summary>
        [Parameter] public IReadOnlyList<AppTierLimitStatusModel>? LimitStatusesOverride { get; set; }

        #endregion

        #region State

        private UserTierSubscriptionModel? _subscription;
        private string _activeTab = "subscription";
        private bool _isProcessing;

        // Post-cancellation notice (scheduled-vs-immediate messaging + store-billing follow-up)
        private string? _cancelNotice;
        private string? _cancelActionInstructions;
        private string? _cancelActionUrl;
        private bool _isCompanyMode;
        private int _overrideCount;

        /// <summary>The shared plan-change flow: preview, confirm, card, 3-D Secure, completion.</summary>
        private PlanChangeDriver? _flow;

        /// <summary>The plan the flow is changing to, for the host's "changed"/"subscribed" notice.</summary>
        private TierSelectedEventArgs? _pendingArgs;

        private bool _disposedAsync;

        internal PlanChangeDriver? Flow
        {
            get { return _flow; }
        }

        /// <summary>
        /// The copy the plan-change parts render from: the host's instance, or the shipped words.
        /// </summary>
        private RegistrationSubscriptionLabels ResolvedLabels
        {
            get { return RegistrationSubscriptionLabels.Resolve(Labels); }
        }

        /// <summary>
        /// The script instance this component's 3-D Secure challenges run on. Unique per
        /// component, so a plan change's authentication never touches a card form's instance or
        /// <c>PaymentComponent</c>'s default one.
        /// </summary>
        internal string PaymentInstanceKey
        {
            get { return "ww-sub-admin-" + ComponentId; }
        }

        /// <summary>
        /// The data layer's own error, unless the plan change already said what went wrong — its
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

        // References to child panels for refreshing
        private SubscriptionStatusPanel? _statusPanel;
        private TierPlansPanel? _plansPanel;
        private FeaturesPanel? _featuresPanel;
        private AddOnsPanel? _addOnsPanel;
        private UsageLimitsPanel? _usagePanel;
        private OverridesPanel? _overridesPanel;

        #endregion

        #region Lifecycle

        protected override async Task OnComponentInitializedAsync()
        {
            // Set initial active tab based on display mode
            if (DisplayMode != SubscriptionDisplayMode.All)
            {
                _activeTab = DisplayMode.ToString().ToLower();
            }
            else if (ShowStatusAboveTabs)
            {
                _activeTab = "plans";
            }

            // Fetch the tracking mode setting to determine user vs company scoping
            await LoadTrackingModeAsync();

            // Built after the tracking mode is known: the scope decides which endpoints the change
            // goes through, and an admin-scoped change never collects a card.
            _flow = new PlanChangeDriver(
                AppTierService,
                PaymentProviderService,
                new StripePaymentActions(JSRuntime, PaymentInstanceKey),
                new PlanChangeSettings
                {
                    AppId = AppId,
                    UserId = UseUserScope ? UserId : null,
                    CompanyId = UseCompanyScope ? CompanyId : null,
                    Labels = ResolvedLabels
                },
                EntitlementService,
                Logger)
            {
                StateChanged = StateHasChanged,
                PaymentRequested = OnPaymentRequired,
                Changed = HandleTierChangedAsync,
                EntitlementsChanged = ForwardEntitlementsChangedAsync,
                ErrorReported = ReportPlanChangeAsync
            };

            await LoadSubscriptionAsync();

            if (IsAdmin)
            {
                await LoadOverrideCountAsync();
            }
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
        /// Detaches the plan change from this component and drops the Stripe instance its 3-D
        /// Secure challenges created.
        /// </summary>
        /// <remarks>
        /// The driver's own disposal returns straight away when a bank challenge is still in
        /// flight — it drains that change to its completion in the background and drops the script
        /// instance afterwards — so leaving the page is never held up behind a customer finishing
        /// a challenge.
        ///
        /// The base's <see cref="BaseWildwoodComponent.Dispose()"/> is called by hand at the end:
        /// Blazor calls <c>DisposeAsync</c> INSTEAD of <c>Dispose</c> on a component that
        /// implements <see cref="IAsyncDisposable"/>, so without this the base's
        /// <c>ThemeChanged</c> unsubscription would never run.
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

        private async Task LoadTrackingModeAsync()
        {
            try
            {
                var mode = await AppTierService.GetTrackingModeAsync(AppId);
                _isCompanyMode = string.Equals(mode, "Company", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "[SubscriptionAdmin] Failed to load tracking mode for AppId={AppId}", AppId);
                await HandleErrorAsync(ex, "Loading tracking mode");
            }
        }

        private bool UseCompanyScope => _isCompanyMode && !string.IsNullOrEmpty(CompanyId);
        private bool UseUserScope => !_isCompanyMode && !string.IsNullOrEmpty(UserId);

        private async Task LoadOverrideCountAsync()
        {
            try
            {
                string? scopeUserId = UseUserScope ? UserId : null;
                var overrides = await AppTierService.GetFeatureOverridesAsync(AppId, scopeUserId);
                _overrideCount = overrides.Count;
            }
            catch
            {
                _overrideCount = 0;
            }
        }

        private async Task LoadSubscriptionAsync()
        {
            try
            {
                if (UseCompanyScope)
                {
                    _subscription = await AppTierService.GetCompanySubscriptionAsync(AppId, CompanyId!);
                }
                else if (UseUserScope)
                {
                    _subscription = await AppTierService.GetUserSubscriptionAsync(AppId, UserId!);
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

        #endregion

        #region Tab Navigation

        private void SetActiveTab(string tab)
        {
            _activeTab = tab;
            StateHasChanged();
        }

        private bool IsTabActive(string tab)
        {
            return string.Equals(_activeTab, tab, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Tier Selection Handler

        /// <summary>
        /// A plan was picked. Every change is PRICED and confirmed first, in every display mode —
        /// the shared flow's rule, and the reason a stacked layout no longer previews and then
        /// shows nothing.
        /// </summary>
        private Task HandleTierSelected(TierSelectedEventArgs args)
        {
            if (_isProcessing) return Task.CompletedTask;

            _pendingArgs = args;
            ClearError();

            return _flow is null ? Task.CompletedTask : _flow.SelectTierAsync(args);
        }

        private Task HandleConfirmChange(TierChangeConfirmOptions options)
        {
            ClearError();
            return _flow is null ? Task.CompletedTask : _flow.ConfirmAsync(options);
        }

        /// <summary>
        /// What the host needs to collect payment for a tier change. The rule itself lives on
        /// <see cref="PlanChangeDriver"/>, which is what both this component and the manage view
        /// build their request from; this stays as the name callers and tests already use.
        /// </summary>
        internal static PaymentRequiredArgs BuildPaymentRequiredArgs(
            TierSelectedEventArgs args, TierChangePreviewModel preview)
        {
            return PlanChangeDriver.BuildPaymentRequiredArgs(args, preview);
        }

        private Task HandleCancelConfirmation()
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

        /// <summary>
        /// The change landed: reload every panel and tell the host. The entitlement cache was
        /// already dropped by the driver, which has to own it — a change drained after this
        /// component has gone has no callback left to do it.
        /// </summary>
        private async Task HandleTierChangedAsync()
        {
            await RefreshAllPanels();

            var args = _pendingArgs;
            await NotifySubscriptionChanged(
                args?.TierName ?? string.Empty,
                args is not null && args.IsChange ? "changed" : "subscribed");
        }

        private Task ForwardEntitlementsChangedAsync(string reason)
        {
            return OnEntitlementsChanged.HasDelegate
                ? OnEntitlementsChanged.InvokeAsync(reason)
                : Task.CompletedTask;
        }

        /// <summary>
        /// A failed plan change. The flow's own notice says it on screen; this keeps the existing
        /// host contract, where the exception reaches the logger and <c>OnError</c>.
        /// </summary>
        private Task ReportPlanChangeAsync(string code, string message)
        {
            return InvokeOnErrorAsync(new InvalidOperationException(message), code);
        }

        /// <summary>A child part's failure is this component's failure: it is re-raised as one.</summary>
        private Task HandleChildErrorAsync(ComponentErrorEventArgs args)
        {
            return InvokeOnErrorAsync(args.Exception, args.Context);
        }

        #endregion

        #region Cancel Handler

        private async Task HandleCancelRequested()
        {
            if (_isProcessing) return;
            _isProcessing = true;
            StateHasChanged();

            try
            {
                AppTierCancelResultModel result;
                if (UseCompanyScope)
                {
                    result = await AppTierService.CancelCompanySubscriptionAsync(AppId, CompanyId!);
                }
                else if (UseUserScope)
                {
                    result = await AppTierService.CancelUserSubscriptionAsync(AppId, UserId!);
                }
                else
                {
                    result = await AppTierService.CancelSubscriptionAsync(AppId);
                }

                if (result.Success)
                {
                    // A scheduled cancellation keeps the subscription (status PendingCancellation)
                    // until the period ends — the refresh reloads whatever state the server has now.
                    await RefreshAllPanels();
                    await RaiseEntitlementsChangedAsync(EntitlementsChangedReasons.Cancel);
                    BuildCancelNotice(result);
                    await NotifySubscriptionChanged("", "cancelled");
                }
                else
                {
                    await HandleErrorAsync(
                        new Exception(string.IsNullOrEmpty(result.ErrorMessage) ? "Failed to cancel subscription" : result.ErrorMessage),
                        "Cancelling subscription");
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Cancelling subscription");
            }
            finally
            {
                _isProcessing = false;
                StateHasChanged();
            }
        }

        /// <summary>
        /// Builds the post-cancellation notice: scheduled-vs-immediate messaging plus, for
        /// store-billed subscriptions (RequiresUserAction), the store-billing follow-up
        /// instructions/link — the platform cannot stop the store's billing itself.
        /// </summary>
        private void BuildCancelNotice(AppTierCancelResultModel result)
        {
            _cancelNotice = result.IsScheduled
                ? result.EffectiveDate.HasValue
                    ? $"Your cancellation is scheduled — access continues until {result.EffectiveDate.Value.ToLocalTime():MMM d, yyyy}."
                    : "Your cancellation is scheduled for the end of the current billing period."
                : "Your subscription has been cancelled.";
            _cancelActionInstructions = result.RequiresUserAction ? result.UserActionInstructions : null;
            _cancelActionUrl = result.RequiresUserAction ? result.UserActionUrl : null;
        }

        private void ClearCancelNotice()
        {
            _cancelNotice = null;
            _cancelActionInstructions = null;
            _cancelActionUrl = null;
            StateHasChanged();
        }

        #endregion

        #region Add-On Change Handler

        /// <summary>
        /// A pack row finished. The panel says WHICH action it was, because the three do not share
        /// one reason: buying is <c>addOn</c>, cancelling is <c>cancel</c> and taking a scheduled
        /// cancellation back is <c>reactivate</c> — the same split JS's <c>wrapMutation</c> makes.
        /// </summary>
        private async Task HandleAddOnChanged(string reason)
        {
            // Refresh features and status after add-on changes
            await LoadSubscriptionAsync();

            if (_statusPanel != null)
            {
                _statusPanel.UpdateSubscription(_subscription);
            }
            if (_featuresPanel != null)
            {
                await _featuresPanel.LoadFeaturesAsync();
            }

            // Add-ons grant features — refresh FeatureGate instances elsewhere in the app.
            await RaiseEntitlementsChangedAsync(reason);
            await NotifySubscriptionChanged("", "addon_changed");
        }

        #endregion

        #region Override Removed Handler

        private async Task HandleOverrideRemoved()
        {
            // Refresh features panel to reflect reverted state
            if (_featuresPanel != null)
            {
                await _featuresPanel.LoadFeaturesAsync();
            }
            await LoadOverrideCountAsync();
            // An override is granted or revoked by hand, outside any plan — JS's "manual".
            await RaiseEntitlementsChangedAsync(EntitlementsChangedReasons.Manual);
            StateHasChanged();
        }

        private async Task HandleOverrideToggled()
        {
            // Refresh overrides panel when a feature toggle creates/updates an override
            if (_overridesPanel != null)
            {
                await _overridesPanel.LoadDataAsync();
            }
            await LoadOverrideCountAsync();
            await RaiseEntitlementsChangedAsync(EntitlementsChangedReasons.Manual);
            StateHasChanged();
        }

        #endregion

        #region Helpers

        private async Task RefreshAllPanels()
        {
            await LoadSubscriptionAsync();

            if (_statusPanel != null)
            {
                _statusPanel.UpdateSubscription(_subscription);
            }
            if (_plansPanel != null)
            {
                _plansPanel.UpdateSubscription(_subscription);
            }
            if (_featuresPanel != null)
            {
                await _featuresPanel.LoadFeaturesAsync();
            }
            if (_addOnsPanel != null)
            {
                await _addOnsPanel.LoadDataAsync();
            }
            if (_usagePanel != null)
            {
                await _usagePanel.LoadDataAsync();
            }
        }

        /// <summary>
        /// Drops the shared entitlement cache with the reason attached, then tells the host. Both
        /// halves always happen together: a cache that is dropped without saying why leaves a host
        /// unable to tell a plan change from a pack purchase, and a callback without the eviction
        /// leaves every FeatureGate in the circuit serving the old plan for the cache TTL.
        /// </summary>
        private async Task RaiseEntitlementsChangedAsync(string reason)
        {
            EntitlementService.Invalidate(AppId, reason);

            if (OnEntitlementsChanged.HasDelegate)
            {
                await OnEntitlementsChanged.InvokeAsync(reason);
            }
        }

        private async Task NotifySubscriptionChanged(string tierName, string action)
        {
            if (OnSubscriptionChanged.HasDelegate)
            {
                await OnSubscriptionChanged.InvokeAsync(new AppTierSubscriptionChangedEventArgs
                {
                    SubscriptionId = _subscription?.Id ?? string.Empty,
                    TierId = _subscription?.AppTierId ?? string.Empty,
                    TierName = tierName,
                    Action = action
                });
            }
        }

        private bool ShouldShowTab(string tab)
        {
            if (DisplayMode == SubscriptionDisplayMode.All) return true;
            return string.Equals(DisplayMode.ToString(), tab, StringComparison.OrdinalIgnoreCase);
        }

        private bool ShouldShowSinglePanel()
        {
            return DisplayMode != SubscriptionDisplayMode.All;
        }

        #endregion
    }

    /// <summary>
    /// Details passed to <see cref="SubscriptionAdminComponent.OnPaymentRequired"/> when a tier
    /// change requires payment. The handler returns a payment transaction id (or null to cancel).
    /// </summary>
    /// <summary>
    /// What the host needs to collect payment for a tier change. Additive since the first release:
    /// a caller that only reads TierId/TierName/PricingId/Price keeps working.
    /// </summary>
    public class PaymentRequiredArgs
    {
        public string TierId { get; set; } = string.Empty;
        public string TierName { get; set; } = string.Empty;

        /// <summary>The tier's pricing option (AppTierPricing id). Not a pricing model id.</summary>
        public string? PricingId { get; set; }

        /// <summary>
        /// The pricing model behind that option — what <c>PaymentComponent.PricingModelId</c>
        /// needs. Hosts used to wire <see cref="PricingId"/> there; the server found no pricing
        /// model for it and charged the prorated amount once, with no recurring subscription,
        /// renewal or trial.
        /// </summary>
        public string? PricingModelId { get; set; }

        /// <summary>
        /// The amount the plan's subscription charges (the pricing option's price) — NOT the
        /// prorated one-time charge, which is not what the new subscription bills.
        /// </summary>
        public decimal Price { get; set; }

        /// <summary>
        /// Free-trial days on the pricing option; pass to <c>PaymentComponent.TrialDays</c>.
        /// </summary>
        public int? TrialDays { get; set; }
    }
}
