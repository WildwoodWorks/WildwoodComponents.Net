using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Http;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// Razor honours "two-factor enrollment required": a restricted sign-in is recorded on the session
/// (flag or claim), the proxy can complete it once the user has enrolled, and an API refusal marks
/// the calling user's session. Before, a restricted user was signed in as fully authenticated.
/// </summary>
public class WildwoodAuthServiceTwoFactorEnrollmentTests
{
    private static string Jwt(string sub, bool pending)
    {
        static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var claim = pending ? ",\"tfa_enroll_pending\":\"true\"" : string.Empty;
        return Encode("{\"alg\":\"none\"}") + "." + Encode($"{{\"sub\":\"{sub}\",\"exp\":4102444800{claim}}}") + ".sig";
    }

    private static string AuthJson(string jwt, bool flag = false) =>
        $"{{\"jwtToken\":\"{jwt}\",\"refreshToken\":\"refresh-1\",\"id\":\"user-1\",\"email\":\"a@b.test\",\"requiresTwoFactor\":false,\"requiresTwoFactorEnrollment\":{(flag ? "true" : "false")}}}";

    private static (WildwoodAuthService Service, FakeHttpMessageHandler Handler, FakeSessionManager Session) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var session = new FakeSessionManager(null);
        var service = new WildwoodAuthService(
            handler.CreateClient("https://api.test/api/"), session, NullLogger<WildwoodAuthService>.Instance, "app-1");
        return (service, handler, session);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_restricted_sign_in_stores_the_tokens_and_records_the_restriction(bool flag, bool claim)
    {
        var (service, handler, session) = CreateService();
        handler.WhenOk("auth/login", AuthJson(Jwt("user-1", claim), flag));

        var result = await service.LoginAsync(new LoginRequest { Username = "ada", Password = "pw" });

        Assert.True(result.Succeeded);
        Assert.True(result.Response!.RequiresTwoFactorEnrollment);
        Assert.True(session.IsTwoFactorEnrollmentPending);
        // Stored: enrollment needs the restricted token. The PROXY withholds the app sign-in.
        Assert.False(string.IsNullOrEmpty(session.GetAccessToken()));
    }

    [Fact]
    public async Task An_unrestricted_sign_in_is_not_pending()
    {
        var (service, handler, session) = CreateService();
        handler.WhenOk("auth/login", AuthJson(Jwt("user-1", false)));

        var result = await service.LoginAsync(new LoginRequest { Username = "ada", Password = "pw" });

        Assert.False(result.Response!.RequiresTwoFactorEnrollment);
        Assert.False(session.IsTwoFactorEnrollmentPending);
    }

    [Fact]
    public async Task Completing_after_enrolling_refreshes_and_lifts_the_restriction()
    {
        var (service, handler, session) = CreateService();
        handler.WhenOk("auth/login", AuthJson(Jwt("user-1", true), flag: true));
        handler.WhenOk("auth/refresh-token", AuthJson(Jwt("user-1", false)));
        await service.LoginAsync(new LoginRequest { Username = "ada", Password = "pw" });

        var result = await service.CompleteTwoFactorEnrollmentAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.Response!.RequiresTwoFactorEnrollment);
        Assert.False(session.IsTwoFactorEnrollmentPending);
    }

    [Fact]
    public async Task Completing_while_still_restricted_fails_with_the_reason()
    {
        var (service, handler, session) = CreateService();
        handler.WhenOk("auth/login", AuthJson(Jwt("user-1", true)));
        handler.WhenOk("auth/refresh-token", AuthJson(Jwt("user-1", true)));
        await service.LoginAsync(new LoginRequest { Username = "ada", Password = "pw" });

        var result = await service.CompleteTwoFactorEnrollmentAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(WildwoodAuthService.EnrollmentStillPendingMessage, result.ErrorMessage);
        Assert.True(session.IsTwoFactorEnrollmentPending);
    }

    [Fact]
    public async Task Completing_without_a_session_fails()
    {
        var (service, _, _) = CreateService();

        var result = await service.CompleteTwoFactorEnrollmentAsync();

        Assert.False(result.Succeeded);
    }
}

public class WildwoodTwoFactorEnrollmentHandlerTests
{
    private static string Jwt(string sub)
    {
        static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return Encode("{\"alg\":\"none\"}") + "." + Encode($"{{\"sub\":\"{sub}\"}}") + ".sig";
    }

    private const string Refusal = "{\"error\":\"Two-factor setup required\",\"errorCode\":\"TWO_FACTOR_ENROLLMENT_REQUIRED\"}";

    private static (HttpClient Client, FakeSessionManager Session, List<TwoFactorEnrollmentRequiredEventArgs> Raised) Create(string sessionUser)
    {
        var session = new FakeSessionManager(Jwt(sessionUser));
        var services = new ServiceCollection().AddSingleton<IWildwoodSessionManager>(session).BuildServiceProvider();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { RequestServices = services } };
        var events = new WildwoodApiEvents();
        var raised = new List<TwoFactorEnrollmentRequiredEventArgs>();
        events.TwoFactorEnrollmentRequired += (_, e) => raised.Add(e);

        var inner = new FakeHttpMessageHandler().When("api/", HttpStatusCode.Forbidden, Refusal);
        var handler = new WildwoodTwoFactorEnrollmentHandler(accessor, events) { InnerHandler = inner };
        return (new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") }, session, raised);
    }

    private static HttpRequestMessage Get(string bearerSub)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/notifications");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Jwt(bearerSub));
        return request;
    }

    [Fact]
    public async Task Marks_the_session_whose_own_token_was_refused()
    {
        var (client, session, raised) = Create("user-1");

        var response = await client.SendAsync(Get("user-1"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("TWO_FACTOR_ENROLLMENT_REQUIRED", await response.Content.ReadAsStringAsync());
        Assert.True(session.IsTwoFactorEnrollmentPending);
        Assert.Equal("user-1", Assert.Single(raised).UserId);
    }

    [Fact]
    public async Task Leaves_the_session_alone_when_someone_elses_token_was_refused()
    {
        var (client, session, raised) = Create("user-1");

        await client.SendAsync(Get("user-2"));

        Assert.False(session.IsTwoFactorEnrollmentPending);
        Assert.Single(raised);
    }
}
