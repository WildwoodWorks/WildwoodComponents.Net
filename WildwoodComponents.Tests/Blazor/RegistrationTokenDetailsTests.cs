using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// Port of the <c>getRegistrationTokenDetails</c> cases in
/// packages/wildwood-core/src/__tests__/authService.test.ts. The load-bearing rule: <c>null</c>
/// means the details could not be READ, which is NOT the same as an invalid token — callers fall
/// back to the plain validity check rather than telling the registrant their token is bad.
/// </summary>
public class RegistrationTokenDetailsTests
{
    private static (AuthenticationService Service, FakeHttpMessageHandler Handler) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var service = new AuthenticationService(
            handler.CreateClient("https://api.test/"),
            new FakeLocalStorageService(),
            NullLogger<AuthenticationService>.Instance);
        return (service, handler);
    }

    [Fact]
    public async Task GetRegistrationTokenDetailsAsync_ReturnsThePlansTheTokenGrants()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("registrationtokens/validate-detailed",
            """
            {"isValid":true,"errorMessage":null,
             "appGrants":[{"appId":"app-1","appTierId":"tier-pro","appTierName":"Pro",
                           "addOnIds":["radar"],"featureCodes":["CHAT"]}]}
            """);

        // The token is URL-encoded (JS uses encodeURIComponent): a token carrying a slash must
        // stay one path segment rather than forging a different route.
        var details = await service.GetRegistrationTokenDetailsAsync("TOKEN/1");

        var request = Assert.Single(handler.Requests);
        Assert.Contains("api/registrationtokens/validate-detailed/TOKEN%2F1", request.Url);
        Assert.NotNull(details);
        Assert.True(details!.IsValid);
        Assert.Null(details.ErrorMessage);
        var grant = Assert.Single(details.AppGrants);
        Assert.Equal("app-1", grant.AppId);
        Assert.Equal("tier-pro", grant.AppTierId);
        Assert.Equal("Pro", grant.AppTierName);
        Assert.Equal("radar", Assert.Single(grant.AddOnIds));
        Assert.Equal("CHAT", Assert.Single(grant.FeatureCodes));
    }

    [Fact]
    public async Task GetRegistrationTokenDetailsAsync_ScopesToAnAppWhenOneIsGiven()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("registrationtokens/validate-detailed", """{"isValid":true}""");

        await service.GetRegistrationTokenDetailsAsync("T", "app-1");

        Assert.Contains("validate-detailed/T?appId=app-1", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task GetRegistrationTokenDetailsAsync_TreatsAServerWithoutPlanGrants_AsATokenWithNone()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("registrationtokens/validate-detailed", """{"isValid":true}""");

        var details = await service.GetRegistrationTokenDetailsAsync("T");

        Assert.NotNull(details);
        Assert.True(details!.IsValid);
        Assert.Empty(details.AppGrants);
    }

    [Fact]
    public async Task GetRegistrationTokenDetailsAsync_ReportsAnInvalidTokenAsDetails_NotNull()
    {
        // An invalid token is an ANSWER: the server said so, and the caller shows its message.
        var (service, handler) = CreateService();
        handler.WhenOk("registrationtokens/validate-detailed",
            """{"isValid":false,"errorMessage":"That token has expired"}""");

        var details = await service.GetRegistrationTokenDetailsAsync("T");

        Assert.NotNull(details);
        Assert.False(details!.IsValid);
        Assert.Equal("That token has expired", details.ErrorMessage);
    }

    [Fact]
    public async Task GetRegistrationTokenDetailsAsync_ReturnsNull_WhenTheDetailsCannotBeRead()
    {
        // A server that predates the route must not read as "your token is invalid".
        var (service, handler) = CreateService();
        handler.When("registrationtokens/validate-detailed", HttpStatusCode.NotFound, "");

        Assert.Null(await service.GetRegistrationTokenDetailsAsync("T"));
    }
}
