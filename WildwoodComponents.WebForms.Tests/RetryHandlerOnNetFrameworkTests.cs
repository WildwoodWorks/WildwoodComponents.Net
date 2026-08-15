using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WildwoodComponents.Shared.Http;
using WildwoodComponents.WebForms.Tests.TestHelpers;
using Xunit;

namespace WildwoodComponents.WebForms.Tests
{
    /// <summary>
    /// Covers the request-replay path that only exists on this target framework.
    /// </summary>
    /// <remarks>
    /// On .NET Framework, <c>HttpClient.SendAsync</c> marks a request as sent and refuses
    /// to send that same message again ("The request message was already sent"), and
    /// disposes its content afterwards. The retry handler sits below that check — it calls
    /// the inner handler directly — so reuse happens to work there today. It clones anyway,
    /// which is what keeps the replay from depending on where in the pipeline the handler
    /// is placed, and matches the behaviour on net10.0 where the restriction is gone.
    /// <para>
    /// Because reuse and cloning are indistinguishable by method, URL, headers and body,
    /// the test that guards this asserts message IDENTITY: it fails if the clone is
    /// dropped. The existing suite runs on net10.0 where the code is compiled out, so
    /// without these tests the branch would ship unexercised.
    /// </para>
    /// </remarks>
    public class RetryHandlerOnNetFrameworkTests
    {
        private static HttpClient Create(FakeHttpMessageHandler inner, int maxAttempts = 3)
        {
            var retry = new WildwoodRetryHandler(
                maxAttempts,
                logger: null,
                delay: (span, token) => Task.FromResult(0))
            {
                InnerHandler = inner
            };

            return new HttpClient(retry) { BaseAddress = new System.Uri("https://api.example.test/api/") };
        }

        [Fact]
        public async Task A_GET_that_fails_once_is_replayed_and_succeeds()
        {
            var inner = new FakeHttpMessageHandler()
                .WhenFailingThenOk("things", HttpStatusCode.InternalServerError, 1, "{\"ok\":true}");
            var client = Create(inner);

            var response = await client.GetAsync("things");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, inner.CountFor("things"));
            Assert.Contains("\"ok\":true", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Each_attempt_sends_a_separate_request_object()
        {
            // The assertion that actually pins the clone down. Everything else about a
            // replay looks the same whether the handler copies the message or resends it,
            // so this test — and only this test — fails if the clone is removed.
            var inner = new FakeHttpMessageHandler()
                .WhenFailingThenOk("things", HttpStatusCode.InternalServerError, 1, "{}");
            var client = Create(inner);

            await client.GetAsync("things");

            Assert.Equal(2, inner.CountFor("things"));
            Assert.Equal(2, inner.DistinctInstancesFor("things"));
        }

        [Fact]
        public async Task A_replayed_body_survives_the_original_content_being_consumed()
        {
            // The body is buffered before the first attempt, so a second attempt does not
            // depend on the original content still being readable — which it would not be
            // if HttpClient had disposed it, the behaviour this framework has.
            var inner = new FakeHttpMessageHandler()
                .WhenFailingThenOk("things", HttpStatusCode.InternalServerError, 2, "{}");
            var client = Create(inner);

            var content = new StringContent("{\"name\":\"value\"}", Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, "things") { Content = content };

            await client.SendAsync(request);

            Assert.Equal(3, inner.CountFor("things"));
            Assert.Equal(3, inner.DistinctInstancesFor("things"));
            foreach (var recorded in inner.Requests)
            {
                Assert.Equal("{\"name\":\"value\"}", recorded.Body);
            }
        }

        [Fact]
        public async Task A_request_that_can_never_be_retried_is_passed_through_untouched()
        {
            // POST is not replayable, so the handler does not buffer or copy it. Comparing
            // against the caller's own message is what makes that observable: a count alone
            // cannot tell "sent the original" from "sent one copy".
            var inner = new FakeHttpMessageHandler().WhenOk("things", "{}");
            var client = Create(inner);

            var request = new HttpRequestMessage(HttpMethod.Post, "things")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };

            await client.SendAsync(request);

            Assert.Same(request, inner.Single("things").Instance);
        }

        [Fact]
        public async Task A_retryable_request_is_copied_even_on_its_first_attempt()
        {
            // The buffer is taken before attempt one, so every attempt of a retryable
            // request — including the first — is a copy rather than the caller's message.
            var inner = new FakeHttpMessageHandler().WhenOk("things", "{}");
            var client = Create(inner);

            var request = new HttpRequestMessage(HttpMethod.Get, "things");

            await client.SendAsync(request);

            Assert.NotSame(request, inner.Single("things").Instance);
        }

        [Fact]
        public async Task A_retried_request_keeps_its_headers_on_every_attempt()
        {
            // The clone must carry the Authorization header, or a retry would arrive
            // unauthenticated and turn a transient 500 into a 401.
            var inner = new FakeHttpMessageHandler()
                .WhenFailingThenOk("things", HttpStatusCode.ServiceUnavailable, 1, "{}");
            var client = Create(inner);

            var request = new HttpRequestMessage(HttpMethod.Get, "things");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "tok");

            await client.SendAsync(request);

            Assert.Equal(2, inner.CountFor("things"));
            foreach (var recorded in inner.Requests)
            {
                Assert.Equal("Bearer tok", recorded.Authorization);
            }
        }

        [Fact]
        public async Task A_retried_request_with_a_body_replays_that_body()
        {
            // PUT is idempotent, so it is retryable and its content must survive the replay
            // even though the framework disposes content once sent.
            var inner = new FakeHttpMessageHandler()
                .WhenFailingThenOk("things", HttpStatusCode.InternalServerError, 1, "{}");
            var client = Create(inner);

            var request = new HttpRequestMessage(HttpMethod.Put, "things")
            {
                Content = new StringContent("{\"name\":\"value\"}", Encoding.UTF8, "application/json")
            };

            await client.SendAsync(request);

            Assert.Equal(2, inner.CountFor("things"));
            foreach (var recorded in inner.Requests)
            {
                Assert.Equal("{\"name\":\"value\"}", recorded.Body);
            }
        }

        [Fact]
        public async Task A_POST_is_never_replayed()
        {
            // Neither a 5xx nor a network error says whether the server already applied it.
            var inner = new FakeHttpMessageHandler()
                .When("things", HttpStatusCode.InternalServerError, "{}");
            var client = Create(inner);

            var response = await client.PostAsync("things", new StringContent("{}", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(1, inner.CountFor("things"));
        }

        [Fact]
        public async Task Attempts_stop_at_the_configured_maximum()
        {
            var inner = new FakeHttpMessageHandler()
                .When("things", HttpStatusCode.InternalServerError, "{}");
            var client = Create(inner, maxAttempts: 3);

            var response = await client.GetAsync("things");

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(3, inner.CountFor("things"));
        }

        [Fact]
        public async Task A_successful_request_is_sent_once()
        {
            var inner = new FakeHttpMessageHandler().WhenOk("things", "{}");
            var client = Create(inner);

            await client.GetAsync("things");

            Assert.Equal(1, inner.CountFor("things"));
        }

        [Fact]
        public async Task A_4xx_is_not_retried()
        {
            var inner = new FakeHttpMessageHandler().When("things", HttpStatusCode.NotFound, "{}");
            var client = Create(inner);

            var response = await client.GetAsync("things");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(1, inner.CountFor("things"));
        }
    }
}
