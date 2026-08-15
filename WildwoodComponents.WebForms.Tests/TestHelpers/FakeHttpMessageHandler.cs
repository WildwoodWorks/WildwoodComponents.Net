using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WildwoodComponents.WebForms.Tests.TestHelpers
{
    /// <summary>
    /// Records every request and answers with canned responses matched by URL substring.
    /// Deliberately a net48-native rewrite of the equivalent helper in
    /// WildwoodComponents.Tests: that one uses records and .NET 5+ overloads that will not
    /// compile on this target framework.
    /// </summary>
    public sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly List<Canned> _responses = new List<Canned>();

        /// <summary>Every request seen, in order.</summary>
        public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

        /// <summary>Status returned when no canned response matches.</summary>
        public HttpStatusCode DefaultStatus { get; set; } = HttpStatusCode.OK;

        /// <summary>Body returned when no canned response matches.</summary>
        public string DefaultJson { get; set; } = "{}";

        /// <summary>Adds a canned response for URLs containing <paramref name="urlContains"/>.</summary>
        public FakeHttpMessageHandler When(string urlContains, HttpStatusCode status, string json)
        {
            _responses.Add(new Canned(urlContains, status, json, null));
            return this;
        }

        /// <summary>Adds a canned 200 response.</summary>
        public FakeHttpMessageHandler WhenOk(string urlContains, string json)
        {
            return When(urlContains, HttpStatusCode.OK, json);
        }

        /// <summary>
        /// Answers with <paramref name="failStatus"/> for the first
        /// <paramref name="failures"/> matching requests and then with a 200 carrying
        /// <paramref name="json"/>. Used to exercise the retry path.
        /// </summary>
        public FakeHttpMessageHandler WhenFailingThenOk(
            string urlContains, HttpStatusCode failStatus, int failures, string json)
        {
            _responses.Add(new Canned(urlContains, failStatus, json, failures));
            return this;
        }

        /// <summary>How many requests were made to URLs containing the given text.</summary>
        public int CountFor(string urlContains)
        {
            var count = 0;
            foreach (var request in Requests)
            {
                if (request.Url.IndexOf(urlContains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// How many DISTINCT <see cref="HttpRequestMessage"/> instances were seen for URLs
        /// containing the given text.
        /// </summary>
        /// <remarks>
        /// This is what makes a replay observable. Sending the same message object twice
        /// and sending two copies of it look identical from the outside — same method, URL,
        /// headers and body — so only object identity distinguishes a handler that clones
        /// per attempt from one that reuses the original.
        /// </remarks>
        public int DistinctInstancesFor(string urlContains)
        {
            var seen = new List<HttpRequestMessage>();
            foreach (var request in Requests)
            {
                if (request.Url.IndexOf(urlContains, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var known = false;
                foreach (var instance in seen)
                {
                    if (ReferenceEquals(instance, request.Instance))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    seen.Add(request.Instance);
                }
            }

            return seen.Count;
        }

        /// <summary>The single request whose URL contains the given text.</summary>
        public RecordedRequest Single(string urlContains)
        {
            RecordedRequest? found = null;
            foreach (var request in Requests)
            {
                if (request.Url.IndexOf(urlContains, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (found != null)
                {
                    throw new InvalidOperationException("More than one request matched '" + urlContains + "'.");
                }

                found = request;
            }

            if (found == null)
            {
                throw new InvalidOperationException(
                    "No request matched '" + urlContains + "'. Seen: " + string.Join(", ", UrlsSeen()));
            }

            return found;
        }

        /// <summary>Every URL requested, for assertion messages.</summary>
        public string[] UrlsSeen()
        {
            var urls = new string[Requests.Count];
            for (var i = 0; i < Requests.Count; i++)
            {
                urls[i] = Requests[i].Method + " " + Requests[i].Url;
            }

            return urls;
        }

        /// <summary>Builds a client over this handler.</summary>
        /// <param name="baseAddress">Base address; a trailing slash is required for relative paths.</param>
        public HttpClient CreateClient(string baseAddress = "https://api.example.test/api/")
        {
            return new HttpClient(this) { BaseAddress = new Uri(baseAddress) };
        }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = null;
            if (request.Content != null)
            {
                body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            }

            var url = request.RequestUri == null ? string.Empty : request.RequestUri.ToString();
            var authorization = request.Headers.Authorization == null
                ? null
                : request.Headers.Authorization.ToString();

            Requests.Add(new RecordedRequest(request.Method, url, body, authorization, request));

            foreach (var canned in _responses)
            {
                if (url.IndexOf(canned.UrlContains, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (canned.RemainingFailures.HasValue)
                {
                    if (canned.RemainingFailures.Value > 0)
                    {
                        canned.RemainingFailures = canned.RemainingFailures.Value - 1;
                        return Respond(canned.Status, "{}");
                    }

                    return Respond(HttpStatusCode.OK, canned.Json);
                }

                return Respond(canned.Status, canned.Json);
            }

            return Respond(DefaultStatus, DefaultJson);
        }

        private static HttpResponseMessage Respond(HttpStatusCode status, string json)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        /// <summary>One observed request.</summary>
        public sealed class RecordedRequest
        {
            internal RecordedRequest(
                HttpMethod method, string url, string? body, string? authorization, HttpRequestMessage instance)
            {
                Method = method;
                Url = url;
                Body = body;
                Authorization = authorization;
                Instance = instance;
            }

            /// <summary>
            /// The message object as received, kept only so a test can compare identity
            /// across attempts. Its content has already been read by then.
            /// </summary>
            public HttpRequestMessage Instance { get; }

            /// <summary>HTTP method used.</summary>
            public HttpMethod Method { get; }

            /// <summary>Absolute URL requested.</summary>
            public string Url { get; }

            /// <summary>Request body, or null.</summary>
            public string? Body { get; }

            /// <summary>Authorization header value, or null when absent.</summary>
            public string? Authorization { get; }
        }

        private sealed class Canned
        {
            public Canned(string urlContains, HttpStatusCode status, string json, int? remainingFailures)
            {
                UrlContains = urlContains;
                Status = status;
                Json = json;
                RemainingFailures = remainingFailures;
            }

            public string UrlContains { get; }

            public HttpStatusCode Status { get; }

            public string Json { get; }

            public int? RemainingFailures { get; set; }
        }
    }
}
