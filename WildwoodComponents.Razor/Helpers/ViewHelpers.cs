using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Helpers;

/// <summary>
/// View helper methods for Razor ViewComponent views.
/// Delegates to <see cref="FormatHelpers"/> in WildwoodComponents.Shared for core formatting logic.
/// </summary>
public static class ViewHelpers
{
    public static string GetCurrencySymbol(string currency) => FormatHelpers.GetCurrencySymbol(currency);

    public static string FormatAmount(decimal amount, string currency) => FormatHelpers.FormatAmount(amount, currency);

    public static string GetBadgeColorClass(string badgeColor) => FormatHelpers.GetBadgeColorClass(badgeColor);

    public static string GetStatusBadgeClass(string status) => FormatHelpers.GetStatusBadgeClass(status);

    public static string GetInvoiceStatusBadge(string status) => FormatHelpers.GetInvoiceStatusBadge(status);
}
