using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Components.Subscription.Admin
{
    public partial class UsageLimitsPanel : BaseWildwoodComponent
    {
        [Inject] private IAppTierComponentService AppTierService { get; set; } = default!;

        [Parameter, EditorRequired] public string AppId { get; set; } = string.Empty;
        [Parameter] public string? CompanyId { get; set; }
        [Parameter] public string? UserId { get; set; }
        [Parameter] public bool IsCompanyMode { get; set; }
        [Parameter] public bool IsAdmin { get; set; }
        [Parameter] public int WarningThreshold { get; set; } = 80;
        [Parameter] public EventCallback OnLimitChanged { get; set; }

        private List<AppTierLimitStatusModel> _limits = new();
        private bool _isProcessing;
        private string? _editingLimitCode;
        private int _editMaxValue;

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

                if (UseCompanyScope)
                {
                    _limits = await AppTierService.GetCompanyLimitStatusesAsync(AppId, CompanyId!);
                }
                else if (UseUserScope)
                {
                    _limits = await AppTierService.GetUserLimitStatusesAsync(AppId, UserId);
                }
                else
                {
                    _limits = await AppTierService.GetAllLimitStatusesAsync(AppId);
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Loading usage limits");
            }
            finally
            {
                await SetLoadingAsync(false);
            }
        }

        #region Admin Limit Editing

        private void StartEditLimit(AppTierLimitStatusModel limit)
        {
            _editingLimitCode = limit.LimitCode;
            _editMaxValue = (int)limit.MaxValue;
            StateHasChanged();
        }

        private void CancelEditLimit()
        {
            _editingLimitCode = null;
            StateHasChanged();
        }

        private async Task SaveLimitMaxValue(string limitCode)
        {
            if (_isProcessing) return;
            _isProcessing = true;
            StateHasChanged();

            try
            {
                bool success;
                if (UseCompanyScope)
                {
                    success = await AppTierService.UpdateCompanyUsageLimitAsync(AppId, CompanyId!, limitCode, _editMaxValue);
                }
                else if (UseUserScope)
                {
                    success = await AppTierService.UpdateUserUsageLimitAsync(AppId, UserId, limitCode, _editMaxValue);
                }
                else
                {
                    success = await AppTierService.UpdateUsageLimitAsync(AppId, limitCode, _editMaxValue);
                }

                if (success)
                {
                    _editingLimitCode = null;
                    await LoadDataAsync();
                    if (OnLimitChanged.HasDelegate)
                    {
                        await OnLimitChanged.InvokeAsync();
                    }
                }
                else
                {
                    await HandleErrorAsync(new Exception("Failed to update limit"), "Updating usage limit");
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Updating usage limit");
            }
            finally
            {
                _isProcessing = false;
                StateHasChanged();
            }
        }

        private async Task ResetUsage(string limitCode)
        {
            if (_isProcessing) return;
            _isProcessing = true;
            StateHasChanged();

            try
            {
                bool success;
                if (UseCompanyScope)
                {
                    success = await AppTierService.ResetCompanyUsageAsync(AppId, CompanyId!, limitCode);
                }
                else if (UseUserScope)
                {
                    success = await AppTierService.ResetUserUsageAsync(AppId, UserId, limitCode);
                }
                else
                {
                    success = await AppTierService.ResetUsageAsync(AppId, limitCode);
                }

                if (success)
                {
                    await LoadDataAsync();
                    if (OnLimitChanged.HasDelegate)
                    {
                        await OnLimitChanged.InvokeAsync();
                    }
                }
                else
                {
                    await HandleErrorAsync(new Exception("Failed to reset usage"), "Resetting usage");
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Resetting usage");
            }
            finally
            {
                _isProcessing = false;
                StateHasChanged();
            }
        }

        private bool IsEditingLimit(string limitCode)
        {
            return _editingLimitCode == limitCode;
        }

        #endregion

        private static string GetBadgeClass(AppTierLimitStatusModel limit)
        {
            if (limit.IsUnlimited) return "bg-success";
            if (limit.IsHardBlocked) return "bg-danger";
            if (limit.IsExceeded) return "bg-danger";
            if (limit.IsAtWarningThreshold) return "bg-warning text-dark";
            return "bg-success";
        }

        private static string GetBadgeText(AppTierLimitStatusModel limit)
        {
            if (limit.IsUnlimited) return "Unlimited";
            if (limit.IsHardBlocked) return "Blocked";
            if (limit.IsExceeded) return "Exceeded";
            if (limit.IsAtWarningThreshold) return "Warning";
            return "OK";
        }

        private static int GetBarWidth(AppTierLimitStatusModel limit)
        {
            if (limit.IsUnlimited) return 100;
            var percent = (int)Math.Round(limit.UsagePercent);
            if (percent > 100) return 100;
            if (percent < 0) return 0;
            return percent;
        }
    }
}
