using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Components.Subscription.Admin
{
    /// <summary>
    /// The packs an account holds and the packs it can still buy.
    /// </summary>
    /// <remarks>
    /// A pack row says how it is paid for, because that decides what cancelling it does. A row with
    /// no payment behind it was GRANTED — a registration token's pack, or an admin's — so nothing
    /// bills it, there is no renewal to show and there is nothing to reactivate at a provider;
    /// cancelling one simply removes it. A billed row keeps access to the end of the period it is
    /// paid up to, and a cancellation scheduled that way can be taken back. Every one of those
    /// decisions lives in <see cref="AddOnRowRules"/>, shared with the Razor panel.
    /// </remarks>
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

        /// <summary>
        /// Fallback currency for a pack that carries none of its own. The pack's own
        /// <see cref="AppTierAddOnModel.Currency"/> always wins.
        /// </summary>
        [Parameter] public string Currency { get; set; } = "USD";

        /// <summary>
        /// Whether this surface offers cancelling at all. A read-only view passes false.
        /// </summary>
        [Parameter] public bool AllowCancel { get; set; } = true;

        /// <summary>
        /// Whether this surface offers taking a scheduled cancellation back. Only ever shown on a
        /// billed pack — a granted one has nothing at a provider to reactivate.
        /// </summary>
        [Parameter] public bool AllowReactivate { get; set; } = true;

        /// <summary>
        /// Copy the host may replace; the shipped words otherwise.
        /// </summary>
        [Parameter] public AddOnsPanelLabels Labels { get; set; } = new AddOnsPanelLabels();

        /// <summary>
        /// Opens the host's own pack picker. Given one, the panel offers "Add packs" — the seam the
        /// manage view uses when packs are bought through a checkout of its own.
        /// </summary>
        [Parameter] public EventCallback OnAddPacks { get; set; }

        /// <summary>Every row the server returned, cancelled ones included.</summary>
        private List<UserAddOnSubscriptionModel> _subscriptions = new();

        /// <summary>The rows that still grant what they pay for — the "Active Add-Ons" list.</summary>
        private List<UserAddOnSubscriptionModel> _ownedSubscriptions = new();

        /// <summary>Every pack the app sells, as the server returned it.</summary>
        private List<AppTierAddOnModel> _addOns = new();

        /// <summary>The packs still on offer: everything the account does not currently own.</summary>
        private List<AppTierAddOnModel> _availableAddOns = new();

        /// <summary>
        /// Why the last subscribe/cancel/reactivate failed. Rendered in the panel itself — a refusal
        /// used to reach the logger and nothing else, so a purchase that never happened looked as if
        /// it had. Cleared when the next attempt starts, so a retry never shows a stale message.
        /// </summary>
        private string? _actionError;

        /// <summary>The row whose cancellation is being confirmed. Nothing is cancelled on the first click.</summary>
        private string? _confirmingSubscriptionId;

        /// <summary>The add-on id or subscription id of the row with a call in flight.</summary>
        private string? _busyRowId;

        /// <summary>Which of the row actions is in flight, so the row says what it is doing.</summary>
        private AddOnAction _busyAction = AddOnAction.Subscribe;

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

                // Load add-on subscriptions
                if (UseCompanyScope)
                {
                    _subscriptions = await AppTierService.GetCompanyAddOnSubscriptionsAsync(AppId, CompanyId!);
                }
                else if (UseUserScope)
                {
                    _subscriptions = await AppTierService.GetUserAddOnsAsync(AppId, UserId!);
                }
                else
                {
                    _subscriptions = await AppTierService.GetMyAddOnsAsync(AppId);
                }

                // Load available add-ons
                if (IsAdmin)
                {
                    _addOns = await AppTierService.GetAllAddOnsAsync(AppId);
                }
                else
                {
                    _addOns = await AppTierService.GetAvailableAddOnsAsync(AppId);
                }

                RebuildRows();
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

        /// <summary>
        /// Splits what the server returned by the one access-granting rule: a Cancelled or Expired
        /// row grants nothing, so its pack goes back on offer and shows no stale badge; a row
        /// scheduled to cancel is still owned, so it is not sold twice.
        /// </summary>
        private void RebuildRows()
        {
            _ownedSubscriptions = AddOnRowRules.OwnedRows(_subscriptions);
            _availableAddOns = AddOnRowRules.AvailableRows(_addOns, _subscriptions);
            _confirmingSubscriptionId = null;
        }

        /// <summary>What one owned row decides about itself.</summary>
        private AddOnRowDecision DescribeRow(UserAddOnSubscriptionModel subscription)
        {
            return AddOnRowRules.Describe(subscription, AllowCancel, AllowReactivate, Labels);
        }

        private bool IsBusy(string rowId)
        {
            return _busyRowId is not null && string.Equals(_busyRowId, rowId, StringComparison.Ordinal);
        }

        private bool IsConfirming(string subscriptionId)
        {
            return string.Equals(_confirmingSubscriptionId, subscriptionId, StringComparison.Ordinal);
        }

        private void BeginCancelConfirmation(string subscriptionId)
        {
            _confirmingSubscriptionId = subscriptionId;
            StateHasChanged();
        }

        private void KeepPack()
        {
            _confirmingSubscriptionId = null;
            StateHasChanged();
        }

        private async Task SubscribeToAddOn(AppTierAddOnModel addOn)
        {
            if (_busyRowId is not null) return;

            BeginAction(addOn.Id, AddOnAction.Subscribe);

            try
            {
                string? failure;

                if (UseCompanyScope)
                {
                    var success = await AppTierService.SubscribeCompanyToAddOnAsync(AppId, CompanyId!, addOn.Id);
                    failure = success ? null : AddOnRowRules.FailureMessage(AddOnAction.Subscribe, addOn.Name, null);
                }
                else if (UseUserScope)
                {
                    var success = await AppTierService.SubscribeUserToAddOnAsync(AppId, UserId!, addOn.Id);
                    failure = success ? null : AddOnRowRules.FailureMessage(AddOnAction.Subscribe, addOn.Name, null);
                }
                else
                {
                    // The processor's trial and price both come from the option that is bought, so
                    // the id of that option travels with the request instead of being guessed
                    // server-side.
                    var pricing = AddOnRowRules.DefaultPricing(addOn);
                    var result = await AppTierService.SubscribeToAddOnDetailedAsync(AppId, addOn.Id, pricing?.Id, null);
                    failure = result.Success
                        ? null
                        : AddOnRowRules.FailureMessage(AddOnAction.Subscribe, addOn.Name, result.Error?.Message);
                }

                await FinishActionAsync(failure, "Subscribing to add-on");
            }
            catch (Exception ex)
            {
                await ReportFailureAsync(
                    AddOnRowRules.FailureMessage(AddOnAction.Subscribe, addOn.Name, ex.Message),
                    ex,
                    "Subscribing to add-on");
            }
            finally
            {
                EndAction();
            }
        }

        private async Task CancelAddOn(UserAddOnSubscriptionModel subscription)
        {
            if (_busyRowId is not null) return;

            _confirmingSubscriptionId = null;
            BeginAction(subscription.Id, AddOnAction.Cancel);

            try
            {
                string? failure;

                if (UseCompanyScope)
                {
                    var success = await AppTierService.CancelCompanyAddOnAsync(subscription.Id, false);
                    failure = success
                        ? null
                        : AddOnRowRules.FailureMessage(AddOnAction.Cancel, subscription.AddOnName, null);
                }
                else if (UseUserScope)
                {
                    var success = await AppTierService.CancelUserAddOnAsync(AppId, subscription.Id);
                    failure = success
                        ? null
                        : AddOnRowRules.FailureMessage(AddOnAction.Cancel, subscription.AddOnName, null);
                }
                else
                {
                    // immediate: false — the user keeps what the current period was paid for, which
                    // is what the confirmation just promised.
                    var result = await AppTierService.CancelAddOnDetailedAsync(subscription.Id, false);
                    failure = result.Success
                        ? null
                        : AddOnRowRules.FailureMessage(AddOnAction.Cancel, subscription.AddOnName, result.ErrorMessage);
                }

                await FinishActionAsync(failure, "Cancelling add-on");
            }
            catch (Exception ex)
            {
                await ReportFailureAsync(
                    AddOnRowRules.FailureMessage(AddOnAction.Cancel, subscription.AddOnName, ex.Message),
                    ex,
                    "Cancelling add-on");
            }
            finally
            {
                EndAction();
            }
        }

        private async Task ReactivateAddOn(UserAddOnSubscriptionModel subscription)
        {
            if (_busyRowId is not null) return;

            BeginAction(subscription.Id, AddOnAction.Reactivate);

            try
            {
                var result = await AppTierService.ReactivateAddOnAsync(subscription.Id);
                var failure = result.Success
                    ? null
                    : AddOnRowRules.FailureMessage(AddOnAction.Reactivate, subscription.AddOnName, result.ErrorMessage);

                await FinishActionAsync(failure, "Reactivating add-on");
            }
            catch (Exception ex)
            {
                await ReportFailureAsync(
                    AddOnRowRules.FailureMessage(AddOnAction.Reactivate, subscription.AddOnName, ex.Message),
                    ex,
                    "Reactivating add-on");
            }
            finally
            {
                EndAction();
            }
        }

        private void BeginAction(string rowId, AddOnAction action)
        {
            // A retry never shows the last attempt's message.
            _actionError = null;
            ClearError();
            _busyRowId = rowId;
            _busyAction = action;
            StateHasChanged();
        }

        private void EndAction()
        {
            _busyRowId = null;
            StateHasChanged();
        }

        private async Task FinishActionAsync(string? failure, string context)
        {
            if (failure is null)
            {
                await LoadDataAsync();
                if (OnSubscriptionChanged.HasDelegate)
                {
                    await OnSubscriptionChanged.InvokeAsync();
                }
                return;
            }

            await ReportFailureAsync(failure, new Exception(failure), context);
        }

        /// <summary>
        /// Shows the refusal in the panel AND keeps the existing host contract: the exception still
        /// reaches the logger and <c>OnError</c> through the base class.
        /// </summary>
        private async Task ReportFailureAsync(string message, Exception exception, string context)
        {
            _actionError = message;
            await HandleErrorAsync(exception, context);
        }

        private async Task RaiseAddPacksAsync()
        {
            if (OnAddPacks.HasDelegate)
            {
                await OnAddPacks.InvokeAsync();
            }
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

        /// <summary>The Bootstrap badge that goes with a row's status.</summary>
        private static string StatusBadgeClass(UserAddOnSubscriptionModel subscription, AddOnRowDecision decision)
        {
            if (subscription.IsBundled) return "bg-info";
            return decision.Cancelling ? "bg-warning text-dark" : "bg-success";
        }
    }
}
