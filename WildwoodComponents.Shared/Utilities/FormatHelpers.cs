using System.Globalization;

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

    /// <summary>
    /// A six-entry symbol table. Superseded by <see cref="FormatMoney"/>, which carries the full
    /// CLDR table and the currency's own fraction digits; no component in the Blazor or Razor
    /// packages calls this any more (a source guard keeps it that way). Kept because it is public
    /// API a consumer may still call.
    /// </summary>
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

    /// <summary>
    /// Symbol + grouped amount, always two decimals. Superseded by <see cref="FormatMoney"/>, which
    /// is what the components render; kept because it is public API a consumer may still call.
    /// </summary>
    public static string FormatAmount(decimal amount, string currency)
    {
        var symbol = GetCurrencySymbol(currency);
        return $"{symbol}{amount:N2}";
    }

    /// <summary>
    /// Money for display, reproducing the JS SDK's
    /// <c>new Intl.NumberFormat('en-US', { style: 'currency', currency }).format(amount)</c>
    /// (<c>formatMoney</c>, packages/wildwood-core/src/features/catalog.ts) byte for byte: en-US
    /// grouping and decimal separators, the en-US symbol for the currency, and the currency's own
    /// number of fraction digits — so a whole $79 is "$79.00" and a whole ¥79 is "¥79".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The locale is FIXED at en-US, as it is in JS: a price rendered on the server and re-rendered
    /// in the browser has to come out identical, and the two hosts rarely agree on a default
    /// locale. Nothing here reads <see cref="CultureInfo.CurrentCulture"/>.
    /// </para>
    /// <para>
    /// The symbol and fraction-digit tables are hard-coded rather than read from
    /// <see cref="CultureInfo"/>, whose currency data differs between .NET (ICU) and .NET Framework
    /// (NLS) and would make the two target frameworks disagree. Both tables were taken from CLDR
    /// through <c>Intl.NumberFormat('en-US', …)</c> on Node 22.18 / ICU 77.1 — every three-letter
    /// code whose en-US symbol is not the code itself (23 of them) and every code whose minor units
    /// are not 2 (51 of them). A code outside the tables renders exactly as ICU renders an unknown
    /// one: the code, a no-break space and two decimals.
    /// </para>
    /// </remarks>
    /// <param name="amount">The amount, in major units.</param>
    /// <param name="currency">
    /// ISO 4217 code. Blank means USD, as it does in JS; a code that is not three letters cannot be
    /// rendered by <c>Intl</c> at all, so both stacks degrade to "CODE 79.00".
    /// </param>
    public static string FormatMoney(decimal amount, string? currency)
    {
        var trimmed = currency is null ? string.Empty : currency.Trim();
        var code = (trimmed.Length == 0 ? "USD" : trimmed).ToUpperInvariant();

        if (!IsWellFormedCurrencyCode(code))
        {
            // Intl throws on anything that is not three letters; JS says the amount and the code
            // rather than nothing. toFixed(2) does not group, so neither does this.
            return code + " " + amount.ToString("0.00", CultureInfo.InvariantCulture);
        }

        var symbol = GetIntlCurrencySymbol(code);
        var digits = GetCurrencyFractionDigits(code);
        var pattern = digits switch
        {
            0 => "#,##0",
            3 => "#,##0.000",
            4 => "#,##0.0000",
            _ => "#,##0.00"
        };

        var sign = amount < 0m ? "-" : string.Empty;
        var magnitude = Math.Abs(amount).ToString(pattern, CultureInfo.InvariantCulture);

        return sign + symbol + CurrencySpacing(symbol) + magnitude;
    }

    /// <summary>
    /// True when a billing frequency names an annual cycle. "Yearly", "Annual" and "Annually" are
    /// the same cycle to a customer, so all three count (JS <c>isAnnualFrequency</c>).
    /// </summary>
    public static bool IsAnnualFrequency(string? billingFrequency)
    {
        // Pattern form narrows on netstandard2.0, whose string.IsNullOrEmpty is unannotated.
        if (billingFrequency is not { Length: > 0 }) return false;

        return string.Equals(billingFrequency, "Yearly", StringComparison.OrdinalIgnoreCase)
            || string.Equals(billingFrequency, "Annual", StringComparison.OrdinalIgnoreCase)
            || string.Equals(billingFrequency, "Annually", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWellFormedCurrencyCode(string code)
    {
        if (code.Length != 3) return false;
        foreach (var c in code)
        {
            if (c < 'A' || c > 'Z') return false;
        }
        return true;
    }

    /// <summary>
    /// CLDR's currency spacing for "en": a no-break space separates the symbol from the digits
    /// unless the symbol ends in a currency-symbol character — "$79.00" and "CA$79.00", but
    /// "CHF 79.00" and "Cg. 79.00".
    /// </summary>
    private static string CurrencySpacing(string symbol)
    {
        if (symbol.Length == 0) return string.Empty;

        var last = symbol[symbol.Length - 1];
        return CharUnicodeInfo.GetUnicodeCategory(last) == UnicodeCategory.CurrencySymbol
            ? string.Empty
            : " ";
    }

    /// <summary>
    /// The en-US symbol CLDR gives a currency. Only the codes whose symbol is not the code itself
    /// are listed; everything else renders as its own code, which is what ICU does.
    /// </summary>
    private static string GetIntlCurrencySymbol(string code)
    {
        return code switch
        {
            "AUD" => "A$",
            "BRL" => "R$",
            "CAD" => "CA$",
            "CNY" => "CN¥",
            "EUR" => "€",
            "GBP" => "£",
            "HKD" => "HK$",
            "ILS" => "₪",
            "INR" => "₹",
            "JPY" => "¥",
            "KRW" => "₩",
            "MXN" => "MX$",
            "NZD" => "NZ$",
            "PHP" => "₱",
            "TWD" => "NT$",
            "USD" => "$",
            "VND" => "₫",
            "XAF" => "FCFA",
            "XCD" => "EC$",
            "XCG" => "Cg.",
            "XOF" => "F CFA",
            "XPF" => "CFPF",
            "XXX" => "¤",
            _ => code
        };
    }

    /// <summary>
    /// A currency's minor units. Only the codes that are not the default 2 are listed. Obsolete
    /// codes (ADP, ITL, ZWD, …) are included because ICU still carries them, so a stale code in an
    /// app's configuration renders the same in both stacks.
    /// </summary>
    private static int GetCurrencyFractionDigits(string code)
    {
        return code switch
        {
            "BHD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => 3,
            "CLF" or "UYW" => 4,
            // IQD belongs here, not with the other Gulf dinars: ISO 4217 gives it 3 minor units,
            // but CLDR overrides it to 0 and that is what Intl renders ("IQD 79", not "IQD 79.000").
            "ADP" or "AFN" or "ALL" or "BIF" or "BYR" or "CLP" or "DJF" or "ESP" or "GNF"
                or "IQD" or "IRR" or "ISK" or "ITL" or "JPY" or "KMF" or "KPW" or "KRW" or "LAK"
                or "LBP" or "LUF" or "MGA" or "MGF" or "MMK" or "MRO" or "PYG" or "RSD" or "RWF"
                or "SLL" or "SOS" or "STD" or "SYP" or "TMM" or "TRL" or "UGX" or "UYI" or "VND"
                or "VUV" or "XAF" or "XOF" or "XPF" or "YER" or "ZMK" or "ZWD" => 0,
            _ => 2
        };
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
        if (color is null) return false;
        var trimmed = color.Trim();
        if (trimmed.Length == 0) return false;
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
        // Pattern form narrows on netstandard2.0, whose string.IsNullOrEmpty is unannotated.
        return badgeColor is { Length: > 0 }
            && status is { Length: > 0 }
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
