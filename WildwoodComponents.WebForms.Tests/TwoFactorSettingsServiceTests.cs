using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using WildwoodComponents.WebForms.Services;
using WildwoodComponents.WebForms.Session;
using WildwoodComponents.WebForms.Tests.TestHelpers;
using Xunit;

namespace WildwoodComponents.WebForms.Tests
{
    public class TwoFactorSettingsServiceTests
    {
        private static WildwoodTwoFactorSettingsService Create(FakeHttpMessageHandler handler)
        {
            var session = new WildwoodSessionManager(new InMemoryTokenStore());
            session.SetTokens("access", "refresh", DateTime.UtcNow.AddHours(1));
            return new WildwoodTwoFactorSettingsService(handler.CreateClient(), session, null, "app-1");
        }

        [Fact]
        public async Task Status_and_list_endpoints_match_the_Razor_service()
        {
            var handler = new FakeHttpMessageHandler()
                .WhenOk("twofactor/status", "{\"isEnabled\":true,\"methodCount\":2}")
                .WhenOk("twofactor/credentials", "[]")
                .WhenOk("twofactor/trusted-devices", "[]")
                .WhenOk("twofactor/recovery-codes/info", "{\"remaining\":5,\"totalGenerated\":10}");
            var service = Create(handler);

            await service.GetStatusAsync();
            await service.GetCredentialsAsync();
            await service.GetTrustedDevicesAsync();
            await service.GetRecoveryCodeInfoAsync();

            Assert.Equal(HttpMethod.Get, handler.Single("twofactor/status").Method);
            Assert.Equal(HttpMethod.Get, handler.Single("twofactor/credentials").Method);
            Assert.Equal(HttpMethod.Get, handler.Single("twofactor/trusted-devices").Method);
            Assert.Equal(HttpMethod.Get, handler.Single("twofactor/recovery-codes/info").Method);
        }

        [Fact]
        public async Task Enrolment_endpoints_use_the_expected_verbs_and_paths()
        {
            var handler = new FakeHttpMessageHandler()
                .WhenOk("twofactor/enroll/email/verify", "{}")
                .WhenOk("twofactor/enroll/email", "{\"success\":true,\"credentialId\":\"c1\"}")
                .WhenOk("twofactor/enroll/authenticator/verify", "{}")
                .WhenOk("twofactor/enroll/authenticator", "{\"success\":true,\"credentialId\":\"c2\"}")
                .WhenOk("twofactor/recovery-codes/regenerate", "{\"success\":true,\"codes\":[\"a\",\"b\"]}");
            var service = Create(handler);

            await service.EnrollEmailAsync("a@b.test");
            await service.VerifyEmailEnrollmentAsync("c1", "123456");
            await service.BeginAuthenticatorEnrollmentAsync("Phone");
            await service.CompleteAuthenticatorEnrollmentAsync("c2", "123456");
            await service.RegenerateRecoveryCodesAsync();

            // Asserted positionally: several of these paths are prefixes of each other, so
            // a substring lookup could not tell them apart.
            Assert.Equal(
                new[]
                {
                    "POST https://api.example.test/api/twofactor/enroll/email",
                    "POST https://api.example.test/api/twofactor/enroll/email/verify",
                    "POST https://api.example.test/api/twofactor/enroll/authenticator",
                    "POST https://api.example.test/api/twofactor/enroll/authenticator/verify",
                    "POST https://api.example.test/api/twofactor/recovery-codes/regenerate"
                },
                handler.UrlsSeen());
        }

        [Fact]
        public async Task Setting_a_primary_credential_uses_PUT_on_the_credential_path()
        {
            var handler = new FakeHttpMessageHandler().WhenOk("primary", "{}");
            var service = Create(handler);

            await service.SetPrimaryCredentialAsync("cred-1");

            var request = handler.Single("primary");
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.EndsWith("twofactor/credentials/cred-1/primary", request.Url);
        }

        [Fact]
        public async Task Removing_a_credential_uses_DELETE()
        {
            var handler = new FakeHttpMessageHandler().WhenOk("credentials/cred-1", "{}");
            var service = Create(handler);

            await service.RemoveCredentialAsync("cred-1");

            Assert.Equal(HttpMethod.Delete, handler.Single("credentials/cred-1").Method);
        }

        [Fact]
        public async Task An_id_with_a_slash_is_escaped_into_one_path_segment()
        {
            var handler = new FakeHttpMessageHandler().WhenOk("trusted-devices", "{}");
            var service = Create(handler);

            await service.RevokeTrustedDeviceAsync("a/b");

            Assert.EndsWith("twofactor/trusted-devices/a%2Fb", handler.Single("trusted-devices").Url);
        }

        [Fact]
        public async Task Revoking_all_devices_reads_the_count_out_of_the_message()
        {
            var handler = new FakeHttpMessageHandler()
                .WhenOk("trusted-devices", "{\"message\":\"3 device(s) revoked\"}");
            var service = Create(handler);

            var count = await service.RevokeAllTrustedDevicesAsync();

            Assert.Equal(3, count);
        }

        [Fact]
        public async Task Revoking_all_devices_reports_zero_when_the_message_has_no_count()
        {
            var handler = new FakeHttpMessageHandler()
                .WhenOk("trusted-devices", "{\"message\":\"done\"}");
            var service = Create(handler);

            Assert.Equal(0, await service.RevokeAllTrustedDevicesAsync());
        }

        [Fact]
        public async Task List_reads_degrade_to_empty_collections()
        {
            var handler = new FakeHttpMessageHandler()
                .When("twofactor/credentials", HttpStatusCode.InternalServerError, "{}")
                .When("twofactor/trusted-devices", HttpStatusCode.InternalServerError, "{}");
            var service = Create(handler);

            Assert.Empty(await service.GetCredentialsAsync());
            Assert.Empty(await service.GetTrustedDevicesAsync());
        }

        [Fact]
        public async Task Single_object_reads_degrade_to_null()
        {
            var handler = new FakeHttpMessageHandler()
                .When("twofactor/status", HttpStatusCode.InternalServerError, "{}");
            var service = Create(handler);

            Assert.Null(await service.GetStatusAsync());
        }

        [Fact]
        public async Task An_empty_id_is_rejected_before_a_request_is_made()
        {
            var handler = new FakeHttpMessageHandler();
            var service = Create(handler);

            await Assert.ThrowsAsync<ArgumentException>(() => service.SetPrimaryCredentialAsync(string.Empty));
            await Assert.ThrowsAsync<ArgumentException>(() => service.RemoveCredentialAsync(string.Empty));
            await Assert.ThrowsAsync<ArgumentException>(() => service.RevokeTrustedDeviceAsync(string.Empty));
            Assert.Empty(handler.Requests);
        }
    }
}
