using WildwoodComponents.Shared.Utilities;
using System.Text;
using System.Text.Json;

namespace WildwoodComponents.Tests.Shared;

public class TokenExpiryParserTests
{
    [Fact]
    public void TryParseUtc_ValidIso8601_ReturnsTrueAndParsesDate()
    {
        var result = TokenExpiryParser.TryParseUtc("2030-06-15T12:00:00Z", out var utcExpiry);

        Assert.True(result);
        Assert.Equal(new DateTime(2030, 6, 15, 12, 0, 0, DateTimeKind.Utc), utcExpiry);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    public void TryParseUtc_InvalidString_ReturnsFalse(string? input)
    {
        var result = TokenExpiryParser.TryParseUtc(input, out _);

        Assert.False(result);
    }

    [Fact]
    public void IsExpired_FutureDate_ReturnsFalse()
    {
        var future = DateTime.UtcNow.AddHours(1).ToString("O");

        Assert.False(TokenExpiryParser.IsExpired(future));
    }

    [Fact]
    public void IsExpired_PastDate_ReturnsTrue()
    {
        var past = DateTime.UtcNow.AddHours(-1).ToString("O");

        Assert.True(TokenExpiryParser.IsExpired(past));
    }

    [Fact]
    public void GetJwtExpiration_ValidJwt_ReturnsExpiry()
    {
        // Build a JWT with exp claim set to 2030-01-01T00:00:00Z (unix 1893456000)
        var header = Base64UrlEncode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncode("{\"sub\":\"123\",\"exp\":1893456000}");
        var jwt = $"{header}.{payload}.fakesignature";

        var expiry = TokenExpiryParser.GetJwtExpiration(jwt);

        Assert.NotNull(expiry);
        Assert.Equal(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), expiry.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("only.one")]
    public void GetJwtExpiration_InvalidJwt_ReturnsNull(string? input)
    {
        Assert.Null(TokenExpiryParser.GetJwtExpiration(input));
    }

    [Fact]
    public void GetRemainingLifetime_ValidFutureToken_ReturnsPositiveTimeSpan()
    {
        // exp = far in the future
        var futureUnix = new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var header = Base64UrlEncode("{\"alg\":\"HS256\"}");
        var payload = Base64UrlEncode($"{{\"exp\":{futureUnix}}}");
        var jwt = $"{header}.{payload}.sig";

        var remaining = TokenExpiryParser.GetRemainingLifetime(jwt);

        Assert.NotNull(remaining);
        Assert.True(remaining.Value.TotalSeconds > 0);
    }

    [Fact]
    public void GetRemainingLifetime_ExpiredToken_ReturnsNull()
    {
        // exp = in the past
        var pastUnix = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var header = Base64UrlEncode("{\"alg\":\"HS256\"}");
        var payload = Base64UrlEncode($"{{\"exp\":{pastUnix}}}");
        var jwt = $"{header}.{payload}.sig";

        Assert.Null(TokenExpiryParser.GetRemainingLifetime(jwt));
    }

    private static string Base64UrlEncode(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
