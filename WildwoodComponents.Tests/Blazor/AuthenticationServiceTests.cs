using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

public class AuthenticationServiceTests
{
    private static (AuthenticationService Service, FakeHttpMessageHandler Handler, FakeLocalStorageService Storage, HttpClient Client) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var client = handler.CreateClient("https://api.test/");
        var storage = new FakeLocalStorageService();
        var service = new AuthenticationService(client, storage, NullLogger<AuthenticationService>.Instance);
        return (service, handler, storage, client);
    }

    // ── Refresh must not silently cancel a pending forced reset ──────────────────

    [Fact]
    public async Task RefreshTokenAsync_PreservesAPendingPasswordReset()
    {
        // The refresh-token endpoint never returns requiresPasswordReset, so storing its
        // response verbatim would drop the flag and stop asking the user to replace a
        // temporary password.
        var (service, handler, storage, _) = CreateService();
        await storage.SetItemAsync(WildwoodStorageKeys.RefreshToken, "refresh-1");
        await storage.SetItemAsync(WildwoodStorageKeys.User, new AuthenticationResponse
        {
            Email = "a@b.test",
            JwtToken = "old",
            RefreshToken = "refresh-1",
            RequiresPasswordReset = true
        });
        handler.WhenOk("auth/refresh-token",
            """{"jwtToken":"new","refreshToken":"refresh-2","requiresPasswordReset":false}""");

        AuthenticationResponse? announced = null;
        service.OnAuthenticationChanged += r => announced = r;

        var refreshed = await service.RefreshTokenAsync();

        Assert.True(refreshed);
        var stored = await storage.GetItemAsync<AuthenticationResponse>(WildwoodStorageKeys.User);
        Assert.NotNull(stored);
        Assert.Equal("new", stored!.JwtToken);
        Assert.True(stored.RequiresPasswordReset);
        Assert.NotNull(announced);
        Assert.True(announced!.RequiresPasswordReset);
    }

    [Fact]
    public async Task RefreshTokenAsync_DoesNotInventAPasswordReset()
    {
        // Carrying the flag forward means "true stays true", never "false becomes true".
        var (service, handler, storage, _) = CreateService();
        await storage.SetItemAsync(WildwoodStorageKeys.RefreshToken, "refresh-1");
        await storage.SetItemAsync(WildwoodStorageKeys.User, new AuthenticationResponse
        {
            Email = "a@b.test",
            JwtToken = "old",
            RefreshToken = "refresh-1",
            RequiresPasswordReset = false
        });
        handler.WhenOk("auth/refresh-token", """{"jwtToken":"new","refreshToken":"refresh-2"}""");

        var refreshed = await service.RefreshTokenAsync();

        Assert.True(refreshed);
        var stored = await storage.GetItemAsync<AuthenticationResponse>(WildwoodStorageKeys.User);
        Assert.NotNull(stored);
        Assert.False(stored!.RequiresPasswordReset);
    }

    // ── The bearer belongs on the request, not on the shared client ──────────────

    [Fact]
    public async Task ResetPasswordAsync_SendsTheStoredBearerOnTheRequest_NotTheClient()
    {
        var (service, handler, storage, client) = CreateService();
        await storage.SetItemAsync(WildwoodStorageKeys.AccessToken, "jwt-1");
        handler.WhenOk("auth/reset-password", "{}");

        var succeeded = await service.ResetPasswordAsync("NewP@ss1!", "NewP@ss1!", "app-1");

        Assert.True(succeeded);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer jwt-1", request.Authorization);
        Assert.Null(client.DefaultRequestHeaders.Authorization);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("NewP@ss1!", body.RootElement.GetProperty("newPassword").GetString());
        Assert.Equal("NewP@ss1!", body.RootElement.GetProperty("confirmPassword").GetString());
        Assert.Equal("app-1", body.RootElement.GetProperty("appId").GetString());
        Assert.False(body.RootElement.TryGetProperty("resetToken", out _));
    }

    [Fact]
    public async Task ResetPasswordAsync_StillCarriesTheBearerAfterARefresh()
    {
        // RefreshTokenAsync nulls DefaultRequestHeaders.Authorization, so a reset that leaned
        // on the client-level header 401'd after any refresh. Reading the token per request
        // keeps the reset working across a refresh.
        var (service, handler, storage, client) = CreateService();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale");
        await storage.SetItemAsync(WildwoodStorageKeys.RefreshToken, "refresh-1");
        handler.WhenOk("auth/refresh-token", """{"jwtToken":"new","refreshToken":"refresh-2"}""");
        handler.WhenOk("auth/reset-password", "{}");

        Assert.True(await service.RefreshTokenAsync());
        Assert.True(await service.ResetPasswordAsync("NewP@ss1!", "NewP@ss1!", "app-1"));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("auth/reset-password", handler.Requests[1].Url);
        Assert.Equal("Bearer new", handler.Requests[1].Authorization);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithAResetToken_IsAnonymousAndCarriesTheToken()
    {
        // Wire parity with the JS SDK's skipAuth emailed-link path. WildwoodAPI does not bind
        // resetToken yet, so this form is a shape guarantee rather than a working flow.
        var (service, handler, storage, _) = CreateService();
        await storage.SetItemAsync(WildwoodStorageKeys.AccessToken, "jwt-1");
        handler.WhenOk("auth/reset-password", "{}");

        var succeeded = await service.ResetPasswordAsync("NewP@ss1!", "NewP@ss1!", "app-1", "tok-1");

        Assert.True(succeeded);
        var request = Assert.Single(handler.Requests);
        Assert.Null(request.Authorization);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("tok-1", body.RootElement.GetProperty("resetToken").GetString());
    }
}
