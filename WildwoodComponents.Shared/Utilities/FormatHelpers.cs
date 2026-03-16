namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Shared formatting helpers used across Razor ViewComponents and Blazor components.
/// Currency formatting, status badge CSS classes, invoice status badges.
/// </summary>
public static class FormatHelpers
{
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
