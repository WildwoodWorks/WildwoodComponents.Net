using System;
using System.Net.Http;
using System.Threading.Tasks;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.WebForms.Attribution;
using WildwoodComponents.WebForms.Models;
using WildwoodComponents.WebForms.Services;
using WildwoodComponents.WebForms.Session;
using WildwoodComponents.WebForms.Tests.TestHelpers;
using Xunit;

namespace WildwoodComponents.WebForms.Tests
{
    /// <summary>
    /// Campaign Attribution on a WebForms site: the server-side capture, the consent gate the host
    /// drives, and the registration that carries the payload either way. Ported from the JS SDK's
    /// attribution and auth attach cases, which are the contract WildwoodAPI is written against.
    /// </summary>
    public class AttributionTests
    {
        private const string RegisterOk =
            "{\"jwtToken\":\"jwt-access\",\"refreshToken\":\"jwt-refresh\",\"id\":\"user-1\"," +
            "\"email\":\"a@b.test\",\"requiresTwoFactor\":false}";

        private const string Landing =
            "https://app.example.test/pricing?utm_source=Reddit&utm_medium=paid&utm_campaign=spring";

        private static WildwoodAuthService CreateService(
            FakeHttpMessageHandler handler,
            WildwoodAttributionStore? attribution)
        {
            return new WildwoodAuthService(
                handler.CreateClient(),
                new WildwoodSessionManager(new InMemoryTokenStore()),
                null,
                "app-1",
                "1.0.0",
                attribution);
        }

        private static RegisterRequest Registration()
        {
            return new RegisterRequest
            {
                Email = "a@b.test",
                Password = "Passw0rd!",
                ConfirmPassword = "Passw0rd!"
            };
        }

        // ── capture ─────────────────────────────────────────────────────────────

        [Fact]
        public void Capture_reads_the_utm_tags_of_the_landing_request()
        {
            var store = new WildwoodAttributionStore(new InMemoryTokenStore());

            var touch = store.Capture(Landing, null);

            Assert.NotNull(touch);
            Assert.Equal("reddit", touch!.Source);
            Assert.Equal("spring", touch.Campaign);

            var payload = store.GetForRegistration();
            Assert.NotNull(payload);
            Assert.Equal("reddit", payload!.LastTouch!.Source);
            Assert.Equal("dotnet", payload.Sdk);
            Assert.Equal(1, payload.Version);
            Assert.True(AttributionRules.IsValidVisitorKey(payload.VisitorKey));
        }

        [Fact]
        public void Capture_keeps_the_first_touch_and_moves_the_last_one()
        {
            var store = new WildwoodAttributionStore(new InMemoryTokenStore());

            store.Capture(Landing, null);
            store.Capture("https://app.example.test/?utm_source=google&utm_medium=cpc", null);

            var payload = store.GetForRegistration();
            Assert.Equal("reddit", payload!.FirstTouch!.Source);
            Assert.Equal("google", payload.LastTouch!.Source);
        }

        [Fact]
        public void A_direct_visit_never_overwrites_what_a_campaign_captured()
        {
            var store = new WildwoodAttributionStore(new InMemoryTokenStore());
            store.Capture(Landing, null);

            Assert.Null(store.Capture("https://app.example.test/pricing", null));

            Assert.Equal("reddit", store.GetForRegistration()!.LastTouch!.Source);
        }

        [Fact]
        public void The_visitor_key_survives_across_requests()
        {
            // One store per request in production, all over the same session.
            var session = new InMemoryTokenStore();
            new WildwoodAttributionStore(session).Capture(Landing, null);
            var first = new WildwoodAttributionStore(session).GetForRegistration();

            new WildwoodAttributionStore(session).Capture("https://app.example.test/?utm_source=google", null);
            var second = new WildwoodAttributionStore(session).GetForRegistration();

            Assert.Equal(first!.VisitorKey, second!.VisitorKey);
        }

        [Fact]
        public void Nothing_captured_means_no_payload_and_no_stored_blob()
        {
            var session = new InMemoryTokenStore();
            var store = new WildwoodAttributionStore(session);

            Assert.Null(store.Capture("https://app.example.test/pricing", null));

            Assert.Null(store.GetForRegistration());
            Assert.Null(session.Get(WildwoodStorageKeys.Attribution));
        }

        [Fact]
        public void The_blob_is_kept_under_the_SDKs_own_storage_key()
        {
            // Same key as the browser engines and @wildwood/core; the parity check hard-fails on drift.
            var session = new InMemoryTokenStore();

            new WildwoodAttributionStore(session).Capture(Landing, null);

            Assert.NotNull(session.Get("ww_attribution"));
        }

        [Fact]
        public void A_request_without_session_state_degrades_instead_of_throwing()
        {
            var session = new InMemoryTokenStore { IsAvailable = false };
            var store = new WildwoodAttributionStore(session);

            var touch = store.Capture(Landing, null);

            Assert.NotNull(touch);
            Assert.Null(store.GetForRegistration());
        }

        // ── the consent gate ────────────────────────────────────────────────────

        [Fact]
        public void An_undecided_visitor_is_still_captured_for_the_visit()
        {
            // The JS SDK's default while a visitor has not answered: held, but nothing new is written
            // to the visitor's device. Session state rides the site's existing session cookie.
            var store = new WildwoodAttributionStore(
                new InMemoryTokenStore(),
                () => WildwoodAttributionConsent.Undecided);

            store.Capture(Landing, null);

            Assert.NotNull(store.GetForRegistration());
        }

        [Fact]
        public void A_granted_visitor_is_captured()
        {
            var store = new WildwoodAttributionStore(
                new InMemoryTokenStore(),
                () => WildwoodAttributionConsent.Granted);

            store.Capture(Landing, null);

            Assert.NotNull(store.GetForRegistration());
        }

        [Fact]
        public void A_withdrawal_drops_what_was_already_held_and_captures_nothing_further()
        {
            var consent = WildwoodAttributionConsent.Granted;
            var session = new InMemoryTokenStore();
            var store = new WildwoodAttributionStore(session, () => consent);
            store.Capture(Landing, null);
            Assert.NotNull(store.GetForRegistration());

            consent = WildwoodAttributionConsent.Denied;
            Assert.Null(store.Capture("https://app.example.test/?utm_source=google", null));

            Assert.Null(store.GetForRegistration());
            Assert.Null(session.Get(WildwoodStorageKeys.Attribution));
        }

        [Fact]
        public void A_consent_delegate_that_throws_is_not_read_as_a_grant_and_does_not_break_capture()
        {
            var store = new WildwoodAttributionStore(
                new InMemoryTokenStore(),
                () => throw new InvalidOperationException("host bug"));

            var touch = store.Capture(Landing, null);

            Assert.NotNull(touch);
        }

        // ── registration carries it ─────────────────────────────────────────────

        [Fact]
        public async Task Register_attaches_what_the_server_captured()
        {
            var handler = new FakeHttpMessageHandler().WhenOk("auth/register", RegisterOk);
            var store = new WildwoodAttributionStore(new InMemoryTokenStore());
            store.Capture(Landing, null);

            await CreateService(handler, store).RegisterAsync(Registration());

            var body = handler.Single("auth/register").Body;
            Assert.NotNull(body);
            Assert.Contains("\"attribution\":", body);
            Assert.Contains("\"source\":\"reddit\"", body);
            Assert.Contains("\"sdk\":\"dotnet\"", body);
        }

        [Fact]
        public async Task Register_prefers_the_payload_the_browser_engine_posted()
        {
            // attribution.js captured the landing; the server may have captured nothing (no
            // Application_AcquireRequestState hook) or something older. The caller's payload wins.
            var handler = new FakeHttpMessageHandler().WhenOk("auth/register", RegisterOk);
            var store = new WildwoodAttributionStore(new InMemoryTokenStore());
            store.Capture(Landing, null);

            var request = Registration();
            request.Attribution = new AttributionPayloadModel
            {
                VisitorKey = "browser-key-0001",
                LastTouch = new AttributionTouchModel { Source = "google", OccurredAt = "2026-09-13T12:00:00.000Z" }
            };

            await CreateService(handler, store).RegisterAsync(request);

            var body = handler.Single("auth/register").Body;
            Assert.Contains("\"visitorKey\":\"browser-key-0001\"", body);
            Assert.DoesNotContain("\"source\":\"reddit\"", body);
        }

        [Fact]
        public async Task Register_omits_the_key_entirely_when_nothing_was_captured()
        {
            var handler = new FakeHttpMessageHandler().WhenOk("auth/register", RegisterOk);

            await CreateService(handler, new WildwoodAttributionStore(new InMemoryTokenStore()))
                .RegisterAsync(Registration());

            Assert.DoesNotContain("attribution", handler.Single("auth/register").Body);
        }

        [Fact]
        public async Task Register_works_when_attribution_is_not_wired_up_at_all()
        {
            var handler = new FakeHttpMessageHandler().WhenOk("auth/register", RegisterOk);

            var result = await CreateService(handler, attribution: null).RegisterAsync(Registration());

            Assert.True(result.Succeeded);
            Assert.DoesNotContain("attribution", handler.Single("auth/register").Body);
        }

        [Fact]
        public async Task A_recorded_signup_drops_the_touches()
        {
            var handler = new FakeHttpMessageHandler().WhenOk("auth/register", RegisterOk);
            var session = new InMemoryTokenStore();
            var store = new WildwoodAttributionStore(session);
            store.Capture(Landing, null);

            await CreateService(handler, store).RegisterAsync(Registration());

            Assert.Null(store.GetForRegistration());
            Assert.Null(session.Get(WildwoodStorageKeys.Attribution));
        }

        [Fact]
        public async Task A_failed_signup_keeps_the_touches_for_the_retry()
        {
            var handler = new FakeHttpMessageHandler()
                .When("auth/register", System.Net.HttpStatusCode.BadRequest, "{\"message\":\"Email already registered\"}");
            var store = new WildwoodAttributionStore(new InMemoryTokenStore());
            store.Capture(Landing, null);

            var result = await CreateService(handler, store).RegisterAsync(Registration());

            Assert.False(result.Succeeded);
            Assert.NotNull(store.GetForRegistration());
        }

        [Fact]
        public void Capture_without_a_request_returns_null_rather_than_throwing()
        {
            Assert.Null(WildwoodAttribution.Capture(null));
        }
    }
}
