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
