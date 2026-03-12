using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;

namespace WildwoodComponents.Blazor.Components.Usage
{
    public partial class OverageSummaryComponent : BaseWildwoodComponent
    {
        [Inject] private IAppTierComponentService AppTierService { get; set; } = default!;

        #region Parameters

        [Parameter, EditorRequired]
        public string AppId { get; set; } = string.Empty;

        /// <summary>
        /// Cost per unit of overage (default 0.003).
        /// </summary>
        [Parameter] public decimal OverageRate { get; set; } = 0.003m;

        [Parameter] public EventCallback OnViewDetails { get; set; }

        /// <summary>
        /// Optional externally-provided limit statuses (e.g. from a parent UsageDashboardComponent).
        /// If not provided, fetches its own data.
        /// </summary>
        [Parameter] public List<AppTierLimitStatusModel>? LimitStatuses { get; set; }

        #endregion

        #region State

        private List<OverageItem> _overageItems = new();
        private decimal _totalCost;
        private bool _initialized;

        #endregion

        #region Lifecycle

        protected override async Task OnComponentInitializedAsync()
        {
            await CalculateOverages();
        }

        protected override async Task OnParametersSetAsync()
        {
            // Only recalculate when externally-provided LimitStatuses change
            // (skip on first render since OnComponentInitializedAsync already ran)
            if (_initialized && LimitStatuses != null)
            {
                await CalculateOverages();
            }
        }

        protected override Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender) _initialized = true;
            return base.OnAfterRenderAsync(firstRender);
        }

        private async Task CalculateOverages()
        {
            var statuses = LimitStatuses;

            if (statuses == null)
            {
                try
                {
                    statuses = await AppTierService.GetAllLimitStatusesAsync(AppId);
                }
                catch (Exception ex)
                {
                    await HandleErrorAsync(ex, "Loading overage data");
                    return;
                }
            }

            _overageItems = new List<OverageItem>();
            _totalCost = 0;

            foreach (var s in statuses)
            {
                if (s.IsExceeded && !s.IsHardBlocked && !s.IsUnlimited)
                {
                    var overageCount = s.CurrentUsage - s.MaxValue;
                    var cost = overageCount * OverageRate;
                    _overageItems.Add(new OverageItem
                    {
                        LimitCode = s.LimitCode,
                        DisplayName = s.DisplayName,
                        OverageCount = overageCount,
                        Cost = cost,
                        Unit = s.Unit
                    });
                    _totalCost += cost;
                }
            }
        }

        #endregion

        #region Models

        private class OverageItem
        {
            public string LimitCode { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public long OverageCount { get; set; }
            public decimal Cost { get; set; }
            public string Unit { get; set; } = string.Empty;
        }

        #endregion
    }
}
