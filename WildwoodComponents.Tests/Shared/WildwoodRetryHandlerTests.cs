using System.Net;
using System.Text;
using WildwoodComponents.Shared.Http;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The retry contract shared with @wildwood/core and WildwoodCore: retry only 5xx and
/// network failures, only for idempotent methods, never on timeout or cancellation.
/// </summary>
public class WildwoodRetryHandlerTests
{
    /// <summary>Inner handler that counts attempts and replays a scripted result per attempt.</summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> _respond;

        public CountingHandler(Func<int, HttpResponseMessage> respond) => _respond = respond;

        public int Calls { get; private set; }

        /// <summary>Body seen on each attempt, so a replay can be proven to re-serialize.</summary>
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var attempt = Calls++;
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return _respond(attempt);
        }
    }

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    private static (HttpMessageInvoker Invoker, CountingHandler Inner, List<TimeSpan> Delays) CreateInvoker(
        Func<int, HttpResponseMessage> respond, int maxAttempts = 3)
    {
        var inner = new CountingHandler(respond);
        var delays = new List<TimeSpan>();
        var retry = new WildwoodRetryHandler(
            maxAttempts,
            delay: (d, _) =>
            {
                delays.Add(d);
                return Task.CompletedTask;
            })
        {
            InnerHandler = inner
        };
        return (new HttpMessageInvoker(retry), inner, delays);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpMessageInvoker invoker, HttpMethod method) =>
        invoker.SendAsync(new HttpRequestMessage(method, "https://api.test/api/thing"), CancellationToken.None);

    [Fact]
    public async Task RetriesServerErrors_ForGet_UntilSuccess()
    {
        var (invoker, inner, delays) = CreateInvoker(attempt =>
            attempt < 2 ? Status(HttpStatusCode.InternalServerError) : Status(HttpStatusCode.OK));

        var response = await SendAsync(invoker, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls);
        Assert.Equal([TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(2000)], delays);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task RetriesServerErrors_ForOtherIdempotentMethods(string method)
    {
        var (invoker, inner, _) = CreateInvoker(attempt =>
            attempt < 1 ? Status(HttpStatusCode.BadGateway) : Status(HttpStatusCode.OK));

        var response = await SendAsync(invoker, new HttpMethod(method));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    public async Task DoesNotReplayNonIdempotentMethods_OnServerError(string method)
    {
        var (invoker, inner, delays) = CreateInvoker(_ => Status(HttpStatusCode.InternalServerError));

        var response = await SendAsync(invoker, new HttpMethod(method));

        // The submit must be delivered exactly once — replaying it would duplicate the
        // effect, because the server commits before the response is produced.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, inner.Calls);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task DoesNotReplayNonIdempotentMethods_OnNetworkError()
    {
        var (invoker, inner, _) = CreateInvoker(_ => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(invoker, HttpMethod.Post));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task DoesNotRetryClientErrors()
    {
        var (invoker, inner, _) = CreateInvoker(_ => Status(HttpStatusCode.NotFound));

        var response = await SendAsync(invoker, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task RetriesNetworkErrors_ForGet()
    {
        var (invoker, inner, _) = CreateInvoker(attempt =>
            attempt < 2 ? throw new HttpRequestException("connection refused") : Status(HttpStatusCode.OK));

        var response = await SendAsync(invoker, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task DoesNotRetryCancellation()
    {
        // HttpClient.Timeout and caller cancellation both surface as TaskCanceledException;
        // neither says whether the server processed the request, so neither is replayed.
        var (invoker, inner, _) = CreateInvoker(_ => throw new TaskCanceledException("timed out"));

        await Assert.ThrowsAsync<TaskCanceledException>(() => SendAsync(invoker, HttpMethod.Get));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task ReturnsLastServerError_WhenAttemptsAreExhausted()
    {
        var (invoker, inner, delays) = CreateInvoker(_ => Status(HttpStatusCode.ServiceUnavailable));

        var response = await SendAsync(invoker, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.Calls);
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task RethrowsNetworkError_WhenAttemptsAreExhausted()
    {
        var (invoker, inner, _) = CreateInvoker(_ => throw new HttpRequestException("connection refused"));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(invoker, HttpMethod.Get));

        Assert.Equal("connection refused", error.Message);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task MaxAttemptsBelowOne_IsClampedToOne()
    {
        var (invoker, inner, _) = CreateInvoker(_ => Status(HttpStatusCode.InternalServerError), maxAttempts: 0);

        var response = await SendAsync(invoker, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task ReplaysBufferedContent_OnRetriedPut()
    {
        // The class remark claims re-sending the same HttpRequestMessage is safe for the
        // buffered content the SDK uses. This is the case that would prove it wrong.
        var (invoker, inner, _) = CreateInvoker(attempt =>
            attempt < 1 ? Status(HttpStatusCode.ServiceUnavailable) : Status(HttpStatusCode.OK));

        var request = new HttpRequestMessage(HttpMethod.Put, "https://api.test/api/thing")
        {
            Content = new StringContent("""{"name":"value"}""", Encoding.UTF8, "application/json")
        };

        var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
        Assert.Equal(["""{"name":"value"}""", """{"name":"value"}"""], inner.Bodies);
    }

    [Fact]
    public async Task BackoffHonoursCancellation()
    {
        var inner = new CountingHandler(_ => Status(HttpStatusCode.InternalServerError));
        using var cts = new CancellationTokenSource();
        var retry = new WildwoodRetryHandler(
            maxAttempts: 3,
            delay: (d, ct) =>
            {
                // Cancel as the backoff begins: the token must reach Task.Delay and abort it.
                cts.Cancel();
                return Task.Delay(d, ct);
            })
        {
            InnerHandler = inner
        };
        using var invoker = new HttpMessageInvoker(retry);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.test/api/thing"), cts.Token));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public void GetBackoff_IsExponential_AndCappedAtTenSeconds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(1000), WildwoodRetryHandler.GetBackoff(0));
        Assert.Equal(TimeSpan.FromMilliseconds(2000), WildwoodRetryHandler.GetBackoff(1));
        Assert.Equal(TimeSpan.FromMilliseconds(4000), WildwoodRetryHandler.GetBackoff(2));
        Assert.Equal(TimeSpan.FromMilliseconds(8000), WildwoodRetryHandler.GetBackoff(3));
        Assert.Equal(TimeSpan.FromMilliseconds(10000), WildwoodRetryHandler.GetBackoff(4));
        Assert.Equal(TimeSpan.FromMilliseconds(10000), WildwoodRetryHandler.GetBackoff(9));
    }

    [Fact]
    public void IsIdempotent_MatchesRfc9110()
    {
        Assert.True(WildwoodRetryHandler.IsIdempotent(HttpMethod.Get));
        Assert.True(WildwoodRetryHandler.IsIdempotent(HttpMethod.Head));
        Assert.True(WildwoodRetryHandler.IsIdempotent(HttpMethod.Options));
        Assert.True(WildwoodRetryHandler.IsIdempotent(HttpMethod.Put));
        Assert.True(WildwoodRetryHandler.IsIdempotent(HttpMethod.Delete));
        Assert.False(WildwoodRetryHandler.IsIdempotent(HttpMethod.Post));
        Assert.False(WildwoodRetryHandler.IsIdempotent(HttpMethod.Patch));
    }
}
