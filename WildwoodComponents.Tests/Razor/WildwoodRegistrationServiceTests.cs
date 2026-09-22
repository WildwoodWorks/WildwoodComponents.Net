using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

public class WildwoodRegistrationServiceTests
{
    private static (WildwoodRegistrationService Service, FakeHttpMessageHandler Handler) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        // Razor services use relative URLs against a base address that includes /api/
        var service = new WildwoodRegistrationService(
            handler.CreateClient("https://api.test/api/"),
            new FakeSessionManager(),
            NullLogger<WildwoodRegistrationService>.Instance,
            "app-1");
        return (service, handler);
    }

    [Fact]
    public async Task GetPasswordRequirementsAsync_DerivesTextFromAuthConfiguration()
    {
        // Regression: the old auth/password-requirements/{appId} endpoint never existed
        var (service, handler) = CreateService();
        handler.WhenOk("auth-configuration", """
        {
            "passwordMinimumLength": 10,
            "passwordRequireUppercase": true,
            "passwordRequireLowercase": false,
            "passwordRequireDigit": true,
            "passwordRequireSpecialChar": false
        }
        """);

        var text = await service.GetPasswordRequirementsAsync("app-1");

        Assert.Equal("Password must have at least 10 characters, uppercase letters (A-Z), and numbers (0-9).", text);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("appcomponentconfigurations/app-1/auth-configuration", request.Url);
        Assert.DoesNotContain("password-requirements", request.Url);
    }

    [Fact]
    public async Task GetPasswordRequirementsAsync_SingleRequirement_UsesSimpleSentence()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("auth-configuration", """
        {
            "passwordMinimumLength": 8,
            "passwordRequireUppercase": false,
            "passwordRequireLowercase": false,
            "passwordRequireDigit": false,
            "passwordRequireSpecialChar": false
        }
        """);

        var text = await service.GetPasswordRequirementsAsync("app-1");

        Assert.Equal("Password must have at least 8 characters.", text);
    }

    [Fact]
    public async Task GetPasswordRequirementsAsync_TwoRequirements_UsesAndWithoutComma()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("auth-configuration", """
        {
            "passwordMinimumLength": 8,
            "passwordRequireUppercase": false,
            "passwordRequireLowercase": true,
            "passwordRequireDigit": false,
            "passwordRequireSpecialChar": false
        }
        """);

        var text = await service.GetPasswordRequirementsAsync("app-1");

        Assert.Equal("Password must have at least 8 characters and lowercase letters (a-z).", text);
    }

    [Fact]
    public async Task GetPasswordRequirementsAsync_ReturnsNull_WhenConfigUnavailable()
    {
        var (service, handler) = CreateService();
        handler.When("auth-configuration", System.Net.HttpStatusCode.NotFound, """{"error":"missing"}""");

        var text = await service.GetPasswordRequirementsAsync("app-1");

        Assert.Null(text);
    }

    // ── Registration token details ──────────────────────────────────────────────
    // Port of the getRegistrationTokenDetails cases in
    // packages/wildwood-core/src/__tests__/authService.test.ts. The load-bearing rule: null means
    // the details could not be READ, which is NOT the same as an invalid token.

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
        Assert.Contains("registrationtokens/validate-detailed/TOKEN%2F1", request.Url);
        Assert.NotNull(details);
        Assert.True(details!.IsValid);
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
        handler.When("registrationtokens/validate-detailed", System.Net.HttpStatusCode.NotFound, "");

        Assert.Null(await service.GetRegistrationTokenDetailsAsync("T"));
    }

    [Fact]
    public async Task ValidateTokenAsync_StillReadsTheDetailedRoute_WithTheTokenEncoded()
    {
        // Both readers share one request path, so the server-render model keeps working and the
        // token stays a single path segment for it too.
        var (service, handler) = CreateService();
        handler.WhenOk("registrationtokens/validate-detailed",
            """{"isValid":true,"assignedRole":"User","requiresPaymentSetup":false}""");

        var validation = await service.ValidateTokenAsync("TOKEN/1");

        Assert.NotNull(validation);
        Assert.True(validation!.IsValid);
        Assert.Equal("User", validation.AssignedRole);
        Assert.Contains("registrationtokens/validate-detailed/TOKEN%2F1", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task LinkTransactionToUserAsync_PostsToLinkByExternalId()
    {
        // Regression: payment/link-transaction never existed in the backend
        var (service, handler) = CreateService();
        handler.WhenOk("link-by-external-id", """{"success":true}""");

        var ok = await service.LinkTransactionToUserAsync("pi_abc", "user-2");

        Assert.True(ok);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("paymenttransactions/link-by-external-id", request.Url);
        Assert.DoesNotContain("payment/link-transaction", request.Url);
    }
}
