using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using WildwoodComponents.WebForms.Models;
using WildwoodComponents.WebForms.Services;
using WildwoodComponents.WebForms.Session;
using WildwoodComponents.WebForms.Tests.TestHelpers;
using Xunit;

namespace WildwoodComponents.WebForms.Tests
{
    /// <summary>
    /// WebForms honours "two-factor enrollment required" (Phase A covers Authentication): the
    /// restriction is recorded on the session from the flag or the token claim, and completing it
    /// refreshes and reports whether it lifted. Before, a restricted user was signed in fully.
    /// </summary>
    public class TwoFactorEnrollmentTests
    {
        private static string Jwt(bool pending)
        {
            string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var claim = pending ? ",\"tfa_enroll_pending\":\"true\"" : string.Empty;
            return Encode("{\"alg\":\"none\"}") + "." + Encode("{\"sub\":\"user-1\",\"exp\":4102444800" + claim + "}") + ".sig";
        }

        private static string Auth(string jwt, bool flag = false)
        {
            return "{\"jwtToken\":\"" + jwt + "\",\"refreshToken\":\"jwt-refresh\",\"id\":\"user-1\"," +
                "\"email\":\"a@b.test\",\"requiresTwoFactor\":false,\"requiresTwoFactorEnrollment\":" +
                (flag ? "true" : "false") + "}";
        }

        private static WildwoodAuthService Create(FakeHttpMessageHandler handler, out IWildwoodSessionManager session)
        {
            session = new WildwoodSessionManager(new InMemoryTokenStore());
            return new WildwoodAuthService(handler.CreateClient(), session, null, "app-1");
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task A_restricted_sign_in_holds_the_tokens_and_records_the_restriction(bool flag, bool claim)
        {
            var handler = new FakeHttpMessageHandler().WhenOk("auth/login", Auth(Jwt(claim), flag));
            IWildwoodSessionManager session;
            var service = Create(handler, out session);

            var result = await service.LoginAsync(new LoginRequest { Username = "ada", Password = "pw" });

            Assert.True(result.Succeeded);
            Assert.True(result.Response!.RequiresTwoFactorEnrollment);
            Assert.True(session.IsTwoFactorEnrollmentPending);
            Assert.False(string.IsNullOrEmpty(session.GetAccessToken()));
        }

        [Fact]
        public async Task An_unrestricted_sign_in_is_not_pending()
        {
            var handler = new FakeHttpMessageHandler().WhenOk("auth/login", Auth(Jwt(false)));
            IWildwoodSessionManager session;
            var service = Create(handler, out session);

            var result = await service.LoginAsync(new LoginRequest { Username = "ada", Password = "pw" });

            Assert.False(result.Response!.RequiresTwoFactorEnrollment);
            Assert.False(session.IsTwoFactorEnrollmentPending);
        }

        [Fact]
        public async Task Completing_after_enrolling_lifts_the_restriction()
        {
            var handler = new FakeHttpMessageHandler()
                .WhenOk("auth/login", Auth(Jwt(true), flag: true))
                .WhenOk("auth/refresh-token", Auth(Jwt(false)));
            IWildwoodSessionManager session;
            var service = Create(handler, out session);
            await service.LoginAsync(new LoginRequest { Username = "ada", Password = "pw" });

            var result = await service.CompleteTwoFactorEnrollmentAsync();

            Assert.True(result.Succeeded);
            Assert.False(session.IsTwoFactorEnrollmentPending);
        }

        [Fact]
        public async Task Completing_while_still_restricted_fails_with_the_reason()
        {
            var handler = new FakeHttpMessageHandler()
                .WhenOk("auth/login", Auth(Jwt(true)))
                .WhenOk("auth/refresh-token", Auth(Jwt(true)));
            IWildwoodSessionManager session;
            var service = Create(handler, out session);
            await service.LoginAsync(new LoginRequest { Username = "ada", Password = "pw" });

            var result = await service.CompleteTwoFactorEnrollmentAsync();

            Assert.False(result.Succeeded);
            Assert.Equal(WildwoodAuthService.EnrollmentStillPendingMessage, result.ErrorMessage);
            Assert.True(session.IsTwoFactorEnrollmentPending);
        }

        [Fact]
        public void Clearing_the_tokens_clears_the_restriction()
        {
            var session = new WildwoodSessionManager(new InMemoryTokenStore());
            session.SetTokens("jwt", "refresh");
            session.SetTwoFactorEnrollmentPending(true);

            session.ClearTokens();

            Assert.False(session.IsTwoFactorEnrollmentPending);
        }
    }
}
