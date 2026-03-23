using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Components.Subscription.Admin
{
    public partial class AddOnsPanel : BaseWildwoodComponent
    {
        [Inject] private IAppTierComponentService AppTierService { get; set; } = default!;

        [Parameter, EditorRequired] public string AppId { get; set; } = string.Empty;
        [Parameter] public string? CompanyId { get; set; }
        [Parameter] public string? UserId { get; set; }
        [Parameter] public bool IsCompanyMode { get; set; }
        [Parameter] public bool IsAdmin { get; set; }
        [Parameter] public string? CurrentTierId { get; set; }
        [Parameter] public EventCallback OnSubscriptionChanged { get; set; }

        private List<UserAddOnSubscriptionModel> _activeSubscriptions = new();
        private List<AppTierAddOnModel> _availableAddOns = new();
        private bool _isProcessing;
        private string? _processingAddOnId;

        private bool UseCompanyScope => IsCompanyMode && !string.IsNullOrEmpty(CompanyId);
        private bool UseUserScope => !IsCompanyMode && !string.IsNullOrEmpty(UserId);

        protected override async Task OnComponentInitializedAsync()
        {
            await LoadDataAsync();
        }

        public async Task LoadDataAsync()
        {
            try
            {
                await SetLoadingAsync(true);

                // Load active add-on subscriptions
                if (UseCompanyScope)
                {
                    _activeSubscriptions = await AppTierService.GetCompanyAddOnSubscriptionsAsync(AppId, CompanyId);
                }
                else if (UseUserScope)
                {
                    _activeSubscriptions = await AppTierService.GetUserAddOnsAsync(AppId, UserId);
                }
                else
                {
                    _activeSubscriptions = await AppTierService.GetMyAddOnsAsync(AppId);
                }

                // Load available add-ons
                if (IsAdmin)
                {
                    _availableAddOns = await AppTierService.GetAllAddOnsAsync(AppId);
                }
                else
                {
                    _availableAddOns = await AppTierService.GetAvailableAddOnsAsync(AppId);
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Loading add-ons");
            }
            finally
            {
                await SetLoadingAsync(false);
            }
        }

        private async Task SubscribeToAddOn(string addOnId)
        {
            if (_isProcessing) return;
            _isProcessing = true;
            _processingAddOnId = addOnId;
            StateHasChanged();

            try
            {
                bool success;
                if (UseCompanyScope)
                {
                    success = await AppTierService.SubscribeCompanyToAddOnAsync(AppId, CompanyId, addOnId);
                }
                else if (UseUserScope)
                {
                    success = await AppTierService.SubscribeUserToAddOnAsync(AppId, UserId, addOnId);
                }
                else
                {
                    success = await AppTierService.SubscribeToAddOnAsync(AppId, addOnId, null, null);
                }

                if (success)
                {
                    await LoadDataAsync();
                    if (OnSubscriptionChanged.HasDelegate)
                    {
                        await OnSubscriptionChanged.InvokeAsync();
                    }
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
                _processingAddOnId = null;
                StateHasChanged();
            }
        }

        private async Task CancelAddOn(string subscriptionId, bool immediate)
        {
            if (_isProcessing) return;
            _isProcessing = true;
            StateHasChanged();

            try
            {
                bool success;
                if (UseCompanyScope)
                {
                    success = await AppTierService.CancelCompanyAddOnAsync(subscriptionId, immediate);
                }
                else if (UseUserScope)
                {
                    success = await AppTierService.CancelUserAddOnAsync(AppId, subscriptionId);
                }
                else
                {
                    success = await AppTierService.CancelAddOnSubscriptionAsync(subscriptionId);
                }

                if (success)
                {
                    await LoadDataAsync();
                    if (OnSubscriptionChanged.HasDelegate)
                    {
                        await OnSubscriptionChanged.InvokeAsync();
                    }
                }
                else
                {
                    await HandleErrorAsync(new Exception("Failed to cancel add-on"), "Cancelling add-on");
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Cancelling add-on");
            }
            finally
            {
                _isProcessing = false;
                StateHasChanged();
            }
        }

        private bool IsAddOnSubscribed(AppTierAddOnModel addOn)
        {
            foreach (var sub in _activeSubscriptions)
            {
                if (sub.AppTierAddOnId == addOn.Id &&
                    (string.Equals(sub.Status, "Active", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(sub.Status, "Trialing", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
        }

        private bool IsAddOnBundled(AppTierAddOnModel addOn)
        {
            if (string.IsNullOrEmpty(CurrentTierId)) return false;
            foreach (var tierId in addOn.BundledInTierIds)
            {
                if (tierId == CurrentTierId)
                    return true;
            }
            return false;
        }

        private static AppTierAddOnPricingModel? GetDefaultPricing(AppTierAddOnModel addOn)
        {
            AppTierAddOnPricingModel? first = null;
            foreach (var p in addOn.PricingOptions)
            {
                if (first == null) first = p;
                if (p.IsDefault) return p;
            }
            return first;
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
}
