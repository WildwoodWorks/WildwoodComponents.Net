using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Helpers;

/// <summary>
/// View helper methods for Razor ViewComponent views.
/// Delegates to <see cref="FormatHelpers"/> in WildwoodComponents.Shared for core formatting logic.
/// </summary>
public static class ViewHelpers
{
    /// <summary>
    /// Money for display — the one formatter every view uses, reproducing the JS SDK's
    /// <c>formatMoney</c> byte for byte.
    /// </summary>
    /// <remarks>
    /// This deprecates <c>GetCurrencySymbol</c> + a <c>ToString("N2")</c> assembled in the markup
    /// and the older <c>FormatAmount</c> — both still here, both still doing what they always did.
    /// Both read a six-entry symbol table, so every currency outside it (CHF, SEK, PLN, ...)
    /// rendered as "CHF 79.00" on one surface and "$79.00" on another, and a zero-decimal currency
    /// such as JPY always showed two.
    /// </remarks>
    public static string FormatMoney(decimal amount, string? currency) => FormatHelpers.FormatMoney(amount, currency);

    /// <summary>
    /// A six-entry symbol table. Superseded by <see cref="FormatMoney"/>; no view in this package
    /// calls it any more (a source guard keeps it that way). Kept, and kept behaving exactly as it
    /// always did, because a host application's own <c>.cshtml</c> may still call it — deleting it
    /// would break that host at compile time for no benefit.
    /// </summary>
    [Obsolete("Use ViewHelpers.FormatMoney(amount, currency), which renders every currency the way the JS SDK does.")]
    public static string GetCurrencySymbol(string currency) => FormatHelpers.GetCurrencySymbol(currency);

    /// <summary>
    /// Symbol + grouped amount, always two decimals. Superseded by <see cref="FormatMoney"/>, which
    /// is what this package's views render; kept unchanged for host applications that call it.
    /// </summary>
    [Obsolete("Use ViewHelpers.FormatMoney(amount, currency), which renders every currency the way the JS SDK does.")]
    public static string FormatAmount(decimal amount, string currency) => FormatHelpers.FormatAmount(amount, currency);

    public static string GetBadgeColorClass(string badgeColor) => FormatHelpers.GetBadgeColorClass(badgeColor);

    public static bool IsRawCssColor(string? color) => FormatHelpers.IsRawCssColor(color);

    public static string GetStatusBadgeClass(string status) => FormatHelpers.GetStatusBadgeClass(status);

    public static string GetInvoiceStatusBadge(string status) => FormatHelpers.GetInvoiceStatusBadge(status);
}
