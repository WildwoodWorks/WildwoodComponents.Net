using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Components.Subscription.Admin
{
    public partial class SubscriptionAdminComponent : BaseWildwoodComponent
    {
        [Inject] private IAppTierComponentService AppTierService { get; set; } = default!;

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
        /// Called when the server confirms a tier change requires payment and no card is on file.
        /// Return a payment transaction id to complete the change, or null/empty to cancel.
        /// Consumers typically wire this to a modal containing PaymentFormComponent.
        /// When not wired, a payment-required change surfaces an error via the alert banner.
        /// </summary>
        [Parameter] public Func<PaymentRequiredArgs, Task<string?>>? OnPaymentRequired { get; set; }

        /// <summary>Override the internally-fetched limit statuses (e.g. with locally-merged real-time usage data).</summary>
        [Parameter] public IReadOnlyList<AppTierLimitStatusModel>? LimitStatusesOverride { get; set; }

        #endregion

        #region State

        private UserTierSubscriptionModel? _subscription;
        private string _activeTab = "subscription";
        private bool _isProcessing;
        private bool _isCompanyMode;
        private int _overrideCount;
        private TierChangePreviewModel? _preview;
        private TierSelectedEventArgs? _pendingArgs;

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
            await LoadSubscriptionAsync();

            if (IsAdmin)
            {
                await LoadOverrideCountAsync();
            }
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

        private async Task HandleTierSelected(TierSelectedEventArgs args)
        {
            if (_isProcessing) return;

            if (args.IsChange)
            {
                // Show preview modal for tier changes
                _isProcessing = true;
                StateHasChanged();

                try
                {
                    // Admin managing a specific user previews against that user; self/company
                    // mode falls back to the self preview (no company-scoped preview endpoint).
                    var preview = UseUserScope
                        ? await AppTierService.PreviewTierChangeAdminAsync(AppId, UserId!, args.TierId, args.PricingId)
                        : await AppTierService.PreviewTierChangeAsync(AppId, args.TierId, args.PricingId);
                    if (preview != null)
                    {
                        _preview = preview;
                        _pendingArgs = args;
                    }
                    else
                    {
                        await HandleErrorAsync(new Exception("Failed to load tier change preview"), "Previewing tier change");
                    }
                }
                catch (Exception ex)
                {
                    await HandleErrorAsync(ex, "Previewing tier change");
                }
                finally
                {
                    _isProcessing = false;
                    StateHasChanged();
                }
            }
            else
            {
                // New subscription - execute directly (no preview needed)
                await ExecuteTierChange(args, true, null);
            }
        }

        private async Task HandleConfirmChange(TierChangeConfirmOptions options)
        {
            if (_pendingArgs == null) return;

            var args = _pendingArgs;
            var preview = _preview;
            _preview = null;
            _pendingArgs = null;
            StateHasChanged();

            // When the server says payment is required (and the admin didn't bypass it),
            // collect a payment transaction id via the OnPaymentRequired extension point
            // before committing the change. Mirrors the React preview -> payment -> change flow.
            string? paymentTransactionId = null;
            if (preview != null && preview.PaymentRequired && !options.BypassPayment)
            {
                if (OnPaymentRequired == null)
                {
                    await HandleErrorAsync(
                        new Exception("Payment is required for this tier change. Wire the OnPaymentRequired callback to collect payment."),
                        "Tier change");
                    return;
                }

                paymentTransactionId = await OnPaymentRequired(new PaymentRequiredArgs
                {
                    TierId = args.TierId,
                    TierName = args.TierName,
                    PricingId = args.PricingId,
                    Price = preview.ProratedChargeToday ?? preview.NewPrice ?? 0m,
                });

                // Null/empty transaction id means the consumer cancelled payment collection.
                if (string.IsNullOrEmpty(paymentTransactionId)) return;
            }

            await ExecuteTierChange(args, options.Immediate, paymentTransactionId);
        }

        private void HandleCancelConfirmation()
        {
            _preview = null;
            _pendingArgs = null;
            StateHasChanged();
        }

        private async Task ExecuteTierChange(TierSelectedEventArgs args, bool immediate, string? paymentTransactionId)
        {
            if (_isProcessing) return;
            _isProcessing = true;
            StateHasChanged();

            try
            {
                AppTierChangeResultModel result;

                if (args.IsChange)
                {
                    // Change existing tier. Admin/company-scoped changes route through the admin
                    // endpoints (no self-service payment token); self-service forwards the token.
                    if (UseCompanyScope)
                    {
                        result = await AppTierService.ChangeCompanyTierAsync(
                            AppId, CompanyId!, args.TierId, args.PricingId, immediate);
                    }
                    else if (UseUserScope)
                    {
                        result = await AppTierService.ChangeUserTierAsync(
                            AppId, UserId!, args.TierId, args.PricingId, immediate);
                    }
                    else
                    {
                        result = await AppTierService.ChangeTierAsync(
                            AppId, args.TierId, args.PricingId, immediate, paymentTransactionId);
                    }
                }
                else
                {
                    // New subscription
                    if (UseCompanyScope)
                    {
                        result = await AppTierService.SubscribeCompanyToTierAsync(
                            AppId, CompanyId!, args.TierId, args.PricingId);
                    }
                    else if (UseUserScope)
                    {
                        result = await AppTierService.SubscribeUserToTierAsync(
                            AppId, UserId!, args.TierId, args.PricingId);
                    }
                    else
                    {
                        result = await AppTierService.SubscribeToTierAsync(
                            AppId, args.TierId, args.PricingId, paymentTransactionId);
                    }
                }

                if (result.Success)
                {
                    _subscription = result.Subscription;
                    await RefreshAllPanels();
                    await NotifySubscriptionChanged(args.TierName, args.IsChange ? "changed" : "subscribed");
                }
                else
                {
                    await HandleErrorAsync(new Exception(result.ErrorMessage), "Assigning tier");
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Assigning tier");
            }
            finally
            {
                _isProcessing = false;
                StateHasChanged();
            }
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
                bool success;
                if (UseCompanyScope)
                {
                    success = await AppTierService.CancelCompanySubscriptionAsync(AppId, CompanyId!);
                }
                else if (UseUserScope)
                {
                    success = await AppTierService.CancelUserSubscriptionAsync(AppId, UserId!);
                }
                else
                {
                    success = await AppTierService.CancelSubscriptionAsync(AppId);
                }

                if (success)
                {
                    _subscription = null;
                    await RefreshAllPanels();
                    await NotifySubscriptionChanged("", "cancelled");
                }
                else
                {
                    await HandleErrorAsync(new Exception("Failed to cancel subscription"), "Cancelling subscription");
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

        #endregion

        #region Add-On Change Handler

        private async Task HandleAddOnChanged()
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
    public class PaymentRequiredArgs
    {
        public string TierId { get; set; } = string.Empty;
        public string TierName { get; set; } = string.Empty;
        public string? PricingId { get; set; }
        public decimal Price { get; set; }
    }
}
