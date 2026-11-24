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

    [Fact]
    public async Task BeginAuthenticatorEnrollment_MapsTheApisFieldNames()
    {
        // The API's AuthenticatorEnrollmentResponse: secretKey / qrCodeBase64 (already a data URL).
        var (service, handler) = CreateService();
        handler.WhenOk("twofactor/enroll/authenticator", """
        {
            "credentialId": "cred-1",
            "secretKey": "JBSWY3DPEHPK3PXP",
            "otpAuthUri": "otpauth://totp/Wildwood:e2e?secret=JBSWY3DPEHPK3PXP",
            "qrCodeBase64": "data:image/png;base64,iVBORw0KGgo=",
            "issuer": "Wildwood",
            "accountName": "e2e"
        }
        """);

        var result = await service.BeginAuthenticatorEnrollmentAsync("Authenticator App");

        Assert.True(result.Success);
        Assert.Equal("cred-1", result.CredentialId);
        Assert.Equal("JBSWY3DPEHPK3PXP", result.Secret);
        Assert.Equal("JBSW Y3DP EHPK 3PXP", result.ManualEntryKey);
        Assert.Equal("data:image/png;base64,iVBORw0KGgo=", result.QrCodeDataUrl);
    }

    [Fact]
    public async Task SetApiBaseUrl_AcceptsABaseEndingInApi()
    {
        // Hosts commonly hold ".../api" as their API base; the paths here already start with api/.
        var (service, handler) = CreateService();
        handler.WhenOk("twofactor/configuration", """{ "isEnabled": true }""");
        service.SetApiBaseUrl("https://api.test/api");

        await service.GetConfigurationAsync("app-1");

        var request = Assert.Single(handler.Requests);
        Assert.Contains("https://api.test/api/twofactor/configuration/app-1", request.Url);
        Assert.DoesNotContain("/api/api/", request.Url);
    }
}
