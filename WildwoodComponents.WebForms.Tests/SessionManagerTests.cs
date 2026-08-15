using System;
using System.Globalization;
using WildwoodComponents.WebForms.Session;
using WildwoodComponents.WebForms.Tests.TestHelpers;
using Xunit;

namespace WildwoodComponents.WebForms.Tests
{
    public class SessionManagerTests
    {
        private static WildwoodSessionManager Create(out InMemoryTokenStore store)
        {
            store = new InMemoryTokenStore();
            return new WildwoodSessionManager(store);
        }

        [Fact]
        public void Uses_the_same_session_keys_as_the_Razor_package()
        {
            // Both stacks must be inspectable the same way; these literals are the contract.
            Assert.Equal("WildwoodAPI_AccessToken", WildwoodSessionManager.AccessTokenKey);
            Assert.Equal("WildwoodAPI_RefreshToken", WildwoodSessionManager.RefreshTokenKey);
            Assert.Equal("WildwoodAPI_TokenExpiry", WildwoodSessionManager.TokenExpiryKey);
        }

        [Fact]
        public void SetTokens_stores_access_refresh_and_expiry()
        {
            InMemoryTokenStore store;
            var manager = Create(out store);
            var expiry = DateTime.UtcNow.AddHours(1);

            manager.SetTokens("access-token", "refresh-token", expiry);

            Assert.Equal("access-token", store.Get(WildwoodSessionManager.AccessTokenKey));
            Assert.Equal("refresh-token", store.Get(WildwoodSessionManager.RefreshTokenKey));
            Assert.NotNull(store.Get(WildwoodSessionManager.TokenExpiryKey));
        }

        [Fact]
        public void SetTokens_derives_the_expiry_from_the_JWT()
        {
            InMemoryTokenStore store;
            var manager = Create(out store);
            var expected = new DateTime(2030, 5, 4, 3, 2, 1, DateTimeKind.Utc);

            manager.SetTokens(TestJwt.WithExpiry(expected), "refresh");

            var actual = manager.GetTokenExpiryUtc();
            Assert.NotNull(actual);
            Assert.Equal(expected, actual!.Value, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void SetTokens_falls_back_to_fifteen_minutes_when_the_JWT_has_no_expiry()
        {
            InMemoryTokenStore store;
            var manager = Create(out store);

            manager.SetTokens(TestJwt.WithoutExpiry(), "refresh");

            var actual = manager.GetTokenExpiryUtc();
            Assert.NotNull(actual);
            Assert.Equal(DateTime.UtcNow.AddMinutes(15), actual!.Value, TimeSpan.FromMinutes(1));
        }

        [Fact]
        public void SetTokens_clears_a_previous_refresh_token_when_none_is_supplied()
        {
            // A re-login that returns no refresh token must not leave the previous user's
            // behind, or a later refresh would act as somebody else.
            InMemoryTokenStore store;
            var manager = Create(out store);
            manager.SetTokens("first-access", "first-refresh", DateTime.UtcNow.AddHours(1));

            manager.SetTokens("second-access", null, DateTime.UtcNow.AddHours(1));

            Assert.Null(manager.GetRefreshToken());
        }

        [Fact]
        public void An_unspecified_kind_expiry_is_read_as_UTC_not_local()
        {
            // The whole package deals in UTC. Reading an unspecified value as local time
            // would shift it by the server's offset and make sessions expire early or late.
            InMemoryTokenStore store;
            var manager = Create(out store);
            var unspecified = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

            manager.SetTokens("access", "refresh", unspecified);

            var stored = store.Get(WildwoodSessionManager.TokenExpiryKey);
            Assert.NotNull(stored);
            var parsed = DateTimeOffset.Parse(stored!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            Assert.Equal(new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc), parsed.UtcDateTime);
        }

        [Fact]
        public void A_local_kind_expiry_is_converted_rather_than_relabelled()
        {
            InMemoryTokenStore store;
            var manager = Create(out store);
            var local = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Local);

            manager.SetTokens("access", "refresh", local);

            var actual = manager.GetTokenExpiryUtc();
            Assert.NotNull(actual);
            Assert.Equal(local.ToUniversalTime(), actual!.Value, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void IsAuthenticated_is_false_without_a_token()
        {
            InMemoryTokenStore store;
            var manager = Create(out store);

            Assert.False(manager.IsAuthenticated);
        }

        [Fact]
        public void IsAuthenticated_is_true_before_the_expiry_and_false_after()
        {
            InMemoryTokenStore store;
            var manager = Create(out store);

            manager.SetTokens("access", "refresh", DateTime.UtcNow.AddMinutes(5));
            Assert.True(manager.IsAuthenticated);

            manager.SetTokens("access", "refresh", DateTime.UtcNow.AddMinutes(-5));
            Assert.False(manager.IsAuthenticated);
        }

        [Fact]
        public void IsAuthenticated_fails_open_on_an_unparseable_expiry()
        {
            // The API is the real authority; a false negative here would sign a user out
            // for no reason.
            var store = new InMemoryTokenStore();
            store.Set(WildwoodSessionManager.AccessTokenKey, "access");
            store.Set(WildwoodSessionManager.TokenExpiryKey, "not-a-date");
            var manager = new WildwoodSessionManager(store);

            Assert.True(manager.IsAuthenticated);
        }

        [Fact]
        public void ClearTokens_removes_everything()
        {
            InMemoryTokenStore store;
            var manager = Create(out store);
            manager.SetTokens("access", "refresh", DateTime.UtcNow.AddHours(1));

            manager.ClearTokens();

            Assert.Null(manager.GetAccessToken());
            Assert.Null(manager.GetRefreshToken());
            Assert.Null(manager.GetTokenExpiry());
            Assert.False(manager.IsAuthenticated);
        }

        [Fact]
        public void GetAuthorizationHeader_is_a_bearer_header_or_null()
        {
            InMemoryTokenStore store;
            var manager = Create(out store);

            Assert.Null(manager.GetAuthorizationHeader());

            manager.SetTokens("the-token", "refresh", DateTime.UtcNow.AddHours(1));

            var header = manager.GetAuthorizationHeader();
            Assert.NotNull(header);
            Assert.Equal("Bearer", header!.Scheme);
            Assert.Equal("the-token", header.Parameter);
        }

        [Fact]
        public void Writing_tokens_without_a_session_degrades_instead_of_throwing()
        {
            var store = new InMemoryTokenStore { IsAvailable = false };
            var manager = new WildwoodSessionManager(store);

            manager.SetTokens("access", "refresh", DateTime.UtcNow.AddHours(1));

            Assert.Null(manager.GetAccessToken());
            Assert.False(manager.IsAuthenticated);
        }

        [Fact]
        public void SetTokens_rejects_an_empty_access_token()
        {
            InMemoryTokenStore store;
            var manager = Create(out store);

            Assert.Throws<ArgumentException>(() => manager.SetTokens(string.Empty, "refresh"));
        }
    }
}
