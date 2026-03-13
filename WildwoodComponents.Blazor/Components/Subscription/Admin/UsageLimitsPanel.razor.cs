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
        [Parameter] public int WarningThreshold { get; set; } = 80;

        private List<AppTierLimitStatusModel> _limits = new();

        protected override async Task OnComponentInitializedAsync()
        {
            await LoadDataAsync();
        }

        public async Task LoadDataAsync()
        {
            try
            {
                await SetLoadingAsync(true);

                if (!string.IsNullOrEmpty(CompanyId))
                {
                    _limits = await AppTierService.GetCompanyLimitStatusesAsync(AppId, CompanyId);
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
