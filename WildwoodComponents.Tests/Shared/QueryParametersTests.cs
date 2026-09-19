using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// <see cref="QueryParameters"/> is billed as a <c>URLSearchParams</c> equivalent, so it is held to
/// the WHATWG algorithms rather than to whatever the current call sites happen to need. Every
/// expectation below was captured from <c>new URLSearchParams(...)</c> on Node 22.18.
/// </summary>
public class QueryParametersTests
{
    #region Parse (WHATWG urlencoded parsing, via the JS asSearchParams shapes)

    [Theory]
    [InlineData("a=1")]
    [InlineData("?a=1")]
    [InlineData("https://example.test/x?a=1")]
    [InlineData("https://example.test/x?a=1#frag")]
    public void Parse_NormalisesEveryUrlShape(string value)
    {
        Assert.Equal("1", QueryParameters.Parse(value).Get("a"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_TreatsAnEmptyInputAsNoParameters(string? value)
    {
        var parameters = QueryParameters.Parse(value);

        Assert.Empty(parameters.Pairs);
        Assert.Null(parameters.Get("a"));
        Assert.Equal(string.Empty, parameters.ToString());
    }

    /// <summary>
    /// A bare name and a name with an empty value both decode to the empty string, and both
    /// serialise back with an <c>=</c> — <c>new URLSearchParams('a&amp;b=&amp;c=1').toString()</c> is
    /// <c>'a=&amp;b=&amp;c=1'</c>.
    /// </summary>
    [Fact]
    public void Parse_ReadsABareNameAsAnEmptyValue()
    {
        var parameters = QueryParameters.Parse("a&b=&c=1");

        Assert.Equal(string.Empty, parameters.Get("a"));
        Assert.Equal(string.Empty, parameters.Get("b"));
        Assert.Equal("1", parameters.Get("c"));
        Assert.Equal("a=&b=&c=1", parameters.ToString());
    }

    [Fact]
    public void Parse_SkipsEmptySequencesAndKeepsTheOriginalOrder()
    {
        var parameters = QueryParameters.Parse("&b=2&&a=1&");

        Assert.Equal(new[] { "b", "a" }, parameters.Pairs.Select(p => p.Key).ToArray());
        Assert.Equal("b=2&a=1", parameters.ToString());
    }

    /// <summary>Only the FIRST <c>=</c> separates the name from the value.</summary>
    [Fact]
    public void Parse_SplitsOnTheFirstEqualsOnly()
    {
        Assert.Equal("b=c", QueryParameters.Parse("a=b=c").Get("a"));
    }

    #endregion

    #region Get / GetAll (WHATWG: first value, then every value, in list order)

    [Fact]
    public void Get_ReturnsTheFirstValue_AndNullWhenTheNameIsAbsent()
    {
        var parameters = QueryParameters.Parse("a=1&b=2&a=3");

        Assert.Equal("1", parameters.Get("a"));
        Assert.Null(parameters.Get("zz"));
    }

    [Fact]
    public void Get_IsCaseSensitive_LikeUrlSearchParams()
    {
        var parameters = QueryParameters.Parse("A=1&a=2");

        Assert.Equal("1", parameters.Get("A"));
        Assert.Equal("2", parameters.Get("a"));
    }

    [Fact]
    public void GetAll_ReturnsEveryValueInOrder_AndAnEmptyListWhenTheNameIsAbsent()
    {
        var parameters = QueryParameters.Parse("addons=a&tier=t&addons=b");

        Assert.Equal(new[] { "a", "b" }, parameters.GetAll("addons"));
        Assert.Empty(parameters.GetAll("zz"));
    }

    #endregion

    #region Set (WHATWG: replace the first match in place, drop the rest, else append)

    /// <summary>
    /// The exact scenario the review raised: the surviving pair keeps the FIRST occurrence's
    /// position, not the last. <c>new URLSearchParams('a=1&amp;b=2&amp;a=3')</c> then
    /// <c>set('a','x')</c> serialises as <c>'a=x&amp;b=2'</c>.
    /// </summary>
    [Fact]
    public void Set_KeepsTheFirstOccurrencesPosition_AndDropsTheLaterDuplicates()
    {
        var parameters = QueryParameters.Parse("a=1&b=2&a=3");

        parameters.Set("a", "x");

        Assert.Equal("a=x&b=2", parameters.ToString());
        Assert.Equal(new[] { "x" }, parameters.GetAll("a"));
        Assert.Equal("x", parameters.Get("a"));
    }

    [Fact]
    public void Set_AppendsWhenTheNameIsAbsent()
    {
        var parameters = QueryParameters.Parse("a=1&b=2");

        parameters.Set("c", "3");

        Assert.Equal("a=1&b=2&c=3", parameters.ToString());
    }

    [Fact]
    public void Set_OnAnEmptyInstance_AppendsInCallOrder()
    {
        var parameters = new QueryParameters();

        parameters.Set("tier", "t1");
        parameters.Set("addons", "a1");

        Assert.Equal("tier=t1&addons=a1", parameters.ToString());
    }

    [Fact]
    public void Set_CollapsesThreeDuplicatesIntoTheFirstSlot()
    {
        var parameters = QueryParameters.Parse("a=1&b=2&a=3&c=4&a=5");

        parameters.Set("a", "x");

        Assert.Equal("a=x&b=2&c=4", parameters.ToString());
        Assert.Equal(new[] { "x" }, parameters.GetAll("a"));
        Assert.Equal(3, parameters.Pairs.Count);
    }

    [Fact]
    public void Set_LeavesTheOrderOfEveryOtherPairAlone()
    {
        var parameters = QueryParameters.Parse("z=9&a=1&y=8&a=3");

        parameters.Set("a", "x");

        Assert.Equal("z=9&a=x&y=8", parameters.ToString());
    }

    [Fact]
    public void Set_MatchesTheNameCaseSensitively()
    {
        var parameters = QueryParameters.Parse("A=1&a=2");

        parameters.Set("a", "x");

        Assert.Equal("A=1&a=x", parameters.ToString());
    }

    [Fact]
    public void Set_AppliedTwice_ReplacesTheSameSlotRatherThanGrowing()
    {
        var parameters = QueryParameters.Parse("a=1&b=2&a=3");

        parameters.Set("a", "x");
        parameters.Set("a", "y");

        Assert.Equal("a=y&b=2", parameters.ToString());
        Assert.Equal(2, parameters.Pairs.Count);
    }

    #endregion

    #region ToString (WHATWG urlencoded serialisation)

    /// <summary>
    /// <c>p.set('q', 'a b+c/d&amp;e=f'); p.toString()</c> is <c>'q=a+b%2Bc%2Fd%26e%3Df'</c>: a space
    /// becomes <c>+</c> and everything outside the urlencoded safe set is percent-escaped in
    /// upper-case hex.
    /// </summary>
    [Fact]
    public void ToString_EncodesLikeUrlSearchParams()
    {
        var parameters = new QueryParameters();
        parameters.Set("q", "a b+c/d&e=f");

        Assert.Equal("q=a+b%2Bc%2Fd%26e%3Df", parameters.ToString());
    }

    [Fact]
    public void ToString_KeepsTheUrlencodedSafeSetUnescaped()
    {
        var parameters = new QueryParameters();
        parameters.Set("k*-._9", "v*-._9");

        Assert.Equal("k*-._9=v*-._9", parameters.ToString());
    }

    [Fact]
    public void ToString_EncodesNonAsciiAsUtf8()
    {
        var parameters = new QueryParameters();
        parameters.Set("n", "Café ☕");

        Assert.Equal("n=Caf%C3%A9+%E2%98%95", parameters.ToString());
    }

    [Fact]
    public void Parse_DecodesPlusAsASpaceAndPercentSequencesAsUtf8()
    {
        Assert.Equal("a b+c", QueryParameters.Parse("q=a+b%2Bc").Get("q"));
        Assert.Equal("Café ☕", QueryParameters.Parse("n=Caf%C3%A9+%E2%98%95").Get("n"));
    }

    [Fact]
    public void ParseThenToString_RoundTripsAValueThroughEveryEscapeRule()
    {
        const string value = "a b+c/d&e=f Café ☕";
        var written = new QueryParameters();
        written.Set("q", value);

        Assert.Equal(value, QueryParameters.Parse(written.ToString()).Get("q"));
    }

    #endregion
}
