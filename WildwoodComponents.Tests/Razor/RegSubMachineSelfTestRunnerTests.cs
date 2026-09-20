using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// Runs the signup and pack-checkout machines' Node self-test, and fails this suite if it fails.
/// </summary>
/// <remarks>
/// The Razor signup's flow genuinely lives in the browser: the two reducers in
/// <c>wwwroot/js/regsub-machines.js</c> are the one part of it no C# test can reach. This repo has
/// no JavaScript test harness and adding one (a package.json, a runner, a CI install step) for two
/// files is not worth it — so the machines carry a dependency-free self-test that replays a
/// representative subset of the TypeScript suite's cases, and this test is what makes it run in
/// <c>dotnet test</c> like everything else. See <see cref="NodeSelfTest"/> for the runner and for
/// why a machine without Node skips rather than fails.
/// </remarks>
public class RegSubMachineSelfTestRunnerTests
{
    private static string SelfTestPath()
    {
        return Path.Combine(
            NodeSelfTest.RepoRoot(), "WildwoodComponents.Tests", "Razor", "js", "regsub-machines.selftest.mjs");
    }

    [Fact]
    public void The_self_test_and_the_machine_it_covers_are_both_present()
    {
        Assert.True(File.Exists(SelfTestPath()), $"Expected {SelfTestPath()} to exist.");

        var machine = Path.Combine(
            NodeSelfTest.RepoRoot(), "WildwoodComponents.Razor", "wwwroot", "js", "regsub-machines.js");
        Assert.True(File.Exists(machine), $"Expected {machine} to exist.");
    }

    [NodeFact]
    public void The_machines_pass_their_self_test()
    {
        NodeSelfTest.Run(SelfTestPath(), "registration/subscription machine");
    }
}
