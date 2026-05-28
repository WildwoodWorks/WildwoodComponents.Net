using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;

namespace WildwoodComponents.Blazor.Components.Usage
{
    public partial class UsageDashboardComponent : BaseWildwoodComponent
    {
        [Inject] private IAppTierComponentService AppTierService { get; set; } = default!;

        #region Parameters

        [Parameter, EditorRequired]
        public string AppId { get; set; } = string.Empty;

        [Parameter] public string? Title { get; set; }
        [Parameter] public string? Subtitle { get; set; }
        [Parameter] public bool ShowOverageInfo { get; set; } = true;

        /// <summary>
        /// Percentage threshold at which progress bars turn yellow (default 80).
        /// </summary>
        [Parameter] public int WarningThreshold { get; set; } = 80;

        /// <summary>
        /// Auto-refresh interval in seconds. 0 = no auto-refresh.
        /// </summary>
        [Parameter] public int RefreshIntervalSeconds { get; set; }

        [Parameter] public EventCallback OnUpgradeClick { get; set; }

        /// <summary>
        /// Optionally inject externally-managed limit statuses, bypassing the fetched/merged
        /// data for display. Mirrors the React <c>limitStatuses</c> override prop. The
        /// subscription is still loaded from the API.
        /// </summary>
        [Parameter] public List<AppTierLimitStatusModel>? LimitStatusesOverride { get; set; }

        /// <summary>
        /// Optional callback to merge or transform limit statuses after fetching from the API.
        /// Called during each load/refresh cycle. Mirrors the React <c>onMergeUsage</c> option.
        /// </summary>
        [Parameter]
        public Func<List<AppTierLimitStatusModel>, UserTierSubscriptionModel?, Task<List<AppTierLimitStatusModel>>>? OnMergeUsage { get; set; }

        #endregion

        #region State

        private List<AppTierLimitStatusModel> _limitStatuses = new();
        private List<AppTierLimitStatusModel> _fetchedLimitStatuses = new();
        private UserTierSubscriptionModel? _subscription;
        private bool _anyAtWarning;
        private bool _anyOverage;
        private Timer? _refreshTimer;

        #endregion

        #region Lifecycle

        protected override async Task OnComponentInitializedAsync()
        {
            await LoadUsageData();

            if (RefreshIntervalSeconds > 0)
            {
                _refreshTimer = new Timer(
                    async _ => await InvokeAsync(async () =>
                    {
                        await LoadUsageData();
                        StateHasChanged();
                    }),
                    null,
                    TimeSpan.FromSeconds(RefreshIntervalSeconds),
                    TimeSpan.FromSeconds(RefreshIntervalSeconds));
            }
        }

        private async Task LoadUsageData()
        {
            await SetLoadingAsync(true);
            try
            {
                var raw = await AppTierService.GetAllLimitStatusesAsync(AppId);
                _subscription = await AppTierService.GetMySubscriptionAsync(AppId);
                _fetchedLimitStatuses = (OnMergeUsage != null
                    ? await OnMergeUsage(raw, _subscription)
                    : raw) ?? new();
                ApplyEffectiveStatuses();
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "Loading usage data");
            }
            finally
            {
                await SetLoadingAsync(false);
            }
        }

        /// <summary>
        /// Applies the <see cref="LimitStatusesOverride"/> (if provided) over the fetched/merged
        /// data and recomputes warning flags. Re-run on parameter changes so a changed override
        /// reflects without a refetch (mirrors React applying the override at render).
        /// </summary>
        private void ApplyEffectiveStatuses()
        {
            _limitStatuses = LimitStatusesOverride ?? _fetchedLimitStatuses;
            UpdateWarningFlags();
        }

        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            ApplyEffectiveStatuses();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Manually refresh the usage data.
        /// </summary>
        public async Task RefreshData()
        {
            ClearError();
            await LoadUsageData();
        }

        /// <summary>
        /// Gets the current limit statuses for external consumers (e.g., OverageSummaryComponent).
        /// </summary>
        public List<AppTierLimitStatusModel> GetLimitStatuses() => _limitStatuses;

        /// <summary>
        /// Gets the raw limit statuses from the API before any override/merge transform.
        /// Mirrors the React hook's <c>rawLimitStatuses</c>.
        /// </summary>
        public List<AppTierLimitStatusModel> GetRawLimitStatuses() => _fetchedLimitStatuses;

        /// <summary>
        /// Gets the current subscription for external consumers.
        /// </summary>
        public UserTierSubscriptionModel? GetSubscription() => _subscription;

        #endregion

        #region Helpers

        private void UpdateWarningFlags()
        {
            _anyAtWarning = false;
            _anyOverage = false;

            foreach (var s in _limitStatuses)
            {
                if (s.UsagePercent >= WarningThreshold || s.IsExceeded)
                    _anyAtWarning = true;
                if (s.IsExceeded && !s.IsHardBlocked)
                    _anyOverage = true;
            }
        }

        private string GetBarClass(double usagePercent, bool isExceeded)
        {
            if (isExceeded) return "ww-usage-bar-exceeded";
            if (usagePercent >= WarningThreshold) return "ww-usage-bar-warning";
            return "ww-usage-bar-ok";
        }

        #endregion

        #region Dispose

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshTimer?.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
