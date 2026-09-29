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

        // ── funnel events (server-side Track) ───────────────────────────────────

        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void A_funnel_event_carries_the_visitor_the_session_and_the_current_touch()
        {
            var store = new WildwoodAttributionStore(new InMemoryTokenStore());
            store.Capture(Landing, null);

            var body = store.CreateEventsRequest("app-1", "cta_click", " hero ", null, "/pricing?x=1", "Mobile", Now);

            Assert.NotNull(body);
            Assert.Equal("app-1", body!.AppId);
            Assert.Equal(store.GetForRegistration()!.VisitorKey, body.VisitorKey);
            Assert.True(AttributionRules.IsValidVisitorKey(body.SessionKey));
            Assert.False(body.IsReturning);
            Assert.Equal("mobile", body.DeviceClass);
            Assert.Equal("web", body.Platform);
            Assert.Equal("reddit", body.Touch!.Source);
            var only = Assert.Single(body.Events);
            Assert.Equal("cta_click", only.Name);
            Assert.Equal("hero", only.Label);
            Assert.Equal("/pricing", only.Path);
            Assert.Equal("2026-09-28T12:00:00.000Z", only.ClientTimestamp);
        }

        [Fact]
        public void The_session_continues_under_thirty_minutes_and_rolls_over_after()
        {
            var session = new InMemoryTokenStore();
            var first = new WildwoodAttributionStore(session).CreateEventsRequest("app-1", "page_view", nowUtc: Now);
            var soon = new WildwoodAttributionStore(session).CreateEventsRequest("app-1", "page_view", nowUtc: Now.AddMinutes(29));
            var later = new WildwoodAttributionStore(session).CreateEventsRequest("app-1", "page_view", nowUtc: Now.AddMinutes(60));

            Assert.Equal(first!.SessionKey, soon!.SessionKey);
            Assert.NotEqual(first.SessionKey, later!.SessionKey);
            Assert.Equal(first.VisitorKey, later.VisitorKey);
            Assert.False(first.IsReturning);
            Assert.True(later.IsReturning);
            Assert.Null(first.Touch);
            Assert.Null(first.DeviceClass);
        }

        [Fact]
        public void Server_only_and_malformed_names_and_refused_values_send_nothing()
        {
            var store = new WildwoodAttributionStore(new InMemoryTokenStore());

            Assert.Null(store.CreateEventsRequest("app-1", "purchase"));
            Assert.Null(store.CreateEventsRequest("app-1", "signup_complete"));
            Assert.Null(store.CreateEventsRequest("app-1", "Bad Name"));
            Assert.Null(store.CreateEventsRequest("app-1", "cta_click"));
            Assert.Null(store.CreateEventsRequest("app-1", "scroll_depth", value: 33));
            Assert.Null(store.CreateEventsRequest("", "page_view"));
            Assert.NotNull(store.CreateEventsRequest("app-1", "demo_booked", value: 1));
        }

        [Fact]
        public void A_signup_error_label_is_reduced_to_a_category()
        {
            var store = new WildwoodAttributionStore(new InMemoryTokenStore());

            var body = store.CreateEventsRequest("app-1", "signup_error", "USERNAME EXISTS!");

            Assert.Equal("username_exists", body!.Events[0].Label);
        }

        [Fact]
        public void A_declined_visitor_sends_nothing_and_drops_what_was_held()
        {
            var session = new InMemoryTokenStore();
            new WildwoodAttributionStore(session).Capture(Landing, null);
            var store = new WildwoodAttributionStore(session, () => WildwoodAttributionConsent.Denied);

            Assert.Null(store.CreateEventsRequest("app-1", "page_view"));
            Assert.Null(session.Get(WildwoodStorageKeys.Attribution));
        }

        [Fact]
        public void Registration_carries_the_funnel_session()
        {
            var session = new InMemoryTokenStore();
            var store = new WildwoodAttributionStore(session);
            store.Capture(Landing, null);
            var tracked = store.CreateEventsRequest("app-1", "signup_view");

            var payload = store.GetForRegistration();

            Assert.Equal(tracked!.SessionKey, payload!.SessionKey);
            Assert.Equal(1, payload.SessionCount);
            Assert.Null(payload.DeviceClass);
        }

        [Fact]
        public async Task SendEvents_posts_the_body_to_the_events_endpoint()
        {
            var handler = new FakeHttpMessageHandler();
            var store = new WildwoodAttributionStore(new InMemoryTokenStore());
            var body = store.CreateEventsRequest("app 1", "cta_click", "hero");

            var sent = await WildwoodAttribution.SendEventsAsync(handler.CreateClient(), body!);

            Assert.True(sent);
            var request = handler.Single("attribution/events");
            Assert.Equal("https://api.example.test/api/attribution/events?appId=app%201", request.Instance.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("\"events\":[{\"name\":\"cta_click\",\"label\":\"hero\"", request.Body);
            Assert.DoesNotContain("\"deviceClass\"", request.Body);
        }

        [Fact]
        public async Task SendEvents_answers_false_on_an_error_status_and_never_throws()
        {
            var handler = new FakeHttpMessageHandler { DefaultStatus = (System.Net.HttpStatusCode)429 };
            var store = new WildwoodAttributionStore(new InMemoryTokenStore());
            var body = store.CreateEventsRequest("app-1", "page_view");

            Assert.False(await WildwoodAttribution.SendEventsAsync(handler.CreateClient(), body!));
            Assert.False(await WildwoodAttribution.SendEventsAsync(null!, body!));
        }

        private const string FunnelOnConfig =
            "{\"appId\":\"cfg-app\",\"isEnabled\":true,\"funnelTrackingEnabled\":true,\"trackSignupSteps\":false,\"customEventNames\":[\"demo_booked\"]}";

        [Fact]
        public async Task SendIfAccepted_sends_only_what_the_apps_config_accepts()
        {
            // @wildwood/core sends nothing the config does not accept; WebForms used to post regardless.
            WildwoodAttribution.ResetConfigCache();
            var handler = new FakeHttpMessageHandler().WhenOk("attribution/config", FunnelOnConfig);
            var client = handler.CreateClient();
            var store = new WildwoodAttributionStore(new InMemoryTokenStore());

            Assert.True(await WildwoodAttribution.SendIfAcceptedAsync(client, store.CreateEventsRequest("cfg-app", "cta_click", "hero")!));
            Assert.True(await WildwoodAttribution.SendIfAcceptedAsync(client, store.CreateEventsRequest("cfg-app", "demo_booked")!));
            Assert.False(await WildwoodAttribution.SendIfAcceptedAsync(client, store.CreateEventsRequest("cfg-app", "other_custom")!));
            Assert.False(await WildwoodAttribution.SendIfAcceptedAsync(client, store.CreateEventsRequest("cfg-app", "signup_view")!));

            Assert.Equal(2, handler.CountFor("attribution/events"));
            // Loaded once, then served from the cache.
            Assert.Equal(1, handler.CountFor("attribution/config"));
            Assert.Equal("GET https://api.example.test/api/attribution/config?appId=cfg-app", handler.UrlsSeen()[0]);
        }

        [Fact]
        public async Task SendIfAccepted_sends_nothing_when_funnel_tracking_is_off_or_the_config_fails()
        {
            WildwoodAttribution.ResetConfigCache();
            var off = new FakeHttpMessageHandler().WhenOk("attribution/config", "{\"isEnabled\":true,\"funnelTrackingEnabled\":false}");
            var store = new WildwoodAttributionStore(new InMemoryTokenStore());
            Assert.False(await WildwoodAttribution.SendIfAcceptedAsync(off.CreateClient(), store.CreateEventsRequest("off-app", "page_view")!));
            Assert.Equal(0, off.CountFor("attribution/events"));

            var failing = new FakeHttpMessageHandler().When("attribution/config", System.Net.HttpStatusCode.InternalServerError, "{}");
            var client = failing.CreateClient();
            Assert.False(await WildwoodAttribution.SendIfAcceptedAsync(client, store.CreateEventsRequest("down-app", "page_view")!));
            Assert.False(await WildwoodAttribution.SendIfAcceptedAsync(client, store.CreateEventsRequest("down-app", "page_view")!));
            Assert.Equal(0, failing.CountFor("attribution/events"));
            // A failure is never cached: the second event asked again.
            Assert.Equal(2, failing.CountFor("attribution/config"));
        }

        [Fact]
        public async Task Track_without_a_request_sends_nothing_rather_than_throwing()
        {
            Assert.False(WildwoodAttribution.Track(null, "page_view"));
            Assert.False(await WildwoodAttribution.TrackAsync(null, "page_view"));
        }
    }
}
