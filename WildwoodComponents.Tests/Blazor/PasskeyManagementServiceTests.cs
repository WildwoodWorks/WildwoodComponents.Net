using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// The passkey self-service calls behind My Account's Passkeys section: the paths and bodies the
/// api/webauthn endpoints expect, and the API's own refusal text surfacing as the error the user sees.
/// </summary>
public class PasskeyManagementServiceTests
{
    private static (PasskeyManagementService Service, FakeHttpMessageHandler Handler) CreateService(string baseUrl = "https://api.test/")
    {
        var handler = new FakeHttpMessageHandler();
        var service = new PasskeyManagementService(handler.CreateClient(), NullLogger<PasskeyManagementService>.Instance);
        service.SetApiBaseUrl(baseUrl);
        service.SetAuthToken("jwt-1");
        return (service, handler);
    }

    [Fact]
    public async Task ABaseEndingInApi_IsTrimmed_SoCallsDoNotGoToApiApi()
    {
        var (service, handler) = CreateService("https://api.test/api");
        handler.WhenOk("webauthn/credentials", "[]");

        await service.GetPasskeysAsync("app-1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.test/api/webauthn/credentials?appId=app-1", request.Url);
        Assert.Equal("Bearer jwt-1", request.Authorization);
    }

    [Fact]
    public async Task GetPasskeys_ParsesTheApiDto()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("webauthn/credentials", """
        [
          { "id": "pk-1", "friendlyName": "Laptop", "isPlatformAuthenticator": true, "isActive": true, "usageCount": 3, "createdAt": "2026-11-01T00:00:00Z", "lastUsedAt": "2026-11-20T00:00:00Z" },
          { "id": "pk-2", "friendlyName": null, "isPlatformAuthenticator": false, "isActive": true, "usageCount": 0, "createdAt": "2026-11-02T00:00:00Z" }
        ]
        """);

        var passkeys = await service.GetPasskeysAsync("app-1");

        Assert.Equal(2, passkeys.Count);
        Assert.Equal("Laptop", passkeys[0].DisplayName);
        Assert.Equal(3, passkeys[0].UsageCount);
        Assert.Equal("Security key", passkeys[1].DisplayName);
        Assert.Null(passkeys[1].LastUsedAt);
    }

    [Theory]
    [InlineData("""{"enabled":true}""", true)]
    [InlineData("""{"enabled":false}""", false)]
    [InlineData("""{}""", false)]
    public async Task IsEnabled_ReadsTheEnabledFlag(string body, bool expected)
    {
        var (service, handler) = CreateService();
        handler.WhenOk("webauthn/enabled/app-1", body);

        Assert.Equal(expected, await service.IsEnabledAsync("app-1"));
    }

    [Fact]
    public async Task IsEnabled_IsFalse_WhenTheCallFails()
    {
        var (service, handler) = CreateService();
        handler.When("webauthn/enabled", HttpStatusCode.Unauthorized, "{}");

        Assert.False(await service.IsEnabledAsync("app-1"));
    }

    [Fact]
    public async Task Registration_SendsTheNameThenTheBrowsersAttestation()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("webauthn/register/options", """{"challenge":"abc","sessionId":"s-1"}""");
        handler.WhenOk("webauthn/register", """{"id":"pk-9","friendlyName":"Work laptop","isActive":true,"createdAt":"2026-11-26T00:00:00Z"}""");

        var options = await service.GetRegistrationOptionsAsync("app-1", "  Work laptop ");
        Assert.Equal("abc", options.GetProperty("challenge").GetString());

        var credential = JsonDocument.Parse("""
        { "id": "cred", "rawId": "cred", "type": "public-key",
          "response": { "clientDataJSON": "cdj", "attestationObject": "att", "transports": ["internal"] } }
        """).RootElement;
        var added = await service.CompleteRegistrationAsync("app-1", credential);

        Assert.Equal("Work laptop", added!.DisplayName);
        var optionsBody = JsonDocument.Parse(handler.Requests[0].Body!).RootElement;
        Assert.Equal("Work laptop", optionsBody.GetProperty("friendlyName").GetString());
        var registerBody = JsonDocument.Parse(handler.Requests[1].Body!).RootElement;
        Assert.Equal("app-1", registerBody.GetProperty("appId").GetString());
        Assert.Equal("att", registerBody.GetProperty("response").GetProperty("attestationObject").GetString());
        Assert.Equal("internal", registerBody.GetProperty("response").GetProperty("transports")[0].GetString());
    }

    [Fact]
    public async Task Rename_PatchesTheCredential()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("webauthn/credentials/pk-1", """{"id":"pk-1","friendlyName":"Phone"}""");

        var renamed = await service.RenameAsync("pk-1", " Phone ");

        Assert.Equal("Phone", renamed!.FriendlyName);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("Phone", JsonDocument.Parse(request.Body!).RootElement.GetProperty("friendlyName").GetString());
    }

    [Fact]
    public async Task Delete_DeletesTheCredential()
    {
        var (service, handler) = CreateService();
        handler.When("webauthn/credentials/pk-1", HttpStatusCode.NoContent, "");

        Assert.True(await service.DeleteAsync("pk-1"));
        Assert.Equal(HttpMethod.Delete, Assert.Single(handler.Requests).Method);
    }

    [Fact]
    public async Task ARefusal_CarriesTheApisMessage()
    {
        var (service, handler) = CreateService();
        handler.When("webauthn/register/options", HttpStatusCode.BadRequest,
            """{"error":"PasskeyNotEnabled","message":"Passkey authentication is not enabled for this app."}""");

        var ex = await Assert.ThrowsAsync<PasskeyManagementException>(() => service.GetRegistrationOptionsAsync("app-1", null));

        Assert.Equal("Passkey authentication is not enabled for this app.", ex.Message);
    }

    [Fact]
    public async Task ARefusalWithoutAMessage_FallsBackToAPlainSentence()
    {
        var (service, handler) = CreateService();
        handler.When("webauthn/credentials/pk-1", HttpStatusCode.NotFound, "");

        var ex = await Assert.ThrowsAsync<PasskeyManagementException>(() => service.DeleteAsync("pk-1"));

        Assert.Equal("The passkey could not be removed.", ex.Message);
    }
}
