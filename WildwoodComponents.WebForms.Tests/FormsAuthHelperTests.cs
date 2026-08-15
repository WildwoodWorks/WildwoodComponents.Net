using System;
using WildwoodComponents.WebForms.Authentication;
using WildwoodComponents.WebForms.Session;
using WildwoodComponents.WebForms.Tests.TestHelpers;
using Xunit;

namespace WildwoodComponents.WebForms.Tests
{
    /// <summary>
    /// Covers the ticket payload itself. Issuing and reading the actual cookie needs a
    /// machine key and a live request, so that part is exercised by the manual test site
    /// rather than here.
    /// </summary>
    public class FormsAuthHelperTests
    {
        [Fact]
        public void UserData_round_trips_all_three_values()
        {
            var expiry = new DateTime(2030, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var userData = WildwoodFormsAuthHelper.CreateUserData("access", "refresh", expiry);
            Assert.NotNull(userData);

            var session = new WildwoodSessionManager(new InMemoryTokenStore());
            var restored = WildwoodFormsAuthHelper.TryRestoreFromUserData(userData, session);

            Assert.True(restored);
            Assert.Equal("access", session.GetAccessToken());
            Assert.Equal("refresh", session.GetRefreshToken());
            Assert.Equal(expiry, session.GetTokenExpiryUtc()!.Value, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void UserData_uses_the_same_field_names_as_the_Razor_cookie_tokens()
        {
            var userData = WildwoodFormsAuthHelper.CreateUserData("access", "refresh", DateTime.UtcNow);

            Assert.NotNull(userData);
            Assert.Contains(WildwoodFormsAuthHelper.AccessTokenName, userData);
            Assert.Contains(WildwoodFormsAuthHelper.RefreshTokenName, userData);
            Assert.Contains(WildwoodFormsAuthHelper.TokenExpiryName, userData);
            Assert.Equal("ww_access_token", WildwoodFormsAuthHelper.AccessTokenName);
            Assert.Equal("ww_refresh_token", WildwoodFormsAuthHelper.RefreshTokenName);
            Assert.Equal("ww_token_expiry", WildwoodFormsAuthHelper.TokenExpiryName);
        }

        [Fact]
        public void An_unspecified_kind_expiry_is_read_as_UTC()
        {
            // Must agree with the session manager, or the ticket copy and the session copy
            // of one expiry would disagree after a session loss.
            var unspecified = new DateTime(2030, 6, 1, 12, 0, 0, DateTimeKind.Unspecified);
            var userData = WildwoodFormsAuthHelper.CreateUserData("access", "refresh", unspecified);

            var session = new WildwoodSessionManager(new InMemoryTokenStore());
            WildwoodFormsAuthHelper.TryRestoreFromUserData(userData, session);

            Assert.Equal(
                new DateTime(2030, 6, 1, 12, 0, 0, DateTimeKind.Utc),
                session.GetTokenExpiryUtc()!.Value,
                TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void An_oversized_payload_drops_the_refresh_token_first()
        {
            // Classic Forms Authentication does not chunk an oversized cookie across
            // several; it emits one the browser then silently refuses. Losing silent
            // renewal is better than losing the sign-in.
            var access = new string('a', WildwoodFormsAuthHelper.MaxUserDataLength - 200);
            var refresh = new string('r', 500);

            var userData = WildwoodFormsAuthHelper.CreateUserData(access, refresh, DateTime.UtcNow);

            Assert.NotNull(userData);
            Assert.True(userData!.Length <= WildwoodFormsAuthHelper.MaxUserDataLength);
            Assert.DoesNotContain(refresh, userData);
            Assert.Contains(access, userData);
        }

        [Fact]
        public void A_payload_too_large_even_alone_is_refused()
        {
            var access = new string('a', WildwoodFormsAuthHelper.MaxUserDataLength + 100);

            var userData = WildwoodFormsAuthHelper.CreateUserData(access, null, DateTime.UtcNow);

            // Null means "no backup written": the sign-in still works through session state.
            Assert.Null(userData);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json at all")]
        [InlineData("{}")]
        [InlineData("{\"ww_access_token\":\"a\"}")]                       // no expiry
        [InlineData("{\"ww_token_expiry\":\"2030-01-01T00:00:00Z\"}")]    // no token
        [InlineData("{\"ww_access_token\":\"a\",\"ww_token_expiry\":\"nonsense\"}")]
        public void A_payload_this_helper_did_not_write_is_ignored(string? userData)
        {
            // A host may keep its own UserData in the ticket; that must not throw here.
            var session = new WildwoodSessionManager(new InMemoryTokenStore());

            Assert.False(WildwoodFormsAuthHelper.TryRestoreFromUserData(userData, session));
            Assert.Null(session.GetAccessToken());
        }

        [Fact]
        public void CreateUserData_refuses_an_empty_access_token()
        {
            Assert.Null(WildwoodFormsAuthHelper.CreateUserData(string.Empty, "refresh", DateTime.UtcNow));
        }
    }
}
