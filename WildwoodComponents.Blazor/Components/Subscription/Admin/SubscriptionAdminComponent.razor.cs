using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
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
        [Parameter] public SubscriptionDisplayMode DisplayMode { get; set; } = SubscriptionDisplayMode.All;
        [Parameter] public bool IsAdmin { get; set; }
        [Parameter] public string Currency { get; set; } = "USD";
        [Parameter] public bool ShowBillingToggle { get; set; } = true;
        /// <summary>When true, render the subscription status card above the tab bar instead of as a tab.</summary>
        [Parameter] public bool ShowStatusAboveTabs { get; set; }
        [Parameter] public EventCallback<AppTierSubscriptionChangedEventArgs> OnSubscriptionChanged { get; set; }

        #endregion

        #region State

        private UserTierSubscriptionModel? _subscription;
        private string _activeTab = "subscription";
        private bool _isProcessing;

        // References to child panels for refreshing
        private SubscriptionStatusPanel? _statusPanel;
        private TierPlansPanel? _plansPanel;
        private FeaturesPanel? _featuresPanel;
        private AddOnsPanel? _addOnsPanel;
        private UsageLimitsPanel? _usagePanel;

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

            await LoadSubscriptionAsync();
        }

        private async Task LoadSubscriptionAsync()
        {
            try
            {
                if (!string.IsNullOrEmpty(CompanyId))
                {
                    _subscription = await AppTierService.GetCompanySubscriptionAsync(AppId, CompanyId);
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
            _isProcessing = true;
            StateHasChanged();

            try
            {
                AppTierChangeResultModel result;

                if (args.IsChange)
                {
                    // Change existing tier
                    if (!string.IsNullOrEmpty(CompanyId))
                    {
                        result = await AppTierService.ChangeCompanyTierAsync(
                            AppId, CompanyId, args.TierId, args.PricingId, true);
                    }
                    else
                    {
                        result = await AppTierService.ChangeTierAsync(
                            AppId, args.TierId, args.PricingId, true);
                    }
                }
                else
                {
                    // New subscription
                    if (!string.IsNullOrEmpty(CompanyId))
                    {
                        result = await AppTierService.SubscribeCompanyToTierAsync(
                            AppId, CompanyId, args.TierId, args.PricingId);
                    }
                    else
                    {
                        result = await AppTierService.SubscribeToTierAsync(
                            AppId, args.TierId, args.PricingId, null);
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
                if (!string.IsNullOrEmpty(CompanyId))
                {
                    success = await AppTierService.CancelCompanySubscriptionAsync(AppId, CompanyId);
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
}
