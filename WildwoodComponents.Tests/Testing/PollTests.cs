using WildwoodComponents.Testing;

namespace WildwoodComponents.Tests.Testing;

/// <summary>
/// The two waits the Playwright helpers are built from, exercised without a browser.
/// </summary>
/// <remarks>
/// <para>
/// JS has no test file for <c>poll.ts</c> - its behaviour is only ever seen through a live page -
/// so these pin the properties the port had to get right and that a rewrite could silently lose:
/// the check runs before the deadline is tested, the diagnostic names the LAST value seen, and the
/// elapsed time is reported the way JS reports it.
/// </para>
/// <para>
/// Every timeout here is small and every interval is 1ms, so the suite does not sit waiting: what
/// is being pinned is which value a wait returns and what a failure says, not how long it takes.
/// </para>
/// </remarks>
public class PollTests
{
    /// <summary>A reader that answers the given values in turn, then repeats the last one.</summary>
    private static Func<Task<T>> Reads<T>(params T[] values)
    {
        var index = 0;
        return () =>
        {
            var value = values[index];
            if (index < values.Length - 1) index++;
            return Task.FromResult(value);
        };
    }

    [Fact]
    public async Task UntilAsync_ReturnsWhenTheCheckPasses()
    {
        var checks = 0;

        await Poll.UntilAsync(
            () => Task.FromResult(++checks >= 3),
            timeoutMs: 5_000,
            () => "never true",
            intervalMs: 1);

        Assert.Equal(3, checks);
    }

    /// <summary>
    /// A condition that becomes true exactly as the budget runs out still succeeds.
    /// </summary>
    /// <remarks>
    /// The check runs BEFORE the deadline is tested, which is the only ordering that is not flaky:
    /// the other one fails a page it has already seen arrive, intermittently and only under load.
    /// A zero budget is the sharpest way to state it - there is no interval in which the check
    /// could be "in time" any other way.
    /// </remarks>
    [Fact]
    public async Task UntilAsync_ChecksBeforeItTestsTheDeadline()
    {
        await Poll.UntilAsync(() => Task.FromResult(true), timeoutMs: 0, () => "never true", intervalMs: 1);
    }

    [Fact]
    public async Task UntilAsync_FailsWithTheCallersMessage()
    {
        var failure = await Assert.ThrowsAsync<WildwoodContractException>(() => Poll.UntilAsync(
            () => Task.FromResult(false),
            timeoutMs: 0,
            () => "the banner never went away",
            intervalMs: 1));

        Assert.StartsWith("the banner never went away", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UntilValueAsync_ReturnsTheFirstValueThePredicateAccepts()
    {
        var value = await Poll.UntilValueAsync(
            Reads("loading", "register", "success"),
            step => step == "success",
            timeoutMs: 5_000,
            step => "stuck on " + step,
            intervalMs: 1);

        Assert.Equal("success", value);
    }

    /// <summary>
    /// The failure names what was on screen, not only what was wanted.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the helpers do not simply let a Playwright wait time out: "timed
    /// out" reads as the product hanging, while "it is on creating" tells the reader in one line
    /// that the flow is alive and the spec is waiting for the wrong thing.
    /// </remarks>
    [Fact]
    public async Task UntilValueAsync_FailsNamingTheLastValueItSaw()
    {
        // The interval outlasts the budget, so the second read is the one the deadline lands on and
        // the message must carry ITS value rather than the value the wait started with.
        var failure = await Assert.ThrowsAsync<WildwoodContractException>(() => Poll.UntilValueAsync(
            Reads("loading", "creating"),
            step => step == "success",
            timeoutMs: 100,
            step => "never reached success - it is on " + step,
            intervalMs: 150));

        Assert.StartsWith("never reached success - it is on creating", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// How long was spent is appended in seconds, rounded away from zero as JS's
    /// <c>Math.round</c> rounds. .NET's default is to-even, which would report this same wait as
    /// <c>0s</c> here and <c>1s</c> in the JS helpers.
    /// </summary>
    [Fact]
    public async Task UntilValueAsync_SaysHowLongItWaited()
    {
        var failure = await Assert.ThrowsAsync<WildwoodContractException>(() => Poll.UntilValueAsync(
            Reads("creating"),
            step => step == "success",
            timeoutMs: 500,
            step => "never reached success",
            intervalMs: 1));

        Assert.Equal("never reached success (waited 1s)", failure.Message);
    }
}
