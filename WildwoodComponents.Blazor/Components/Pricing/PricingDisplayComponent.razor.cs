using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;

namespace WildwoodComponents.Blazor.Components.Pricing
{
    public partial class PricingDisplayComponent : BaseWildwoodComponent
    {
        [Inject] private IAppTierComponentService AppTierService { get; set; } = default!;

        #region Parameters

        [Parameter, EditorRequired]
        public string AppId { get; set; } = string.Empty;

        [Parameter] public string? Title { get; set; }
        [Parameter] public string? Subtitle { get; set; }
        [Parameter] public bool ShowBillingToggle { get; set; } = true;
        [Parameter] public bool ShowFeatureComparison { get; set; } = true;
        [Parameter] public bool ShowLimits { get; set; } = true;
        [Parameter] public string Currency { get; set; } = "USD";
        [Parameter] public string? EnterpriseContactUrl { get; set; }

        /// <summary>
        /// Optional pre-loaded tiers. If provided, skips the API call.
        /// </summary>
        [Parameter] public List<AppTierModel>? PreloadedTiers { get; set; }

        /// <summary>
        /// Fired when a user selects a tier. Passes the selected tier and its pricing.
        /// </summary>
        [Parameter] public EventCallback<PricingTierSelectedEventArgs> OnSelectTier { get; set; }

        #endregion

        #region State

        private List<AppTierModel> _tiers = new();
        private bool _billingAnnual;

        #endregion

        #region Lifecycle

        protected override async Task OnComponentInitializedAsync()
        {
            if (PreloadedTiers != null)
            {
                _tiers = PreloadedTiers;
                SortTiersByDisplayOrder(_tiers);
                return;
            }

            if (string.IsNullOrEmpty(AppId))
            {
                await HandleErrorAsync(new InvalidOperationException("No AppId provided"), "Loading pricing");
                return;
            }

            await LoadTiers();
        }

        private async Task LoadTiers()
        {
            await SetLoadingAsync(true);
            try
            {
                _tiers = await AppTierService.GetPublicTiersAsync(AppId);
                SortTiersByDisplayOrder(_tiers);
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Loading pricing tiers");
            }
            finally
            {
                await SetLoadingAsync(false);
            }
        }

        #endregion

        #region Actions

        private void ToggleBilling()
        {
            _billingAnnual = !_billingAnnual;
            StateHasChanged();
        }

        private async Task HandleSelectTier(AppTierModel tier)
        {
            if (OnSelectTier.HasDelegate)
            {
                var pricing = GetPricing(tier);
                await OnSelectTier.InvokeAsync(new PricingTierSelectedEventArgs
                {
                    Tier = tier,
                    SelectedPricing = pricing
                });
            }
        }

        #endregion

        #region Helpers

        private AppTierPricingModel? GetPricing(AppTierModel tier)
        {
            if (tier.PricingOptions.Count == 0) return null;

            if (_billingAnnual)
            {
                foreach (var p in tier.PricingOptions)
                {
                    if (string.Equals(p.BillingFrequency, "Yearly", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.BillingFrequency, "Annual", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.BillingFrequency, "Annually", StringComparison.OrdinalIgnoreCase))
                        return p;
                }
            }

            // Fall back to default or first
            AppTierPricingModel? defaultPricing = null;
            foreach (var p in tier.PricingOptions)
            {
                if (p.IsDefault) return p;
                if (defaultPricing == null) defaultPricing = p;
            }
            return defaultPricing;
        }

        private int GetAnnualDiscount(AppTierModel tier)
        {
            if (tier.PricingOptions.Count < 2) return 0;

            AppTierPricingModel? monthly = null;
            AppTierPricingModel? annual = null;

            foreach (var p in tier.PricingOptions)
            {
                if (string.Equals(p.BillingFrequency, "Monthly", StringComparison.OrdinalIgnoreCase))
                    monthly = p;
                if (string.Equals(p.BillingFrequency, "Yearly", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.BillingFrequency, "Annual", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.BillingFrequency, "Annually", StringComparison.OrdinalIgnoreCase))
                    annual = p;
            }

            if (monthly == null || annual == null) return 0;

            var monthlyTotal = monthly.Price * 12;
            if (annual.Price < monthlyTotal)
            {
                return (int)Math.Round((monthlyTotal - annual.Price) / monthlyTotal * 100);
            }

            return 0;
        }

        private int GetMaxAnnualDiscount()
        {
            int max = 0;
            foreach (var tier in _tiers)
            {
                var d = GetAnnualDiscount(tier);
                if (d > max) max = d;
            }
            return max;
        }

        private bool HasAnnualPricing()
        {
            foreach (var tier in _tiers)
            {
                foreach (var p in tier.PricingOptions)
                {
                    if (string.Equals(p.BillingFrequency, "Yearly", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.BillingFrequency, "Annual", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.BillingFrequency, "Annually", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        private bool IsEnterpriseTier(AppTierModel tier)
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

        private static string GetCurrencySymbol(string currency)
        {
            if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)) return "$";
            if (string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase)) return "\u20AC";
            if (string.Equals(currency, "GBP", StringComparison.OrdinalIgnoreCase)) return "\u00A3";
            if (string.Equals(currency, "JPY", StringComparison.OrdinalIgnoreCase)) return "\u00A5";
            if (string.Equals(currency, "INR", StringComparison.OrdinalIgnoreCase)) return "\u20B9";
            return currency;
        }

        private static void SortTiersByDisplayOrder(List<AppTierModel> tiers)
        {
            tiers.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));
        }

        #endregion
    }
}
