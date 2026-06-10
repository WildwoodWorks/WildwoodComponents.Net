using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

public class DisclaimerServiceTests
{
    private static (DisclaimerService Service, FakeHttpMessageHandler Handler) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var service = new DisclaimerService(
            handler.CreateClient("https://api.test/"),
            NullLogger<DisclaimerService>.Instance);
        return (service, handler);
    }

    [Fact]
    public async Task AcceptDisclaimerAsync_PostsSingleAcceptPayload()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("disclaimeracceptance/accept", """{"success":true,"message":"Disclaimer accepted"}""");

        var result = await service.AcceptDisclaimerAsync("app-1", "disc-1", "ver-1");

        Assert.True(result.Success);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/api/disclaimeracceptance/accept", request.Url);
        Assert.DoesNotContain("accept-bulk", request.Url);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("app-1", body.RootElement.GetProperty("appId").GetString());
        Assert.Equal("disc-1", body.RootElement.GetProperty("companyDisclaimerId").GetString());
        Assert.Equal("ver-1", body.RootElement.GetProperty("companyDisclaimerVersionId").GetString());
    }

    [Fact]
    public async Task AcceptDisclaimerAsync_MapsNotFoundToFriendlyError()
    {
        var (service, handler) = CreateService();
        handler.When("disclaimeracceptance/accept", System.Net.HttpStatusCode.NotFound, """{"error":"missing"}""");

        var result = await service.AcceptDisclaimerAsync("app-1", "disc-1", "ver-gone");

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPendingDisclaimersAsync_AppendsShowOnQuery()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("disclaimeracceptance/pending", """{"disclaimers":[]}""");

        await service.GetPendingDisclaimersAsync("app-1", showOn: "registration");

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/api/disclaimeracceptance/pending/app-1", request.Url);
        Assert.Contains("showOn=registration", request.Url);
    }
}
