using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

public class WildwoodAuthServiceTests
{
    private const string LoginOk =
        """{"jwtToken":"jwt-1","refreshToken":"refresh-1","id":"user-1","email":"a@b.test","requiresTwoFactor":false}""";

    private const string LoginNeedsPasswordReset =
        """{"jwtToken":"jwt-1","refreshToken":"refresh-1","id":"user-1","email":"a@b.test","requiresTwoFactor":false,"requiresPasswordReset":true}""";

    private static (WildwoodAuthService Service, FakeHttpMessageHandler Handler, FakeSessionManager Session) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var session = new FakeSessionManager(null);
        var service = new WildwoodAuthService(
            handler.CreateClient("https://api.test/api/"),
            session,
            NullLogger<WildwoodAuthService>.Instance,
            "app-1");
        return (service, handler, session);
    }

    // ── The forced-reset flag is session state, because the API only sends it once ──

    [Fact]
    public async Task LoginAsync_RecordsAPendingPasswordReset()
    {
        var (service, handler, session) = CreateService();
        handler.WhenOk("auth/login", LoginNeedsPasswordReset);

        var result = await service.LoginAsync(new LoginRequest { Username = "ada", Password = "temp-pw" });

        Assert.True(result.Succeeded);
        Assert.True(session.RequiresPasswordReset);
    }

    [Fact]
    public async Task LoginAsync_LeavesTheFlagClearForANormalSignIn()
    {
        var (service, handler, session) = CreateService();
        handler.WhenOk("auth/login", LoginOk);

        var result = await service.LoginAsync(new LoginRequest { Username = "ada", Password = "pw" });

        Assert.True(result.Succeeded);
        Assert.False(session.RequiresPasswordReset);
    }

    [Fact]
    public async Task RefreshTokenAsync_PreservesAPendingPasswordReset()
    {
        // The refresh-token response never carries requiresPasswordReset, so mapping it
        // verbatim would stop asking a user who refreshed before resetting.
        var (service, handler, session) = CreateService();
        session.SetTokens("jwt-1", "refresh-1");
        session.SetRequiresPasswordReset(true);
        handler.WhenOk("auth/refresh-token", """{"jwtToken":"jwt-2","refreshToken":"refresh-2"}""");

        var result = await service.RefreshTokenAsync();

        Assert.True(result.Succeeded);
        Assert.True(result.Response!.RequiresPasswordReset);
        Assert.True(session.RequiresPasswordReset);
    }

    [Fact]
    public async Task RefreshTokenAsync_DoesNotInventAPasswordReset()
    {
        var (service, handler, session) = CreateService();
        session.SetTokens("jwt-1", "refresh-1");
        handler.WhenOk("auth/refresh-token", """{"jwtToken":"jwt-2","refreshToken":"refresh-2"}""");

        var result = await service.RefreshTokenAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.Response!.RequiresPasswordReset);
        Assert.False(session.RequiresPasswordReset);
    }

    // ── Reset: authenticated by the session, or anonymously by an emailed token ──

    [Fact]
    public async Task ResetPasswordAsync_ClearsTheFlagAndUsesTheSessionBearer()
    {
        var (service, handler, session) = CreateService();
        session.SetTokens("jwt-1", "refresh-1");
        session.SetRequiresPasswordReset(true);
        handler.WhenOk("auth/reset-password", "{}");

        var result = await service.ResetPasswordAsync(new ResetPasswordRequest
        {
            NewPassword = "NewP@ss1!",
            ConfirmPassword = "NewP@ss1!"
        });

        Assert.True(result.Succeeded);
        Assert.False(session.RequiresPasswordReset);
        Assert.Equal(1, session.ApplyAuthorizationHeaderCalls);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        Assert.False(body.RootElement.TryGetProperty("resetToken", out _));
    }

    [Fact]
    public async Task ResetPasswordAsync_WithATokenIsAnonymousAndCarriesTheToken()
    {
        // Wire parity with the JS SDK's skipAuth emailed-link path; the API does not bind
        // resetToken yet, so this is a shape guarantee rather than a working flow.
        var (service, handler, session) = CreateService();
        session.SetTokens("jwt-1", "refresh-1");
        handler.WhenOk("auth/reset-password", "{}");

        var result = await service.ResetPasswordAsync(new ResetPasswordRequest
        {
            Token = "tok-1",
            NewPassword = "NewP@ss1!",
            ConfirmPassword = "NewP@ss1!"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(0, session.ApplyAuthorizationHeaderCalls);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        Assert.Equal("tok-1", body.RootElement.GetProperty("resetToken").GetString());
    }
}
