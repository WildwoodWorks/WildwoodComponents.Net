namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Shared formatting helpers used across Razor ViewComponents and Blazor components.
/// Currency formatting, status badge CSS classes, invoice status badges.
/// </summary>
public static class FormatHelpers
{
    /// <summary>
    /// Relative "time ago" label for a timestamp, matching the JS/Swift notification formatting
    /// (&lt;60s "just now", &lt;60m "Nm ago", &lt;24h "Nh ago", else "Nd ago"). This is a cross-stack
    /// contract — keep the thresholds aligned with the JS/Swift timeAgo helpers.
    /// </summary>
    public static string RelativeTime(DateTime createdAt)
    {
        var created = createdAt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(createdAt, DateTimeKind.Utc)
            : createdAt.ToUniversalTime();

        var seconds = (DateTime.UtcNow - created).TotalSeconds;
        if (seconds < 0) seconds = 0;
        if (seconds < 60) return "just now";

        var minutes = seconds / 60;
        if (minutes < 60) return $"{(int)minutes}m ago";

        var hours = minutes / 60;
        if (hours < 24) return $"{(int)hours}h ago";

        var days = hours / 24;
        return $"{(int)days}d ago";
    }

    public static string GetCurrencySymbol(string currency)
    {
        return currency.ToUpperInvariant() switch
        {
            "USD" => "$",
            "EUR" => "\u20ac",
            "GBP" => "\u00a3",
            "JPY" => "\u00a5",
            "CAD" => "CA$",
            "AUD" => "A$",
            _ => currency + " "
        };
    }

    public static string FormatAmount(decimal amount, string currency)
    {
        var symbol = GetCurrencySymbol(currency);
        return $"{symbol}{amount:N2}";
    }

    public static string GetBadgeColorClass(string badgeColor)
    {
        if (string.IsNullOrEmpty(badgeColor)) return "bg-primary";
        return badgeColor.ToLowerInvariant() switch
        {
            "primary" => "bg-primary",
            "success" => "bg-success",
            "warning" => "bg-warning",
            "danger" => "bg-danger",
            "info" => "bg-info",
            _ => "bg-primary"
        };
    }

    /// <summary>
    /// True when a tier badge color is a raw CSS color ("#c9a227", "rgb(...)", "hsl(...)")
    /// rather than a semantic token ("success"). Raw colors can't be class names, so
    /// callers render them as an inline background-color with white text instead.
    /// </summary>
    public static bool IsRawCssColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return false;
        var trimmed = color.Trim();
        return trimmed.StartsWith("#", StringComparison.Ordinal)
            || trimmed.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("hsl", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a tier card should show its lifecycle status badge. "Active" is every publicly
    /// listed tier's lifecycle status — only non-default statuses (Beta, Deprecated, ...) are
    /// informative, so an Active badge is hidden.
    /// </summary>
    public static bool ShouldShowTierStatusBadge(string? badgeColor, string? status)
    {
        return !string.IsNullOrEmpty(badgeColor)
            && !string.IsNullOrEmpty(status)
            && !string.Equals(status.Trim(), "Active", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetStatusBadgeClass(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "active" => "bg-success",
            "paused" => "bg-warning text-dark",
            "cancelled" => "bg-danger",
            "pastdue" => "bg-warning text-dark",
            "trialing" => "bg-info",
            _ => "bg-secondary"
        };
    }

    public static string GetInvoiceStatusBadge(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "paid" => "bg-success",
            "pending" => "bg-warning text-dark",
            "overdue" => "bg-danger",
            "void" => "bg-secondary",
            _ => "bg-secondary"
        };
    }
}
