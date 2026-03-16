using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Models;

public class UsageDashboardViewModel
{
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-app-tiers";
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public bool ShowOverageInfo { get; set; } = true;
    public int WarningThreshold { get; set; } = 80;
    public UserTierSubscriptionModel? Subscription { get; set; }
    public List<AppTierLimitStatusModel> LimitStatuses { get; set; } = new();
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public static string GetBarClass(AppTierLimitStatusModel limit, int warningThreshold)
    {
        if (limit.IsExceeded) return "ww-bar-danger";
        if (limit.IsAtWarningThreshold || limit.UsagePercent >= warningThreshold) return "ww-bar-warning";
        return "ww-bar-normal";
    }

    public static string GetStatusTextClass(AppTierLimitStatusModel limit, int warningThreshold)
    {
        if (limit.IsExceeded) return "text-danger fw-medium";
        if (limit.IsAtWarningThreshold || limit.UsagePercent >= warningThreshold) return "text-warning";
        return "text-muted";
    }
}

public class OverageSummaryViewModel
{
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-app-tiers";
    public List<AppTierLimitStatusModel> OverageLimits { get; set; } = new();
    public UserTierSubscriptionModel? Subscription { get; set; }
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}
