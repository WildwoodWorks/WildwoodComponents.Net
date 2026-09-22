using WildwoodComponents.Razor.Helpers;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// <see cref="ViewHelpers"/> is public API a host application's own <c>.cshtml</c> can call, so the
/// two helpers that <see cref="ViewHelpers.FormatMoney"/> supersedes are deprecated rather than
/// deleted. These assert the part a consumer actually depends on: that the deprecated wrappers
/// still return exactly what they always returned. The deprecation is a compiler hint, not a
/// behaviour change — a host that has not migrated yet must keep rendering what it rendered before.
/// </summary>
public class ViewHelpersTests
{
    // The wrappers under test are [Obsolete] on purpose; calling them here is the point.
#pragma warning disable CS0618

    [Theory]
    [InlineData("USD", "$")]
    [InlineData("EUR", "€")]
    [InlineData("GBP", "£")]
    [InlineData("JPY", "¥")]
    [InlineData("CAD", "CA$")]
    [InlineData("AUD", "A$")]
    [InlineData("usd", "$")]     // the table is read upper-cased
    [InlineData("CHF", "CHF ")]  // outside the six-entry table: the code and a trailing space
    public void GetCurrencySymbol_StillReturnsTheSixEntryTablesAnswer(string currency, string expected)
    {
        Assert.Equal(expected, ViewHelpers.GetCurrencySymbol(currency));
        // And it is still the same answer as the Shared helper it has always delegated to.
        Assert.Equal(FormatHelpers.GetCurrencySymbol(currency), ViewHelpers.GetCurrencySymbol(currency));
    }

    [Theory]
    [InlineData(1234.56, "USD", "$1,234.56")]
    [InlineData(99.00, "GBP", "£99.00")]
    [InlineData(0.50, "EUR", "€0.50")]
    [InlineData(79.00, "JPY", "¥79.00")]  // two decimals, unlike FormatMoney — deliberately unchanged
    [InlineData(79.00, "CHF", "CHF 79.00")]
    public void FormatAmount_StillFormatsSymbolPlusTwoDecimals(decimal amount, string currency, string expected)
    {
        Assert.Equal(expected, ViewHelpers.FormatAmount(amount, currency));
        Assert.Equal(FormatHelpers.FormatAmount(amount, currency), ViewHelpers.FormatAmount(amount, currency));
    }

    /// <summary>
    /// The reason they are deprecated, stated as an assertion: the replacement disagrees with them
    /// wherever the old symbol table was wrong. If these ever start agreeing, the deprecation note
    /// in the README is no longer true.
    /// </summary>
    [Fact]
    public void FormatMoney_IsWhyTheOldHelpersAreDeprecated()
    {
        // Outside the six-entry table both say "CHF 79.00", but only FormatMoney separates symbol
        // from digits with CLDR's NO-BREAK space; the old helper concatenates an ordinary one.
        Assert.Equal("CHF 79.00", ViewHelpers.FormatMoney(79m, "CHF"));
        Assert.Equal("CHF 79.00", ViewHelpers.FormatAmount(79m, "CHF"));
        // A zero-decimal currency: the old helper always showed two.
        Assert.Equal("¥79", ViewHelpers.FormatMoney(79m, "JPY"));
        Assert.Equal("¥79.00", ViewHelpers.FormatAmount(79m, "JPY"));
        // A code outside the old table: FormatMoney groups it the way ICU does.
        Assert.Equal("SEK 1,234.56", ViewHelpers.FormatMoney(1234.56m, "SEK"));
    }

#pragma warning restore CS0618
}
