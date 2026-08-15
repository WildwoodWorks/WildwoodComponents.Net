using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WildwoodComponents.Shared.Http;

/// <summary>
/// Retries transient WildwoodAPI failures, mirroring the retry contract in
/// <c>@wildwood/core</c>'s <c>HttpClient</c> and <c>WildwoodCore</c>'s
/// <c>WildwoodHttpClient</c> so all three stacks behave the same way:
/// <list type="number">
///   <item>retry only on 5xx responses and network-level failures;</item>
///   <item>never replay a non-idempotent method (POST, PATCH);</item>
///   <item>never retry a timeout or a caller cancellation;</item>
///   <item>exponential backoff, <c>min(1000 * 2^attempt, 10000)</c> ms.</item>
/// </list>
/// Registered as a <see cref="DelegatingHandler"/> on the WildwoodAPI HttpClient by
/// the Blazor and Razor DI extensions, so every service inherits the same behaviour
/// without its own retry loop.
/// </summary>
/// <remarks>
/// Retrying re-sends the same <see cref="HttpRequestMessage"/>, which .NET Core 3.0+
/// permits. The SDK's retryable content-bearing requests use buffered content
/// (<c>StringContent</c> / <c>JsonContent</c>), which re-serializes cleanly; a
/// one-shot stream body would not, but no idempotent SDK call sends one.
/// <para>
/// The Seeder is deliberately exempt: <c>SeederApiClient</c> builds its own HttpClient and
/// <c>SeederRunner</c> already retries whole tasks, so layering this handler underneath
/// would compound the two.
/// </para>
/// </remarks>
public sealed class WildwoodRetryHandler : DelegatingHandler
{
    /// <summary>
    /// HTTP methods that may be replayed without changing the outcome (RFC 9110 §9.2.2).
    /// PUT and DELETE qualify: both describe an end state, so applying them twice lands
    /// where applying them once did. POST and PATCH do not — each is a fresh instruction
    /// to the server.
    /// </summary>
    private static readonly HashSet<string> IdempotentMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "PUT", "DELETE" };

    private readonly int _maxAttempts;
    private readonly ILogger? _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="maxAttempts">Maximum attempts including the first. Values below 1 are clamped to 1.</param>
    /// <param name="logger">Optional; retries are logged at Warning.</param>
    /// <param name="delay">Backoff hook. Defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>; tests substitute a no-op.</param>
    public WildwoodRetryHandler(
        int maxAttempts = 3,
        ILogger<WildwoodRetryHandler>? logger = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _maxAttempts = Math.Max(1, maxAttempts);
        _logger = logger;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Whether <paramref name="method"/> is safe to replay.</summary>
    public static bool IsIdempotent(HttpMethod method) =>
        method is not null && IdempotentMethods.Contains(method.Method);

    /// <summary>Backoff before the attempt following <paramref name="attempt"/> (0-based).</summary>
    public static TimeSpan GetBackoff(int attempt) =>
        TimeSpan.FromMilliseconds(Math.Min(1000d * Math.Pow(2, attempt), 10000d));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A non-idempotent request gets exactly one attempt. Neither a network error nor a
        // 5xx tells us whether the server already applied it, so replaying can duplicate the
        // effect — that is not hypothetical: on the JS side, one slow feedback submit filed
        // the feedback three times, because the row commits before the response is produced.
        var maxAttempts = IsIdempotent(request.Method) ? _maxAttempts : 1;

#if NETSTANDARD2_0
        // On .NET Framework, HttpClient.SendAsync marks a message as sent and refuses to
        // send that same instance again ("The request message was already sent."), then
        // disposes its content. This handler sits below that check, calling the inner
        // handler directly, so reuse does work here today — but only as an accident of
        // where the handler sits in the pipeline. Buffering the body once and giving each
        // attempt its own message makes the replay independent of that, and matches the
        // net10.0 path, where the restriction no longer exists. Requests that will only
        // ever be attempted once are passed through untouched.
        var buffered = maxAttempts > 1 ? await BufferAsync(request).ConfigureAwait(false) : null;
#endif

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? networkError = null;

#if NETSTANDARD2_0
            var attemptRequest = buffered == null ? request : CloneRequest(request, buffered);
#else
            var attemptRequest = request;
#endif

            try
            {
                response = await base.SendAsync(attemptRequest, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode < 500)
                {
                    return response;
                }
            }
            catch (HttpRequestException ex)
            {
                networkError = ex;
            }
            // OperationCanceledException is deliberately NOT caught. Both HttpClient.Timeout
            // and caller cancellation surface as one, and neither says anything about whether
            // the server processed the request — only that we stopped waiting for the answer.

            if (attempt >= maxAttempts - 1)
            {
                if (networkError is not null)
                {
                    ExceptionDispatchInfo.Capture(networkError).Throw();
                }

                return response!;
            }

            var backoff = GetBackoff(attempt);
            _logger?.LogWarning(
                "{Method} {Uri} failed ({Reason}) on attempt {Attempt}/{MaxAttempts}; retrying in {DelayMs}ms.",
                request.Method,
                request.RequestUri,
                networkError is not null ? networkError.Message : $"HTTP {(int)response!.StatusCode}",
                attempt + 1,
                maxAttempts,
                backoff.TotalMilliseconds);

            response?.Dispose();
            await _delay(backoff, cancellationToken).ConfigureAwait(false);
        }
    }

#if NETSTANDARD2_0
    /// <summary>
    /// Reads a retryable request's body into memory so it can be replayed. Returns an
    /// empty array for a bodyless request, which still signals "clone each attempt".
    /// </summary>
    private static async Task<byte[]> BufferAsync(HttpRequestMessage request)
    {
        if (request.Content == null)
        {
            return Array.Empty<byte>();
        }

        return await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a fresh request equivalent to <paramref name="request"/>, carrying the same
    /// method, URI, version, headers and properties, with the buffered body.
    /// </summary>
    private static HttpRequestMessage CloneRequest(HttpRequestMessage request, byte[] body)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var property in request.Properties)
        {
            clone.Properties[property.Key] = property.Value;
        }

        if (request.Content != null)
        {
            var content = new ByteArrayContent(body);
            foreach (var header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
        }

        return clone;
    }
#endif
}
