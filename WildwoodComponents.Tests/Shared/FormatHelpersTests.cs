using System.Globalization;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

public class FormatHelpersTests
{
    #region FormatMoney (ported from the formatMoney block of catalog.test.ts)

    [Fact]
    public void FormatMoney_UsesTheCurrencyStandardFractionDigits_NeverAHardCodedSymbol()
    {
        Assert.Equal("$79.00", FormatHelpers.FormatMoney(79m, "USD"));
        Assert.Equal("$79.50", FormatHelpers.FormatMoney(79.5m, "USD"));
        Assert.Equal("€790.00", FormatHelpers.FormatMoney(790m, "EUR"));
        Assert.Equal("£12.34", FormatHelpers.FormatMoney(12.34m, "GBP"));
        // A zero-decimal currency keeps zero decimals.
        Assert.Equal("¥79", FormatHelpers.FormatMoney(79m, "JPY"));
    }

    [Fact]
    public void FormatMoney_HonoursALowerCasedCode_AndDefaultsAMissingCurrencyToUsd()
    {
        Assert.Equal("€79.00", FormatHelpers.FormatMoney(79m, "eur"));
        Assert.Equal("$79.00", FormatHelpers.FormatMoney(79m, ""));
        Assert.Equal("$79.00", FormatHelpers.FormatMoney(79m, "  "));
        Assert.Equal("$79.00", FormatHelpers.FormatMoney(79m, null));
    }

    [Fact]
    public void FormatMoney_DegradesToTheCodeAndTheAmountForAnUnknownCurrency()
    {
        Assert.Equal("ZZZZ 79.00", FormatHelpers.FormatMoney(79m, "ZZZZ"));
        // Intl throws on anything that is not three letters, and the fallback does not group.
        Assert.Equal("ZZZZ 1234.50", FormatHelpers.FormatMoney(1234.5m, "ZZZZ"));
    }

    /// <summary>
    /// JS fixes the locale at en-US so a server render and a browser render agree. The .NET port
    /// takes no locale at all, so the guarantee is stated the only way it can be: the thread's
    /// culture never reaches the output.
    /// </summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ja-JP")]
    [InlineData("")]
    public void FormatMoney_IsIndependentOfTheThreadCulture(string cultureName)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = cultureName.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            Assert.Equal("$1,234.50", FormatHelpers.FormatMoney(1234.5m, "USD"));
            Assert.Equal("€1,234.50", FormatHelpers.FormatMoney(1234.5m, "EUR"));
            Assert.Equal("¥1,235", FormatHelpers.FormatMoney(1234.5m, "JPY"));
            Assert.Equal("CHF 1,234.50", FormatHelpers.FormatMoney(1234.5m, "CHF"));
            Assert.Equal("ZZZZ 1234.50", FormatHelpers.FormatMoney(1234.5m, "ZZZZ"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    /// <summary>
    /// The seven currencies the old symbol tables carried must render exactly as the JS SDK renders
    /// them. Expectations captured from
    /// <c>Intl.NumberFormat('en-US', { style: 'currency', currency }).format(n)</c> on Node 22.18 /
    /// ICU 77.1 — the same source the .NET table was built from.
    /// </summary>
    [Theory]
    [InlineData("USD", "$79.00", "$1,234.50", "-$79.00")]
    [InlineData("EUR", "€79.00", "€1,234.50", "-€79.00")]
    [InlineData("GBP", "£79.00", "£1,234.50", "-£79.00")]
    [InlineData("JPY", "¥79", "¥1,235", "-¥79")]
    [InlineData("INR", "₹79.00", "₹1,234.50", "-₹79.00")]
    [InlineData("CAD", "CA$79.00", "CA$1,234.50", "-CA$79.00")]
    [InlineData("AUD", "A$79.00", "A$1,234.50", "-A$79.00")]
    public void FormatMoney_RendersTheLegacyCurrenciesExactlyAsTheJsSdkDoes(
        string currency,
        string expectedWhole,
        string expectedGrouped,
        string expectedNegative)
    {
        Assert.Equal(expectedWhole, FormatHelpers.FormatMoney(79m, currency));
        Assert.Equal(expectedGrouped, FormatHelpers.FormatMoney(1234.5m, currency));
        Assert.Equal(expectedNegative, FormatHelpers.FormatMoney(-79m, currency));
    }

    /// <summary>
    /// A currency outside the old seven-entry tables renders in its own currency instead of
    /// falling back to a dollar sign — the whole reason formatMoney replaced formatPrice.
    /// CLDR puts a no-break space after an alphabetic symbol.
    /// </summary>
    [Theory]
    [InlineData("CHF", "CHF 79.00")]
    [InlineData("SEK", "SEK 79.00")]
    [InlineData("NOK", "NOK 79.00")]
    [InlineData("KRW", "₩79")]
    [InlineData("BRL", "R$79.00")]
    [InlineData("HKD", "HK$79.00")]
    [InlineData("NZD", "NZ$79.00")]
    [InlineData("KWD", "KWD 79.000")]
    [InlineData("XYZ", "XYZ 79.00")]
    public void FormatMoney_RendersCurrenciesTheOldSymbolTableMissed(string currency, string expected)
    {
        Assert.Equal(expected, FormatHelpers.FormatMoney(79m, currency));
    }

    /// <summary>
    /// The COMPLETE ICU fraction-digit table, pinned: every three-letter code whose
    /// <c>maximumFractionDigits</c> is not the default 2 under
    /// <c>Intl.NumberFormat('en-US', { style: 'currency', currency })</c> on Node 22.18 / ICU 77.1
    /// (51 of them), with the string that call renders for 79. Captured by walking the whole
    /// AAA-ZZZ space through ICU. The data is literal here on purpose: the test pins the table the
    /// implementation hard-codes, instead of re-deriving it from whatever ICU the build agent has.
    /// </summary>
    /// <remarks>
    /// IQD is the entry that makes this table worth pinning. ISO 4217 gives the Iraqi dinar three
    /// minor units, so it reads like a Gulf dinar, but CLDR overrides it to zero and Intl renders
    /// "IQD 79" - the port carried the ISO value until this test went in.
    /// </remarks>
    private static readonly (string Code, int Digits, string At79)[] FractionDigitTable =
    {
        ("ADP", 0, "ADP\u00a079"),
        ("AFN", 0, "AFN\u00a079"),
        ("ALL", 0, "ALL\u00a079"),
        ("BHD", 3, "BHD\u00a079.000"),
        ("BIF", 0, "BIF\u00a079"),
        ("BYR", 0, "BYR\u00a079"),
        ("CLF", 4, "CLF\u00a079.0000"),
        ("CLP", 0, "CLP\u00a079"),
        ("DJF", 0, "DJF\u00a079"),
        ("ESP", 0, "ESP\u00a079"),
        ("GNF", 0, "GNF\u00a079"),
        ("IQD", 0, "IQD\u00a079"),
        ("IRR", 0, "IRR\u00a079"),
        ("ISK", 0, "ISK\u00a079"),
        ("ITL", 0, "ITL\u00a079"),
        ("JOD", 3, "JOD\u00a079.000"),
        ("JPY", 0, "¥79"),
        ("KMF", 0, "KMF\u00a079"),
        ("KPW", 0, "KPW\u00a079"),
        ("KRW", 0, "₩79"),
        ("KWD", 3, "KWD\u00a079.000"),
        ("LAK", 0, "LAK\u00a079"),
        ("LBP", 0, "LBP\u00a079"),
        ("LUF", 0, "LUF\u00a079"),
        ("LYD", 3, "LYD\u00a079.000"),
        ("MGA", 0, "MGA\u00a079"),
        ("MGF", 0, "MGF\u00a079"),
        ("MMK", 0, "MMK\u00a079"),
        ("MRO", 0, "MRO\u00a079"),
        ("OMR", 3, "OMR\u00a079.000"),
        ("PYG", 0, "PYG\u00a079"),
        ("RSD", 0, "RSD\u00a079"),
        ("RWF", 0, "RWF\u00a079"),
        ("SLL", 0, "SLL\u00a079"),
        ("SOS", 0, "SOS\u00a079"),
        ("STD", 0, "STD\u00a079"),
        ("SYP", 0, "SYP\u00a079"),
        ("TMM", 0, "TMM\u00a079"),
        ("TND", 3, "TND\u00a079.000"),
        ("TRL", 0, "TRL\u00a079"),
        ("UGX", 0, "UGX\u00a079"),
        ("UYI", 0, "UYI\u00a079"),
        ("UYW", 4, "UYW\u00a079.0000"),
        ("VND", 0, "₫79"),
        ("VUV", 0, "VUV\u00a079"),
        ("XAF", 0, "FCFA\u00a079"),
        ("XOF", 0, "F\u202fCFA\u00a079"),
        ("XPF", 0, "CFPF\u00a079"),
        ("YER", 0, "YER\u00a079"),
        ("ZMK", 0, "ZMK\u00a079"),
        ("ZWD", 0, "ZWD\u00a079"),
    };

    /// <summary>
    /// The COMPLETE ICU symbol table, pinned the same way: every three-letter code whose en-US
    /// symbol is not the code itself (23 of them), the symbol, and the rendering of 79.
    /// </summary>
    private static readonly (string Code, string Symbol, string At79)[] SymbolTable =
    {
        ("AUD", "A$", "A$79.00"),
        ("BRL", "R$", "R$79.00"),
        ("CAD", "CA$", "CA$79.00"),
        ("CNY", "CN¥", "CN¥79.00"),
        ("EUR", "€", "€79.00"),
        ("GBP", "£", "£79.00"),
        ("HKD", "HK$", "HK$79.00"),
        ("ILS", "₪", "₪79.00"),
        ("INR", "₹", "₹79.00"),
        ("JPY", "¥", "¥79"),
        ("KRW", "₩", "₩79"),
        ("MXN", "MX$", "MX$79.00"),
        ("NZD", "NZ$", "NZ$79.00"),
        ("PHP", "₱", "₱79.00"),
        ("TWD", "NT$", "NT$79.00"),
        ("USD", "$", "$79.00"),
        ("VND", "₫", "₫79"),
        ("XAF", "FCFA", "FCFA\u00a079"),
        ("XCD", "EC$", "EC$79.00"),
        ("XCG", "Cg.", "Cg.\u00a079.00"),
        ("XOF", "F\u202fCFA", "F\u202fCFA\u00a079"),
        ("XPF", "CFPF", "CFPF\u00a079"),
        ("XXX", "¤", "¤79.00"),
    };

    public static TheoryData<string, int, string> FractionDigitRows
    {
        get
        {
            var data = new TheoryData<string, int, string>();
            foreach (var row in FractionDigitTable) data.Add(row.Code, row.Digits, row.At79);
            return data;
        }
    }

    public static TheoryData<string, string, string> SymbolRows
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var row in SymbolTable) data.Add(row.Code, row.Symbol, row.At79);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FractionDigitRows))]
    public void FormatMoney_MatchesIcuForEveryCurrencyWhoseMinorUnitsAreNotTwo(
        string currency,
        int expectedDigits,
        string expectedAt79)
    {
        var formatted = FormatHelpers.FormatMoney(79m, currency);

        Assert.Equal(expectedAt79, formatted);
        Assert.Equal(expectedDigits, TrailingFractionDigits(formatted));
    }

    [Theory]
    [MemberData(nameof(SymbolRows))]
    public void FormatMoney_MatchesIcuForEveryCurrencyWhoseSymbolIsNotItsCode(
        string currency,
        string expectedSymbol,
        string expectedAt79)
    {
        var formatted = FormatHelpers.FormatMoney(79m, currency);

        Assert.Equal(expectedAt79, formatted);
        Assert.StartsWith(expectedSymbol, formatted, StringComparison.Ordinal);
    }

    /// <summary>
    /// The regression the pinned table exists for: the Iraqi dinar renders with no decimals.
    /// </summary>
    [Fact]
    public void FormatMoney_GivesTheIraqiDinarZeroFractionDigits_NotThreeLikeTheOtherDinars()
    {
        Assert.Equal("IQD\u00a079", FormatHelpers.FormatMoney(79m, "IQD"));
        Assert.Equal("IQD\u00a01,235", FormatHelpers.FormatMoney(1234.5m, "IQD"));
        // Its neighbours in the ISO three-minor-unit bucket are unaffected.
        Assert.Equal("BHD\u00a079.000", FormatHelpers.FormatMoney(79m, "BHD"));
        Assert.Equal("KWD\u00a079.000", FormatHelpers.FormatMoney(79m, "KWD"));
    }

    /// <summary>
    /// Completeness, the half a row-by-row theory cannot state: no code OUTSIDE the two pinned
    /// tables may render with anything but two decimals and its own code as the symbol. Walks all
    /// 17,576 three-letter codes, so an entry silently added to - or dropped from - either table
    /// fails here.
    /// </summary>
    [Fact]
    public void FormatMoney_TreatsEveryCodeOutsideThePinnedTablesAsTwoDigitsAndItsOwnSymbol()
    {
        var pinnedDigits = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in FractionDigitTable) pinnedDigits.Add(row.Code);
        var pinnedSymbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in SymbolTable) pinnedSymbols.Add(row.Code);

        var unexpectedDigits = new List<string>();
        var unexpectedSymbols = new List<string>();

        for (var a = 'A'; a <= 'Z'; a++)
        for (var b = 'A'; b <= 'Z'; b++)
        for (var c = 'A'; c <= 'Z'; c++)
        {
            var code = new string(new[] { a, b, c });
            var formatted = FormatHelpers.FormatMoney(79m, code);

            if (!pinnedDigits.Contains(code) && TrailingFractionDigits(formatted) != 2)
            {
                unexpectedDigits.Add(code + " -> " + formatted);
            }

            if (!pinnedSymbols.Contains(code)
                && !formatted.StartsWith(code + "\u00a0", StringComparison.Ordinal))
            {
                unexpectedSymbols.Add(code + " -> " + formatted);
            }
        }

        Assert.Empty(unexpectedDigits);
        Assert.Empty(unexpectedSymbols);
        Assert.Equal(51, pinnedDigits.Count);
        Assert.Equal(23, pinnedSymbols.Count);
    }

    /// <summary>Decimals in the rendered amount: the run of digits after its last decimal point.</summary>
    private static int TrailingFractionDigits(string formatted)
    {
        var dot = formatted.LastIndexOf('.');
        if (dot < 0 || dot == formatted.Length - 1) return 0;

        for (var i = dot + 1; i < formatted.Length; i++)
        {
            if (formatted[i] < '0' || formatted[i] > '9') return 0;
        }
        return formatted.Length - dot - 1;
    }

    #endregion

    #region IsAnnualFrequency (tierUtils.ts)

    [Theory]
    [InlineData("Yearly", true)]
    [InlineData("yearly", true)]
    [InlineData("Annual", true)]
    [InlineData("ANNUALLY", true)]
    [InlineData("Monthly", false)]
    [InlineData("Quarterly", false)]
    [InlineData(" Yearly ", false)] // JS does not trim here; neither does the port
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAnnualFrequency_CountsTheThreeAnnualSynonyms(string? frequency, bool expected)
    {
        Assert.Equal(expected, FormatHelpers.IsAnnualFrequency(frequency));
    }

    #endregion

    [Theory]
    [InlineData("USD", "$")]
    [InlineData("EUR", "\u20ac")]
    [InlineData("GBP", "\u00a3")]
    [InlineData("JPY", "\u00a5")]
    [InlineData("CAD", "CA$")]
    [InlineData("AUD", "A$")]
    public void GetCurrencySymbol_KnownCurrency_ReturnsSymbol(string currency, string expected)
    {
        Assert.Equal(expected, FormatHelpers.GetCurrencySymbol(currency));
    }

    [Fact]
    public void GetCurrencySymbol_UnknownCurrency_ReturnsCurrencyWithSpace()
    {
        Assert.Equal("CHF ", FormatHelpers.GetCurrencySymbol("CHF"));
    }

    [Theory]
    [InlineData(1234.56, "USD", "$1,234.56")]
    [InlineData(99.00, "GBP", "\u00a399.00")]
    [InlineData(0.50, "EUR", "\u20ac0.50")]
    public void FormatAmount_VariousCurrencies_FormatsCorrectly(decimal amount, string currency, string expected)
    {
        Assert.Equal(expected, FormatHelpers.FormatAmount(amount, currency));
    }

    [Theory]
    [InlineData("primary", "bg-primary")]
    [InlineData("success", "bg-success")]
    [InlineData("warning", "bg-warning")]
    [InlineData("danger", "bg-danger")]
    [InlineData("info", "bg-info")]
    [InlineData("unknown", "bg-primary")]
    public void GetBadgeColorClass_KnownColors_ReturnsCorrectClass(string color, string expected)
    {
        Assert.Equal(expected, FormatHelpers.GetBadgeColorClass(color));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetBadgeColorClass_NullOrEmpty_ReturnsPrimary(string? color)
    {
        Assert.Equal("bg-primary", FormatHelpers.GetBadgeColorClass(color!));
    }

    [Theory]
    [InlineData("active", "bg-success")]
    [InlineData("paused", "bg-warning text-dark")]
    [InlineData("cancelled", "bg-danger")]
    [InlineData("pastdue", "bg-warning text-dark")]
    [InlineData("trialing", "bg-info")]
    [InlineData("other", "bg-secondary")]
    public void GetStatusBadgeClass_KnownStatuses_ReturnsCorrectClass(string status, string expected)
    {
        Assert.Equal(expected, FormatHelpers.GetStatusBadgeClass(status));
    }

    [Theory]
    [InlineData("#c9a227", true)]
    [InlineData("rgb(201, 162, 39)", true)]
    [InlineData("RGBA(0,0,0,0.5)", true)]
    [InlineData("hsl(45, 68%, 47%)", true)]
    [InlineData(" #c9a227 ", true)]
    [InlineData("success", false)]
    [InlineData("primary", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsRawCssColor_DetectsRawColors(string? color, bool expected)
    {
        Assert.Equal(expected, FormatHelpers.IsRawCssColor(color));
    }

    [Theory]
    [InlineData("success", "Beta", true)]
    [InlineData("#c9a227", "Deprecated", true)]
    [InlineData("success", "Active", false)]   // Active is the default lifecycle status — not informative
    [InlineData("success", "active", false)]   // case-insensitive
    [InlineData("success", " Active ", false)] // whitespace-tolerant
    [InlineData("", "Beta", false)]            // no color -> no badge
    [InlineData("success", "", false)]         // no status -> no badge
    public void ShouldShowTierStatusBadge_HidesActiveAndEmpty(string badgeColor, string status, bool expected)
    {
        Assert.Equal(expected, FormatHelpers.ShouldShowTierStatusBadge(badgeColor, status));
    }
}
