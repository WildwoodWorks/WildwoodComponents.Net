using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Components.Subscription.Admin
{
    public partial class SubscriptionStatusPanel : BaseWildwoodComponent
    {
        [Inject] private IAppTierComponentService AppTierService { get; set; } = default!;

        [Parameter, EditorRequired] public string AppId { get; set; } = string.Empty;
        [Parameter] public string? CompanyId { get; set; }
        [Parameter] public string? UserId { get; set; }
        [Parameter] public bool IsCompanyMode { get; set; }
        [Parameter] public UserTierSubscriptionModel? Subscription { get; set; }
        [Parameter] public EventCallback OnCancelRequested { get; set; }

        private UserTierSubscriptionModel? _subscription;
        private bool _showCancelConfirm;

        private bool UseCompanyScope => IsCompanyMode && !string.IsNullOrEmpty(CompanyId);
        private bool UseUserScope => !IsCompanyMode && !string.IsNullOrEmpty(UserId);

        protected override async Task OnComponentInitializedAsync()
        {
            if (Subscription != null)
            {
                _subscription = Subscription;
            }
            else
            {
                await LoadSubscriptionAsync();
            }
        }

        protected override async Task OnParametersSetAsync()
        {
            if (Subscription != null)
            {
                _subscription = Subscription;
            }
        }

        public async Task LoadSubscriptionAsync()
        {
            try
            {
                await SetLoadingAsync(true);

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
                await HandleErrorAsync(ex, "Loading subscription");
            }
            finally
            {
                await SetLoadingAsync(false);
            }
        }

        public void UpdateSubscription(UserTierSubscriptionModel? subscription)
        {
            _subscription = subscription;
            StateHasChanged();
        }

        private void ShowCancelConfirmation()
        {
            _showCancelConfirm = true;
            StateHasChanged();
        }

        private void HideCancelConfirmation()
        {
            _showCancelConfirm = false;
            StateHasChanged();
        }

        private async Task ConfirmCancel()
        {
            _showCancelConfirm = false;

            if (OnCancelRequested.HasDelegate)
            {
                await OnCancelRequested.InvokeAsync();
            }
        }

        /// <summary>
        /// End of access for a scheduled cancellation: the pending change date when the server
        /// set one, otherwise the subscription's end date.
        /// </summary>
        private DateTime? CancellationAccessEndDate => _subscription?.PendingChangeDate ?? _subscription?.EndDate;

        private bool IsPendingCancellation =>
            string.Equals(_subscription?.Status, "PendingCancellation", StringComparison.OrdinalIgnoreCase);

        private static string GetStatusBadgeClass(string status)
        {
            if (string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)) return "bg-success";
            if (string.Equals(status, "Trialing", StringComparison.OrdinalIgnoreCase)) return "bg-info";
            if (string.Equals(status, "PastDue", StringComparison.OrdinalIgnoreCase)) return "bg-warning text-dark";
            if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase)) return "bg-danger";
            if (string.Equals(status, "Expired", StringComparison.OrdinalIgnoreCase)) return "bg-secondary";
            if (string.Equals(status, "PendingUpgrade", StringComparison.OrdinalIgnoreCase)) return "bg-info";
            if (string.Equals(status, "PendingDowngrade", StringComparison.OrdinalIgnoreCase)) return "bg-info";
            if (string.Equals(status, "PendingCancellation", StringComparison.OrdinalIgnoreCase)) return "bg-warning text-dark";
            return "bg-secondary";
        }

        /// <summary>Friendly display label for raw subscription statuses.</summary>
        private static string GetStatusLabel(string status)
        {
            if (string.Equals(status, "PastDue", StringComparison.OrdinalIgnoreCase)) return "Past Due";
            if (string.Equals(status, "PendingUpgrade", StringComparison.OrdinalIgnoreCase)) return "Upgrade Scheduled";
            if (string.Equals(status, "PendingDowngrade", StringComparison.OrdinalIgnoreCase)) return "Downgrade Scheduled";
            if (string.Equals(status, "PendingCancellation", StringComparison.OrdinalIgnoreCase)) return "Cancellation Scheduled";
            return status;
        }
    }
}
