using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// Runs the Campaign Attribution funnel Node self-test, and fails this suite if it fails.
/// </summary>
/// <remarks>
/// The funnel tracker lives in the browser: history hooks, sendBeacon, the scroll and visibility
/// listeners, localStorage and the sessionStorage mirror. The self-test loads BOTH engines
/// (<c>WildwoodComponents.Blazor/wwwroot/js/wildwood-attribution.js</c> and
/// <c>WildwoodComponents.Razor/wwwroot/js/attribution.js</c>) into a fake browser, runs the same
/// scenarios against each, and requires them to send the same events. See <see cref="NodeSelfTest"/>
/// for the runner and for why a machine without Node skips rather than fails.
/// </remarks>
public class AttributionFunnelSelfTestRunnerTests
{
    private static string SelfTestPath()
    {
        return Path.Combine(
            NodeSelfTest.RepoRoot(), "WildwoodComponents.Tests", "Razor", "js", "attribution-funnel.selftest.mjs");
    }

    [Fact]
    public void The_self_test_and_both_engines_it_covers_are_present()
    {
        Assert.True(File.Exists(SelfTestPath()), $"Expected {SelfTestPath()} to exist.");

        foreach (var engine in new[]
                 {
                     Path.Combine(NodeSelfTest.RepoRoot(), "WildwoodComponents.Razor", "wwwroot", "js", "attribution.js"),
                     Path.Combine(
                         NodeSelfTest.RepoRoot(), "WildwoodComponents.Blazor", "wwwroot", "js", "wildwood-attribution.js")
                 })
        {
            Assert.True(File.Exists(engine), $"Expected {engine} to exist.");
        }
    }

    [NodeFact]
    public void The_funnel_tracker_passes_its_self_test_in_both_engines()
    {
        NodeSelfTest.Run(SelfTestPath(), "attribution funnel");
    }
}
