using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

public class UrlHelpersTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void StripApiSuffix_ReturnsEmpty_ForNullOrEmpty(string? input)
    {
        Assert.Equal(string.Empty, UrlHelpers.StripApiSuffix(input));
    }

    [Theory]
    [InlineData("https://api.test/api", "https://api.test")]
    [InlineData("https://api.test/api/", "https://api.test")]
    [InlineData("https://api.test/API", "https://api.test")]       // case-insensitive
    [InlineData("https://api.test/api///", "https://api.test")]    // collapses trailing slashes
    public void StripApiSuffix_RemovesTrailingApiSegment(string input, string expected)
    {
        Assert.Equal(expected, UrlHelpers.StripApiSuffix(input));
    }

    [Theory]
    [InlineData("https://api.test", "https://api.test")]
    [InlineData("https://api.test/", "https://api.test")]
    [InlineData("https://api.test/v1", "https://api.test/v1")]
    public void StripApiSuffix_LeavesNonApiRootsIntact(string input, string expected)
    {
        Assert.Equal(expected, UrlHelpers.StripApiSuffix(input));
    }

    [Fact]
    public void StripApiSuffix_OnlyStripsAWholeApiSegment_NotASubstring()
    {
        // "/apidocs" ends in nothing that should be stripped; only a standalone "/api" segment is removed.
        Assert.Equal("https://api.test/apidocs", UrlHelpers.StripApiSuffix("https://api.test/apidocs"));
    }
}
