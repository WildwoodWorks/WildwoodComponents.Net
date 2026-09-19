using System.Diagnostics;
using System.Text;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// Runs the signup and pack-checkout machines' Node self-test, and fails this suite if it fails.
/// </summary>
/// <remarks>
/// <para>
/// The Razor signup's flow genuinely lives in the browser: the two reducers in
/// <c>wwwroot/js/regsub-machines.js</c> are the one part of it no C# test can reach. This repo has
/// no JavaScript test harness and adding one (a package.json, a runner, a CI install step) for two
/// files is not worth it — so the machines carry a dependency-free self-test that replays a
/// representative subset of the TypeScript suite's cases, and this test is what makes it run in
/// <c>dotnet test</c> like everything else.
/// </para>
/// <para>
/// It SKIPS rather than fails when <c>node</c> is not on PATH. A .NET developer on a machine with
/// no Node must still be able to run the suite; CI has Node, so the coverage is not optional
/// there. A skip says so in the output rather than passing silently. xUnit 2 cannot skip from
/// inside a test, so the decision is made at discovery by <c>NodeFactAttribute</c>.
/// </para>
/// </remarks>
public class RegSubMachineSelfTestRunnerTests
{
    /// <summary>
    /// A <see cref="FactAttribute"/> that skips itself when <c>node</c> is not on PATH. xUnit 2's
    /// assertions cannot skip at run time, so the decision is made at discovery: the test then
    /// reports as SKIPPED with the reason rather than passing silently or failing a developer who
    /// has no Node installed.
    /// </summary>
    private sealed class NodeFactAttribute : FactAttribute
    {
        public NodeFactAttribute()
        {
            if (!NodeIsOnPath())
            {
                Skip = "node is not on PATH, so the registration/subscription machine self-test was not run.";
            }
        }

        private static bool NodeIsOnPath()
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (path is null || path.Length == 0) return false;

            var names = OperatingSystem.IsWindows()
                ? new[] { "node.exe", "node.cmd", "node.bat" }
                : new[] { "node" };

            foreach (var directory in path.Split(Path.PathSeparator))
            {
                if (directory.Length == 0) continue;

                foreach (var name in names)
                {
                    try
                    {
                        if (File.Exists(Path.Combine(directory, name))) return true;
                    }
                    catch (ArgumentException)
                    {
                        // A malformed PATH entry is not a reason to fail discovery.
                    }
                }
            }

            return false;
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WildwoodComponents.Net.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find WildwoodComponents.Net.slnx above {AppContext.BaseDirectory}.");
    }

    private static string SelfTestPath()
    {
        return Path.Combine(
            RepoRoot(), "WildwoodComponents.Tests", "Razor", "js", "regsub-machines.selftest.mjs");
    }

    [Fact]
    public void The_self_test_and_the_machine_it_covers_are_both_present()
    {
        Assert.True(File.Exists(SelfTestPath()), $"Expected {SelfTestPath()} to exist.");

        var machine = Path.Combine(
            RepoRoot(), "WildwoodComponents.Razor", "wwwroot", "js", "regsub-machines.js");
        Assert.True(File.Exists(machine), $"Expected {machine} to exist.");
    }

    [NodeFact]
    public void The_machines_pass_their_self_test()
    {
        var selfTest = SelfTestPath();
        Assert.True(File.Exists(selfTest), $"Expected {selfTest} to exist.");

        var start = new ProcessStartInfo
        {
            FileName = "node",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot()
        };
        start.ArgumentList.Add(selfTest);

        // PATH said node is there, so a failure to start it now is a real problem, not a skip.
        var process = Process.Start(start);
        Assert.NotNull(process);

        var output = new StringBuilder();
        output.Append(process!.StandardOutput.ReadToEnd());
        output.Append(process.StandardError.ReadToEnd());

        // Generous: the self-test is pure computation and finishes in milliseconds, so this only
        // ever fires if node itself hung.
        Assert.True(process.WaitForExit(60_000), "The machine self-test did not finish within 60 seconds.");

        Assert.True(
            process.ExitCode == 0,
            "The registration/subscription machine self-test failed:\n" + output);
    }
}
