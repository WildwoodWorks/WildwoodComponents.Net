using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

public class FormatHelpersTests
{
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
}
