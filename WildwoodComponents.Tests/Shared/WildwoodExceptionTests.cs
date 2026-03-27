using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

public class WildwoodExceptionTests
{
    [Fact]
    public void Constructor_WithMessageAndStatus_SetsProperties()
    {
        var ex = new WildwoodException("Test error", 404);

        Assert.Equal("Test error", ex.Message);
        Assert.Equal(404, ex.Status);
        Assert.Equal(WildwoodErrorCode.NotFound, ex.Code);
    }

    [Fact]
    public void FromHttpResponse_401_MapsToUnauthorized()
    {
        var ex = WildwoodException.FromHttpResponse(401, null);

        Assert.Equal(401, ex.Status);
        Assert.Equal(WildwoodErrorCode.Unauthorized, ex.Code);
    }

    [Fact]
    public void FromHttpResponse_403_MapsToForbidden()
    {
        var ex = WildwoodException.FromHttpResponse(403, null);

        Assert.Equal(WildwoodErrorCode.Forbidden, ex.Code);
    }

    [Fact]
    public void FromHttpResponse_404_MapsToNotFound()
    {
        var ex = WildwoodException.FromHttpResponse(404, null);

        Assert.Equal(WildwoodErrorCode.NotFound, ex.Code);
    }

    [Fact]
    public void FromHttpResponse_429_MapsToRateLimited()
    {
        var ex = WildwoodException.FromHttpResponse(429, null);

        Assert.Equal(WildwoodErrorCode.RateLimited, ex.Code);
    }

    [Fact]
    public void FromHttpResponse_500_MapsToServerError()
    {
        var ex = WildwoodException.FromHttpResponse(500, null);

        Assert.Equal(WildwoodErrorCode.ServerError, ex.Code);
    }

    [Fact]
    public void FromHttpResponse_JsonBodyWithMessage_ExtractsMessage()
    {
        var body = "{\"message\":\"Custom error message\"}";

        var ex = WildwoodException.FromHttpResponse(400, body);

        Assert.Equal("Custom error message", ex.Message);
    }

    [Fact]
    public void FromHttpResponse_JsonBodyWithErrorField_ExtractsMessage()
    {
        var body = "{\"error\":\"Something went wrong\"}";

        var ex = WildwoodException.FromHttpResponse(400, body);

        Assert.Equal("Something went wrong", ex.Message);
    }

    [Fact]
    public void FromHttpResponse_NullBody_UsesFallbackMessage()
    {
        var ex = WildwoodException.FromHttpResponse(400, null, "Fallback");

        Assert.Equal("Fallback", ex.Message);
    }
}
