using System.Diagnostics;
using System.Globalization;

namespace WildwoodComponents.Testing;

// The two waits everything else in this package is built from.
//
// Ported from packages/wildwood-react/src/testing/poll.ts. The JS file exists because the helpers
// originally asserted with `expect` from @playwright/test, which made the built module carry a
// RUNTIME import of Playwright - and a consumer resolving a different copy of Playwright than its
// own test runner crashes on "Playwright was loaded twice" before a single assertion runs. C# has
// no equivalent of that hazard: a test project resolves one Microsoft.Playwright and these helpers
// bind to whatever it resolved, so there is no second copy to collide with. What carries over is
// the OTHER half of the reason: a failure here has to read like an assertion failure rather than a
// timeout, so every wait takes something that says what it was waiting for.
//
// Not public, for the same reason `index.ts` re-exports the steps, the flows and the selectors and
// nothing from poll.ts: a host writes waits against the published DOM contract, not against these.

/// <summary>
/// Polls until something is true, and fails saying what it was waiting for and what it saw instead.
/// </summary>
internal static class Poll
{
    /// <summary>How long to leave between reads when a caller does not say. JS's default.</summary>
    internal const int DefaultIntervalMs = 100;

    /// <summary>
    /// Polls <paramref name="check"/> until it answers true, or fails with <paramref name="message"/>.
    /// </summary>
    /// <remarks>
    /// The check runs BEFORE the deadline is tested, so a condition that becomes true exactly as the
    /// budget runs out still succeeds - a poll that failed a page it had already seen arrive would
    /// be flaky in the worst way, intermittently and only under load.
    /// </remarks>
    internal static async Task UntilAsync(
        Func<Task<bool>> check,
        int timeoutMs,
        Func<string> message,
        int intervalMs = DefaultIntervalMs)
    {
        // Stopwatch rather than wall-clock arithmetic: it is monotonic, so a clock change mid-run
        // cannot end a wait early or extend it indefinitely.
        var elapsed = Stopwatch.StartNew();

        for (; ; )
        {
            if (await check().ConfigureAwait(false)) return;
            if (elapsed.ElapsedMilliseconds >= timeoutMs) throw Timeout(message(), timeoutMs);
            await Task.Delay(intervalMs).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Polls <paramref name="read"/> until its value satisfies <paramref name="predicate"/>, then
    /// returns that value.
    /// </summary>
    /// <remarks>
    /// <paramref name="message"/> is handed the LAST value read, which is what lets a failure say
    /// "it is on <c>creating</c>" rather than only "it never reached <c>success</c>". The
    /// diagnostic is therefore a read of the page without the message itself having to touch the
    /// page - which is why, as in JS, it is a plain synchronous callback.
    /// </remarks>
    internal static async Task<T> UntilValueAsync<T>(
        Func<Task<T>> read,
        Func<T, bool> predicate,
        int timeoutMs,
        Func<T, string> message,
        int intervalMs = DefaultIntervalMs)
    {
        var elapsed = Stopwatch.StartNew();
        var last = await read().ConfigureAwait(false);

        for (; ; )
        {
            if (predicate(last)) return last;
            if (elapsed.ElapsedMilliseconds >= timeoutMs) throw Timeout(message(last), timeoutMs);
            await Task.Delay(intervalMs).ConfigureAwait(false);
            last = await read().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The failure, with how long was spent on it appended - JS's <c>(waited 30s)</c>.
    /// </summary>
    /// <remarks>
    /// Away-from-zero rounding because that is what JS's <c>Math.round</c> does with a half;
    /// .NET's default is to-even, so a 500 ms budget would otherwise be reported as 0s here and 1s
    /// there for the same wait.
    /// </remarks>
    private static WildwoodContractException Timeout(string message, int timeoutMs)
    {
        var seconds = (int)Math.Round(timeoutMs / 1000d, MidpointRounding.AwayFromZero);
        return new WildwoodContractException(
            message + " (waited " + seconds.ToString(CultureInfo.InvariantCulture) + "s)");
    }
}
