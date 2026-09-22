using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// Runs the consent banner's reserve-space Node self-test, and fails this suite if it fails.
/// </summary>
/// <remarks>
/// Keeping a <c>position: fixed</c> banner off the host's own edge-anchored UI genuinely lives in
/// the browser — the measurement, the <c>ResizeObserver</c> and the writes to
/// <c>document.body.style</c> have no C# counterpart. What does not need a browser is the
/// bookkeeping underneath: which position pads at all, how much, whose padding it is when two
/// banners are up, and what the page gets back afterwards. That half is kept as pure functions in
/// <c>WildwoodComponents.Razor/wwwroot/js/consent.js</c> — line for line the same text as the copy
/// in the Blazor engine, which
/// <see cref="AttributionConsentGateSourceTests.TheBookkeepingIsTheSameTextInBothEngines"/> pins —
/// and this test is what makes the self-test run in <c>dotnet test</c> like everything else. See
/// <see cref="NodeSelfTest"/> for the runner and for why a machine without Node skips rather than
/// fails.
/// </remarks>
public class ConsentReserveSpaceSelfTestRunnerTests
{
    private static string SelfTestPath()
    {
        return Path.Combine(
            NodeSelfTest.RepoRoot(), "WildwoodComponents.Tests", "Razor", "js", "consent-reserve-space.selftest.mjs");
    }

    [Fact]
    public void The_self_test_and_both_engines_it_covers_are_present()
    {
        Assert.True(File.Exists(SelfTestPath()), $"Expected {SelfTestPath()} to exist.");

        foreach (var engine in new[]
                 {
                     Path.Combine(NodeSelfTest.RepoRoot(), "WildwoodComponents.Razor", "wwwroot", "js", "consent.js"),
                     Path.Combine(
                         NodeSelfTest.RepoRoot(), "WildwoodComponents.Blazor", "wwwroot", "js", "wildwood-consent.js")
                 })
        {
            Assert.True(File.Exists(engine), $"Expected {engine} to exist.");
        }
    }

    [NodeFact]
    public void The_reserve_space_bookkeeping_passes_its_self_test()
    {
        NodeSelfTest.Run(SelfTestPath(), "consent reserve-space");
    }
}
