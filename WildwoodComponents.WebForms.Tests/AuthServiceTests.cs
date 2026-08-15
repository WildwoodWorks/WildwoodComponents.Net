using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using WildwoodComponents.WebForms.Models;
using WildwoodComponents.WebForms.Services;
using WildwoodComponents.WebForms.Session;
using WildwoodComponents.WebForms.Tests.TestHelpers;
using Xunit;

namespace WildwoodComponents.WebForms.Tests
{
    public class AuthServiceTests
    {
        private const string LoginOk =
            "{\"jwtToken\":\"jwt-access\",\"refreshToken\":\"jwt-refresh\",\"id\":\"user-1\"," +
            "\"email\":\"a@b.test\",\"firstName\":\"Ada\",\"lastName\":\"Lovelace\"," +
            "\"requiresTwoFactor\":false}";

        private const string LoginNeedsTwoFactor =
            "{\"jwtToken\":\"jwt-access\",\"refreshToken\":\"jwt-refresh\",\"id\":\"user-1\"," +
            "\"email\":\"a@b.test\",\"requiresTwoFactor\":true,\"twoFactorSessionId\":\"2fa-session\"}";

        private static WildwoodAuthService Create(
            FakeHttpMessageHandler handler,
            out IWildwoodSessionManager session,
            out HttpClient client)
        {
            client = handler.CreateClient();
            session = new WildwoodSessionManager(new InMemoryTokenStore());
            return new WildwoodAuthService(client, session, null, "app-1");
        }

        [Fact]
        public async Task Login_posts_to_the_same_endpoint_as_the_Razor_service()
        {
            var handler = new FakeHttpMessageHandler().WhenOk("auth/login", LoginOk);
            IWildwoodSessionManager session;
            HttpClient client;
            var service = Create(handler, out session, out client);

            await service.LoginAsync(new LoginRequest { Username = "ada", Password = "pw" });

            var request = handler.Single("auth/login");
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.example.test/api/auth/login", request.Url);
        }

        [Fact]
        public async Task Login_serialises_the_body_as_camelCase()
        {
            // Matches what the Razor package's PostAsJsonAsync produces.
            var handler = new FakeHttpMessageHandler().WhenOk("auth/login", LoginOk);
            IWildwoodSessionManager session;
            HttpClient client;
            var service = Create(handler, out session, out client);

            await service.LoginAsync(new LoginRequest { Username = "ada", Password = "pw" });

            var body = handler.Single("auth/login").Body;
            Assert.NotNull(body);
            Assert.Contains("\"username\":\"ada\"", body);
            Assert.Contains("\"appId\":\"app-1\"", body);
        }

        [Fact]
        public async Task A_successful_login_stores_the_tokens()
        {
            var handler = new FakeHttpMessageHandler().WhenOk("auth/login", LoginOk);
            IWildwoodSessionManager session;
            HttpClient client;
            var service = Create(handler, out session, out client);

            var result = await service.LoginAsync(new LoginRequest { Username = "ada", Password = "pw" });

            Assert.True(result.Succeeded);
            Assert.Equal("jwt-access", session.GetAccessToken());
            Assert.Equal("jwt-refresh", session.GetRefreshToken());
        }

        [Fact]
        public async Task A_login_still_needing_two_factor_stores_nothing()
        {
            // Otherwise a password alone would establish a session, defeating the second
            // factor entirely.
            var handler = new FakeHttpMessageHandler().WhenOk("auth/login", LoginNeedsTwoFactor);
            IWildwoodSessionManager session;
            HttpClient client;
            var service = Create(handler, out session, out client);

            var result = await service.LoginAsync(new LoginRequest { Username = "ada", Password = "pw" });

            Assert.True(result.RequiresTwoFactor);
            Assert.Null(session.GetAccessToken());
            Assert.Null(session.GetRefreshToken());
            Assert.False(session.IsAuthenticated);
        }

        [Fact]
        public async Task Verifying_the_second_factor_completes_the_sign_in()
        {
            var handler = new FakeHttpMessageHandler()
                .WhenOk("twofactor/verify", "{\"success\":true,\"authResponse\":" + LoginOk + "}");
            IWildwoodSessionManager session;
            HttpClient client;
            var service = Create(handler, out session, out client);

            var result = await service.VerifyTwoFactorAsync(new TwoFactorVerifyRequest { Code = "123456" });

            Assert.True(result.Succeeded);
            Assert.Equal("jwt-access", session.GetAccessToken());
        }

        [Fact]
        public async Task A_rejected_login_reports_the_API_message()
        {
            var handler = new FakeHttpMessageHandler()
                .When("auth/login", HttpStatusCode.Unauthorized, "{\"message\":\"Invalid credentials\"}");
            IWildwoodSessionManager session;
            HttpClient client;
            var service = Create(handler, out session, out client);

            var result = await service.LoginAsync(new LoginRequest { Username = "ada", Password = "wrong" });

            Assert.False(result.Succeeded);
            Assert.Equal("Invalid credentials", result.ErrorMessage);
            Assert.Null(session.GetAccessToken());
        }

        [Fact]
        public async Task An_html_error_page_does_not_become_an_exception()
        {
            var handler = new FakeHttpMessageHandler()
                .When("auth/login", HttpStatusCode.BadGateway, "<html><body>502</body></html>");
            IWildwoodSessionManager session;
            HttpClient client;
            var service = Create(handler, out session, out client);

            var result = await service.LoginAsync(new LoginRequest { Username = "ada", Password = "pw" });

            Assert.False(result.Succeeded);
            Assert.NotNull(result.ErrorMessage);
        }

        [Fact]
        public async Task The_bearer_token_goes_on_the_request_and_never_on_the_shared_client()
        {
            // This package shares one HttpClient for the whole application. A token on
            // DefaultRequestHeaders would be sent with other users' concurrent requests.
            var handler = new FakeHttpMessageHandler().WhenOk("twofactor/configuration", "{}");
            var client = handler.CreateClient();
            var session = new WildwoodSessionManager(new InMemoryTokenStore());
            session.SetTokens("secret-token", "refresh", DateTime.UtcNow.AddHours(1));
            var service = new WildwoodAuthService(client, session, null, "app-1");

            await service.GetAuthConfigAsync();

            Assert.Equal("Bearer secret-token", handler.Single("twofactor/configuration").Authorization);
            Assert.Null(client.DefaultRequestHeaders.Authorization);
        }

        [Fact]
        public async Task GetAuthConfig_returns_permissive_defaults_when_the_API_fails()
        {
            // An API outage should degrade to a plain login form, not an empty page.
            var handler = new FakeHttpMessageHandler()
                .When("twofactor/configuration", HttpStatusCode.InternalServerError, "{}");
            IWildwoodSessionManager session;
            HttpClient client;
            var service = Create(handler, out session, out client);

            var config = await service.GetAuthConfigAsync();

            Assert.NotNull(config);
            Assert.True(config.AllowRegistration);
            Assert.False(config.EnableTwoFactor);
        }

        [Fact]
        public async Task Logout_revokes_the_refresh_token_then_clears_the_session()
        {
            var handler = new FakeHttpMessageHandler().WhenOk("auth/revoke-token", "{}");
            IWildwoodSessionManager session;
            HttpClient client;
            var service = Create(handler, out session, out client);
            session.SetTokens("access", "refresh", DateTime.UtcNow.AddHours(1));

            await service.LogoutAsync();

            Assert.Equal(HttpMethod.Post, handler.Single("auth/revoke-token").Method);
            Assert.Null(session.GetAccessToken());
        }

        [Fact]
        public async Task Logout_clears_the_session_even_when_the_API_call_fails()
        {
            // A sign-out that cannot reach the API must still sign the user out here.
            var handler = new FakeHttpMessageHandler()
                .When("auth/revoke-token", HttpStatusCode.InternalServerError, "{}");
            IWildwoodSessionManager session;
            HttpClient client;
            var service = Create(handler, out session, out client);
            session.SetTokens("access", "refresh", DateTime.UtcNow.AddHours(1));

            await service.LogoutAsync();

            Assert.Null(session.GetAccessToken());
            Assert.False(session.IsAuthenticated);
        }

        [Fact]
        public async Task Refreshing_without_a_stored_refresh_token_makes_no_request()
        {
            var handler = new FakeHttpMessageHandler();
            IWildwoodSessionManager session;
            HttpClient client;
            var service = Create(handler, out session, out client);

            var result = await service.RefreshTokenAsync();

            Assert.False(result.Succeeded);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task A_failed_refresh_clears_the_session()
        {
            var handler = new FakeHttpMessageHandler()
                .When("auth/refresh-token", HttpStatusCode.Unauthorized, "{}");
            IWildwoodSessionManager session;
            HttpClient client;
            var service = Create(handler, out session, out client);
            session.SetTokens("access", "refresh", DateTime.UtcNow.AddHours(1));

            var result = await service.RefreshTokenAsync();

            Assert.False(result.Succeeded);
            Assert.Null(session.GetAccessToken());
        }

        [Fact]
        public async Task Forgot_password_answers_the_same_way_whether_or_not_the_account_exists()
        {
            var handler = new FakeHttpMessageHandler().WhenOk("auth/forgot-password", "{}");
            IWildwoodSessionManager session;
            HttpClient client;
            var service = Create(handler, out session, out client);

            var result = await service.ForgotPasswordAsync("nobody@example.test");

            Assert.True(result.Succeeded);
            Assert.Contains("If an account", result.Message);
        }
    }
}
