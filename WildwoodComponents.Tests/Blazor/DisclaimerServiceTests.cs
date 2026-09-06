using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
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

    /// <summary>
    /// Exact-equality on purpose. A `Contains` assertion passes just as happily against
    /// https://api.test/api/api/disclaimeracceptance/accept-bulk, which is precisely the
    /// misconfiguration (a base address ending in /api) that made every acceptance 404.
    /// </summary>
    [Fact]
    public async Task AcceptDisclaimersAsync_PostsToApiRootRelativePath()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("disclaimeracceptance/accept-bulk", """{"success":true}""");

        await service.AcceptDisclaimersAsync("app-1", [new DisclaimerAcceptanceResult
        {
            CompanyDisclaimerId = "disc-1",
            CompanyDisclaimerVersionId = "ver-1"
        }]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.test/api/disclaimeracceptance/accept-bulk", request.Url);
    }

    [Fact]
    public async Task AcceptDisclaimersAsync_SendsBearerTokenWhenSet()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("disclaimeracceptance/accept-bulk", """{"success":true}""");
        service.SetAuthToken("jwt-1");

        await service.AcceptDisclaimersAsync("app-1", [new DisclaimerAcceptanceResult
        {
            CompanyDisclaimerId = "disc-1",
            CompanyDisclaimerVersionId = "ver-1"
        }]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer jwt-1", request.Authorization);
    }

    [Fact]
    public async Task AcceptDisclaimerAsync_SendsBearerTokenWhenSet()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("disclaimeracceptance/accept", """{"success":true}""");
        service.SetAuthToken("jwt-1");

        await service.AcceptDisclaimerAsync("app-1", "disc-1", "ver-1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer jwt-1", request.Authorization);
    }

    [Fact]
    public async Task AcceptDisclaimersAsync_OmitsAuthorizationWhenNoTokenSet()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("disclaimeracceptance/accept-bulk", """{"success":true}""");

        await service.AcceptDisclaimersAsync("app-1", [new DisclaimerAcceptanceResult
        {
            CompanyDisclaimerId = "disc-1",
            CompanyDisclaimerVersionId = "ver-1"
        }]);

        var request = Assert.Single(handler.Requests);
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task SetAuthToken_WithNull_ClearsAPreviouslySetToken()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("disclaimeracceptance/accept-bulk", """{"success":true}""");
        service.SetAuthToken("jwt-1");
        service.SetAuthToken(null);

        await service.AcceptDisclaimersAsync("app-1", [new DisclaimerAcceptanceResult
        {
            CompanyDisclaimerId = "disc-1",
            CompanyDisclaimerVersionId = "ver-1"
        }]);

        var request = Assert.Single(handler.Requests);
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task AcceptDisclaimersAsync_MapsUnauthorizedToAFriendlyError()
    {
        var (service, handler) = CreateService();
        handler.When("disclaimeracceptance/accept-bulk", System.Net.HttpStatusCode.Unauthorized, "");

        var result = await service.AcceptDisclaimersAsync("app-1", [new DisclaimerAcceptanceResult
        {
            CompanyDisclaimerId = "disc-1",
            CompanyDisclaimerVersionId = "ver-1"
        }]);

        Assert.False(result.Success);
        Assert.Contains("sign in again", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
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
