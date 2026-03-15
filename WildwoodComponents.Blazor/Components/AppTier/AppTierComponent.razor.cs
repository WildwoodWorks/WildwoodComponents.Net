using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;

namespace WildwoodComponents.Blazor.Components.AppTier
{
    public partial class AppTierComponent : BaseWildwoodComponent
    {
        [Inject] private IAppTierComponentService AppTierService { get; set; } = default!;

        #region Parameters

        [Parameter, EditorRequired]
        public string AppId { get; set; } = string.Empty;

        [Parameter] public string? Title { get; set; }
        [Parameter] public string? Subtitle { get; set; }
        [Parameter] public string? PreSelectedTierId { get; set; }
        [Parameter] public bool ShowAddOns { get; set; } = true;
        [Parameter] public bool ShowBillingToggle { get; set; } = true;
        [Parameter] public bool ShowCurrentPlan { get; set; } = true;
        [Parameter] public bool ShowFeatureComparison { get; set; } = true;
        [Parameter] public bool ShowLimits { get; set; } = true;
        [Parameter] public string Currency { get; set; } = "USD";
        [Parameter] public int? AnnualDiscount { get; set; }
        [Parameter] public bool RequireBillingAddress { get; set; } = false;

        [Parameter] public string? EnterpriseContactUrl { get; set; }

        // Payment-related parameters
        [Parameter] public string? CustomerId { get; set; }
        [Parameter] public string? CustomerEmail { get; set; }

        // Registration parameters
        [Parameter] public bool AllowRegistration { get; set; }
        [Parameter] public EventCallback<AuthenticationResponse> OnRegistrationComplete { get; set; }

        [Parameter] public EventCallback<AppTierSelectedEventArgs> OnTierSelected { get; set; }
        [Parameter] public EventCallback<AppTierSubscriptionChangedEventArgs> OnSubscriptionChanged { get; set; }
        [Parameter] public new EventCallback<ComponentErrorEventArgs> OnError { get; set; }

        #endregion

        #region State

        private List<AppTierModel> _tiers = new();
        private List<AppTierAddOnModel> _addOns = new();
        private UserTierSubscriptionModel? _currentSubscription;
        private List<UserAddOnSubscriptionModel> _myAddOns = new();
        private AppTierComponentStep _currentStep = AppTierComponentStep.TierSelection;
        private string _selectedBillingCycle = "monthly";
        private AppTierModel? _selectedTier;
        private AppTierPricingModel? _selectedPricing;
        private bool _isProcessing;
        private string? _loadingMessage;
        private bool _isAuthenticated;

        private enum AppTierComponentStep
        {
            TierSelection,
            Registration,
            Payment,
            AddOns,
            Success,
            CancelConfirmation
        }

        #endregion

        #region Lifecycle

        protected override async Task OnComponentInitializedAsync()
        {
            await LoadTierData();
        }

        private async Task LoadTierData()
        {
            _loadingMessage = "Loading plans...";
            await SetLoadingAsync(true);

            try
            {
                // Load tiers
                _tiers = await AppTierService.GetAvailableTiersAsync(AppId);

                // Sort tiers by display order (no LINQ - iOS compatible)
                SortTiersByDisplayOrder(_tiers);

                // Load current subscription
                _currentSubscription = await AppTierService.GetMySubscriptionAsync(AppId);

                // Load add-ons if enabled
                if (ShowAddOns)
                {
                    _addOns = await AppTierService.GetAvailableAddOnsAsync(AppId);
                    _myAddOns = await AppTierService.GetMyAddOnsAsync(AppId);
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Loading tier data");
            }
            finally
            {
                await SetLoadingAsync(false);
            }
        }

        #endregion

        #region Tier Selection

        private void SelectTier(AppTierModel tier)
        {
            _selectedTier = tier;

            // Find default pricing for selected billing cycle (no LINQ)
            _selectedPricing = FindPricingForCycle(tier, _selectedBillingCycle);

            // If registration is enabled and user is not authenticated, register first
            if (AllowRegistration && !_isAuthenticated)
            {
                _currentStep = AppTierComponentStep.Registration;
                StateHasChanged();
                return;
            }

            if (tier.IsFreeTier)
            {
                _ = SubscribeToFreeTier();
            }
            else
            {
                // Paid tier - go to payment step
                _currentStep = AppTierComponentStep.Payment;
                StateHasChanged();
            }
        }

        private async Task SubscribeToFreeTier()
        {
            if (_selectedTier == null) return;

            _isProcessing = true;
            StateHasChanged();

            try
            {
                var result = await AppTierService.SubscribeToTierAsync(AppId, _selectedTier.Id, null, null);

                if (result.Success)
                {
                    _currentSubscription = result.Subscription;
                    _currentStep = AppTierComponentStep.Success;
                    await NotifySubscriptionChanged(_selectedTier, "subscribed");
                }
                else
                {
                    await HandleErrorAsync(new Exception(result.ErrorMessage), "Subscribing to free tier");
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Subscribing to free tier");
            }
            finally
            {
                _isProcessing = false;
                StateHasChanged();
            }
        }

        private async Task HandlePaymentSuccess(PaymentSuccessEventArgs args)
        {
            if (_selectedTier == null) return;

            try
            {
                if (_currentSubscription != null && _currentSubscription.IsActive)
                {
                    // Change existing tier with payment proof
                    var result = await AppTierService.ChangeTierAsync(AppId, _selectedTier.Id, _selectedPricing?.Id, true);

                    if (result.Success)
                    {
                        _currentSubscription = result.Subscription;
                        _currentStep = AppTierComponentStep.Success;
                        await NotifySubscriptionChanged(_selectedTier, "changed");
                    }
                    else
                    {
                        await HandleErrorAsync(new Exception(result.ErrorMessage), "Changing tier after payment");
                    }
                }
                else
                {
                    // New subscription with payment transaction
                    var result = await AppTierService.SubscribeToTierAsync(
                        AppId, _selectedTier.Id, _selectedPricing?.Id, args.TransactionId);

                    if (result.Success)
                    {
                        _currentSubscription = result.Subscription;
                        _currentStep = AppTierComponentStep.Success;
                        await NotifySubscriptionChanged(_selectedTier, "subscribed");
                    }
                    else
                    {
                        await HandleErrorAsync(new Exception(result.ErrorMessage), "Subscribing after payment");
                    }
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Processing subscription after payment");
            }

            StateHasChanged();
        }

        private async Task HandlePaymentFailure(PaymentFailureEventArgs args)
        {
            await HandleErrorAsync(new Exception(args.ErrorMessage ?? "Payment failed"), "Payment processing");
        }

        #endregion

        #region Registration

        private async Task HandleAuthenticationSuccess(AuthenticationResponse response)
        {
            _isAuthenticated = true;

            // Set the auth token on the service so subsequent API calls are authenticated
            if (!string.IsNullOrEmpty(response.JwtToken))
            {
                AppTierService.SetAuthToken(response.JwtToken);
            }

            // Update customer email from auth response if not already set
            if (string.IsNullOrEmpty(CustomerEmail) && !string.IsNullOrEmpty(response.Email))
            {
                CustomerEmail = response.Email;
            }

            // Notify parent of registration completion
            if (OnRegistrationComplete.HasDelegate)
            {
                await OnRegistrationComplete.InvokeAsync(response);
            }

            // Continue with the subscription flow
            if (_selectedTier != null)
            {
                if (_selectedTier.IsFreeTier)
                {
                    await SubscribeToFreeTier();
                }
                else
                {
                    _currentStep = AppTierComponentStep.Payment;
                    StateHasChanged();
                }
            }
        }

        private void HandleAuthenticationError(string error)
        {
            _ = HandleErrorAsync(new Exception(error), "Registration");
        }

        #endregion

        #region Payment Helpers

        private decimal GetPaymentAmount()
        {
            if (_selectedPricing == null) return 0;
            return _selectedPricing.Price;
        }

        #endregion

        #region Cancellation

        private void ShowCancelConfirmation()
        {
            _currentStep = AppTierComponentStep.CancelConfirmation;
            StateHasChanged();
        }

        private async Task ConfirmCancellation()
        {
            _isProcessing = true;
            StateHasChanged();

            try
            {
                var success = await AppTierService.CancelSubscriptionAsync(AppId);

                if (success)
                {
                    var tierName = _currentSubscription?.TierName ?? "Unknown";
                    _currentSubscription = null;
                    _currentStep = AppTierComponentStep.TierSelection;

                    await NotifySubscriptionChanged(null, "cancelled");
                    await LoadTierData();
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

        #region Navigation

        private void BackToTierSelection()
        {
            _currentStep = AppTierComponentStep.TierSelection;
            _selectedTier = null;
            _selectedPricing = null;
            StateHasChanged();
        }

        private async Task HandleSuccessContinue()
        {
            if (_selectedTier != null && OnTierSelected.HasDelegate)
            {
                await OnTierSelected.InvokeAsync(new AppTierSelectedEventArgs
                {
                    TierId = _selectedTier.Id,
                    TierName = _selectedTier.Name,
                    PricingId = _selectedPricing?.Id ?? string.Empty,
                    Price = _selectedPricing?.Price ?? 0,
                    BillingFrequency = _selectedBillingCycle,
                    IsFreeTier = _selectedTier.IsFreeTier
                });
            }
        }

        #endregion

        #region Billing Cycle

        private void SetBillingCycle(string cycle)
        {
            _selectedBillingCycle = cycle;
            StateHasChanged();
        }

        #endregion

        #region Helpers

        private async Task NotifySubscriptionChanged(AppTierModel? tier, string action)
        {
            if (OnSubscriptionChanged.HasDelegate)
            {
                await OnSubscriptionChanged.InvokeAsync(new AppTierSubscriptionChangedEventArgs
                {
                    SubscriptionId = _currentSubscription?.Id ?? string.Empty,
                    TierId = tier?.Id ?? string.Empty,
                    TierName = tier?.Name ?? string.Empty,
                    Action = action
                });
            }
        }

        private static void SortTiersByDisplayOrder(List<AppTierModel> tiers)
        {
            tiers.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));
        }

        private static bool IsAnnualFrequency(string? frequency)
        {
            if (string.IsNullOrEmpty(frequency)) return false;
            return string.Equals(frequency, "Annually", StringComparison.OrdinalIgnoreCase)
                || string.Equals(frequency, "Annual", StringComparison.OrdinalIgnoreCase)
                || string.Equals(frequency, "Yearly", StringComparison.OrdinalIgnoreCase);
        }

        private static AppTierPricingModel? FindPricingForCycle(AppTierModel tier, string cycle)
        {
            // Find monthly or annual pricing (no LINQ - iOS compatible)
            bool wantAnnual = string.Equals(cycle, "annually", StringComparison.OrdinalIgnoreCase);
            AppTierPricingModel? defaultPricing = null;
            foreach (var p in tier.PricingOptions)
            {
                if (wantAnnual && IsAnnualFrequency(p.BillingFrequency))
                    return p;

                if (!wantAnnual && string.Equals(p.BillingFrequency, "Monthly", StringComparison.OrdinalIgnoreCase))
                    return p;

                if (p.IsDefault)
                    defaultPricing = p;
            }

            // Fall back to default or first
            if (defaultPricing != null) return defaultPricing;
            if (tier.PricingOptions.Count > 0) return tier.PricingOptions[0];
            return null;
        }

        private static string GetPricePeriod(string billingCycle)
        {
            if (string.Equals(billingCycle, "annually", StringComparison.OrdinalIgnoreCase))
                return "/year";
            return "/month";
        }

        private static string GetCurrencySymbol(string currency)
        {
            if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)) return "$";
            if (string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase)) return "\u20AC";
            if (string.Equals(currency, "GBP", StringComparison.OrdinalIgnoreCase)) return "\u00A3";
            return currency;
        }

        private int GetMaxAnnualDiscount()
        {
            if (AnnualDiscount.HasValue) return AnnualDiscount.Value;
            int max = 0;
            foreach (var tier in _tiers)
            {
                var d = ComputeAnnualDiscount(tier);
                if (d > max) max = d;
            }
            return max;
        }

        private static int ComputeAnnualDiscount(AppTierModel tier)
        {
            if (tier.PricingOptions.Count < 2) return 0;
            AppTierPricingModel? monthly = null;
            AppTierPricingModel? annual = null;
            foreach (var p in tier.PricingOptions)
            {
                if (string.Equals(p.BillingFrequency, "Monthly", StringComparison.OrdinalIgnoreCase))
                    monthly = p;
                if (IsAnnualFrequency(p.BillingFrequency))
                    annual = p;
            }
            if (monthly == null || annual == null || monthly.Price <= 0) return 0;
            var fullAnnual = monthly.Price * 12;
            var discount = (int)Math.Round(((fullAnnual - annual.Price) / fullAnnual) * 100);
            return discount > 0 ? discount : 0;
        }

        private static bool IsEnterpriseTier(AppTierModel tier)
        {
            return !tier.IsFreeTier && tier.PricingOptions.Count == 0;
        }

        private string FormatPrice(decimal amount)
        {
            var symbol = GetCurrencySymbol(Currency);
            if (string.Equals(Currency, "JPY", StringComparison.OrdinalIgnoreCase))
                return $"{symbol}{Math.Round(amount)}";
            return $"{symbol}{amount:N2}";
        }

        private bool IsTierCurrentPlan(AppTierModel tier)
        {
            if (_currentSubscription == null) return false;
            return _currentSubscription.AppTierId == tier.Id && _currentSubscription.IsActive;
        }

        private bool IsTierPreSelected(AppTierModel tier)
        {
            return !string.IsNullOrEmpty(PreSelectedTierId) && PreSelectedTierId == tier.Id;
        }

        private bool HasMonthlyAndAnnualPricing()
        {
            // Check if any tier has both billing options (no LINQ)
            foreach (var tier in _tiers)
            {
                bool hasMonthly = false;
                bool hasAnnual = false;
                foreach (var p in tier.PricingOptions)
                {
                    if (string.Equals(p.BillingFrequency, "Monthly", StringComparison.OrdinalIgnoreCase))
                        hasMonthly = true;
                    if (IsAnnualFrequency(p.BillingFrequency))
                        hasAnnual = true;
                }
                if (hasMonthly && hasAnnual) return true;
            }
            return false;
        }

        private bool IsAddOnSubscribed(AppTierAddOnModel addOn)
        {
            foreach (var sub in _myAddOns)
            {
                if (sub.AppTierAddOnId == addOn.Id &&
                    string.Equals(sub.Status, "Active", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private bool IsAddOnBundled(AppTierAddOnModel addOn)
        {
            if (_currentSubscription == null) return false;
            foreach (var tierId in addOn.BundledInTierIds)
            {
                if (tierId == _currentSubscription.AppTierId)
                    return true;
            }
            return false;
        }

        private static AppTierAddOnPricingModel? GetDefaultAddOnPricing(AppTierAddOnModel addOn)
        {
            AppTierAddOnPricingModel? first = null;
            foreach (var p in addOn.PricingOptions)
            {
                if (first == null) first = p;
                if (p.IsDefault) return p;
            }
            return first;
        }

        private async Task SubscribeToAddOn(AppTierAddOnModel addOn)
        {
            if (_isProcessing) return;
            _isProcessing = true;
            StateHasChanged();

            try
            {
                var pricing = GetDefaultAddOnPricing(addOn);
                var success = await AppTierService.SubscribeToAddOnAsync(AppId, addOn.Id, pricing?.Id, null);

                if (success)
                {
                    _myAddOns = await AppTierService.GetMyAddOnsAsync(AppId);
                }
                else
                {
                    await HandleErrorAsync(new Exception("Failed to subscribe to add-on"), "Subscribing to add-on");
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Subscribing to add-on");
            }
            finally
            {
                _isProcessing = false;
                StateHasChanged();
            }
        }

        #endregion
    }
}
