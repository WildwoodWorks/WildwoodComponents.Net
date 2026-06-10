using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

public class TwoFactorSettingsServiceTests
{
    private static (TwoFactorSettingsService Service, FakeHttpMessageHandler Handler) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var service = new TwoFactorSettingsService(
            handler.CreateClient("https://api.test/"),
            NullLogger<TwoFactorSettingsService>.Instance);
        return (service, handler);
    }

    [Fact]
    public async Task GetConfigurationAsync_CallsConfigurationEndpoint_AndParsesDto()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("twofactor/configuration", """
        {
            "isEnabled": true,
            "isRequired": false,
            "availableMethods": [
                { "providerType": "Email", "name": "Email", "description": "Code by email", "icon": "mail", "isEnabled": true }
            ],
            "codeValiditySeconds": 300,
            "maxAttempts": 5,
            "lockoutMinutes": 15,
            "allowRememberDevice": true,
            "rememberDeviceDays": 30
        }
        """);

        var config = await service.GetConfigurationAsync("app-1");

        Assert.NotNull(config);
        Assert.True(config.IsEnabled);
        Assert.False(config.IsRequired);
        Assert.Equal(300, config.CodeValiditySeconds);
        Assert.Equal(30, config.RememberDeviceDays);
        var method = Assert.Single(config.AvailableMethods);
        Assert.Equal("Email", method.ProviderType);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("/api/twofactor/configuration/app-1", request.Url);
    }

    [Fact]
    public async Task GetConfigurationAsync_ReturnsNull_OnServerError()
    {
        var (service, handler) = CreateService();
        handler.When("twofactor/configuration", System.Net.HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        var config = await service.GetConfigurationAsync("app-1");

        Assert.Null(config);
    }
}
