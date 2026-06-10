using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

public class WildwoodDisclaimerServiceTests
{
    private static (WildwoodDisclaimerService Service, FakeHttpMessageHandler Handler) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var service = new WildwoodDisclaimerService(
            handler.CreateClient("https://api.test/api/"),
            new FakeSessionManager(),
            NullLogger<WildwoodDisclaimerService>.Instance);
        return (service, handler);
    }

    [Fact]
    public async Task AcceptDisclaimerAsync_PostsSingleAcceptPayload()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("disclaimeracceptance/accept", """{"success":true,"message":"Disclaimer accepted"}""");

        var result = await service.AcceptDisclaimerAsync("app-1", "disc-2", "ver-5");

        Assert.True(result.Succeeded);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("disclaimeracceptance/accept", request.Url);
        Assert.DoesNotContain("accept-bulk", request.Url);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("app-1", body.RootElement.GetProperty("appId").GetString());
        Assert.Equal("disc-2", body.RootElement.GetProperty("companyDisclaimerId").GetString());
        Assert.Equal("ver-5", body.RootElement.GetProperty("companyDisclaimerVersionId").GetString());
    }

    [Fact]
    public async Task AcceptDisclaimerAsync_ReturnsFailureMessage_OnError()
    {
        var (service, handler) = CreateService();
        handler.When("disclaimeracceptance/accept", System.Net.HttpStatusCode.BadRequest,
            """{"message":"Disclaimer version not found"}""");

        var result = await service.AcceptDisclaimerAsync("app-1", "disc-2", "ver-gone");

        Assert.False(result.Succeeded);
        Assert.Equal("Disclaimer version not found", result.Message);
    }
}
