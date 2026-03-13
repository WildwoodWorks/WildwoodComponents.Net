using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Components.Subscription.Admin
{
    public partial class TierPlansPanel : BaseWildwoodComponent
    {
        [Inject] private IAppTierComponentService AppTierService { get; set; } = default!;

        [Parameter, EditorRequired] public string AppId { get; set; } = string.Empty;
        [Parameter] public string? CompanyId { get; set; }
        [Parameter] public bool IsAdmin { get; set; }
        [Parameter] public string Currency { get; set; } = "USD";
        [Parameter] public bool ShowBillingToggle { get; set; } = true;
        [Parameter] public EventCallback<TierSelectedEventArgs> OnTierSelected { get; set; }

        private List<AppTierModel> _tiers = new();
        private UserTierSubscriptionModel? _currentSubscription;
        private string _selectedBillingCycle = "monthly";
        private bool _isProcessing;

        protected override async Task OnComponentInitializedAsync()
        {
            await LoadDataAsync();
        }

        public async Task LoadDataAsync()
        {
            try
            {
                await SetLoadingAsync(true);
                _tiers = await AppTierService.GetAvailableTiersAsync(AppId);
                SortTiersByDisplayOrder(_tiers);

                if (!string.IsNullOrEmpty(CompanyId))
                {
                    _currentSubscription = await AppTierService.GetCompanySubscriptionAsync(AppId, CompanyId);
                }
                else
                {
                    _currentSubscription = await AppTierService.GetMySubscriptionAsync(AppId);
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Loading tier plans");
            }
            finally
            {
                await SetLoadingAsync(false);
            }
        }

        public void UpdateSubscription(UserTierSubscriptionModel? subscription)
        {
            _currentSubscription = subscription;
            StateHasChanged();
        }

        private async Task SelectTier(AppTierModel tier, string? pricingId)
        {
            if (_isProcessing) return;
            _isProcessing = true;
            StateHasChanged();

            try
            {
                if (OnTierSelected.HasDelegate)
                {
                    var pricing = FindPricingForCycle(tier, _selectedBillingCycle);
                    await OnTierSelected.InvokeAsync(new TierSelectedEventArgs
                    {
                        TierId = tier.Id,
                        TierName = tier.Name,
                        PricingId = pricingId ?? pricing?.Id,
                        Price = pricing?.Price ?? 0,
                        IsFreeTier = tier.IsFreeTier,
                        IsChange = _currentSubscription != null && _currentSubscription.IsActive
                    });
                }
            }
            finally
            {
                _isProcessing = false;
                StateHasChanged();
            }
        }

        private void SetBillingCycle(string cycle)
        {
            _selectedBillingCycle = cycle;
            StateHasChanged();
        }

        private bool IsTierCurrentPlan(AppTierModel tier)
        {
            if (_currentSubscription == null) return false;
            return _currentSubscription.AppTierId == tier.Id && _currentSubscription.IsActive;
        }

        private bool HasMonthlyAndAnnualPricing()
        {
            foreach (var tier in _tiers)
            {
                bool hasMonthly = false;
                bool hasAnnual = false;
                foreach (var p in tier.PricingOptions)
                {
                    if (string.Equals(p.BillingFrequency, "Monthly", StringComparison.OrdinalIgnoreCase))
                        hasMonthly = true;
                    if (string.Equals(p.BillingFrequency, "Annually", StringComparison.OrdinalIgnoreCase))
                        hasAnnual = true;
                }
                if (hasMonthly && hasAnnual) return true;
            }
            return false;
        }

        private static void SortTiersByDisplayOrder(List<AppTierModel> tiers)
        {
            tiers.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));
        }

        private static AppTierPricingModel? FindPricingForCycle(AppTierModel tier, string cycle)
        {
            AppTierPricingModel? defaultPricing = null;
            foreach (var p in tier.PricingOptions)
            {
                if (string.Equals(cycle, "annually", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.BillingFrequency, "Annually", StringComparison.OrdinalIgnoreCase))
                    return p;

                if (string.Equals(cycle, "monthly", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.BillingFrequency, "Monthly", StringComparison.OrdinalIgnoreCase))
                    return p;

                if (p.IsDefault)
                    defaultPricing = p;
            }

            if (defaultPricing != null) return defaultPricing;
            if (tier.PricingOptions.Count > 0) return tier.PricingOptions[0];
            return null;
        }

        private static string GetCurrencySymbol(string currency)
        {
            if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)) return "$";
            if (string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase)) return "\u20AC";
            if (string.Equals(currency, "GBP", StringComparison.OrdinalIgnoreCase)) return "\u00A3";
            return currency;
        }

        private int GetCurrentTierDisplayOrder()
        {
            if (_currentSubscription == null) return 0;
            foreach (var tier in _tiers)
            {
                if (tier.Id == _currentSubscription.AppTierId)
                    return tier.DisplayOrder;
            }
            return 0;
        }

        private static string GetBadgeColorClass(string badgeColor)
        {
            if (string.IsNullOrEmpty(badgeColor)) return "bg-primary";
            if (string.Equals(badgeColor, "primary", StringComparison.OrdinalIgnoreCase)) return "bg-primary";
            if (string.Equals(badgeColor, "success", StringComparison.OrdinalIgnoreCase)) return "bg-success";
            if (string.Equals(badgeColor, "warning", StringComparison.OrdinalIgnoreCase)) return "bg-warning";
            if (string.Equals(badgeColor, "danger", StringComparison.OrdinalIgnoreCase)) return "bg-danger";
            if (string.Equals(badgeColor, "info", StringComparison.OrdinalIgnoreCase)) return "bg-info";
            return "bg-primary";
        }
    }

    public class TierSelectedEventArgs
    {
        public string TierId { get; set; } = string.Empty;
        public string TierName { get; set; } = string.Empty;
        public string? PricingId { get; set; }
        public decimal Price { get; set; }
        public bool IsFreeTier { get; set; }
        public bool IsChange { get; set; }
    }
}
