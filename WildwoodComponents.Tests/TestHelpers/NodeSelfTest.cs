using System.Diagnostics;
using System.Text;

namespace WildwoodComponents.Tests.TestHelpers;

/// <summary>
/// Runs a dependency-free Node self-test from <c>dotnet test</c>, and the discovery-time skip
/// that keeps a Node-less machine green.
/// </summary>
/// <remarks>
/// <para>
/// Some of this library genuinely lives in the browser — the registration/subscription machines,
/// the AI chat's speech decisions — and this repository has no JavaScript test harness. Adding one
/// (a package.json, a runner, a CI install step) for a handful of files is not worth it, so those
/// scripts carry self-tests written in plain Node and this helper is what makes them run alongside
/// everything else.
/// </para>
/// <para>
/// A self-test SKIPS rather than fails when <c>node</c> is not on PATH: a .NET developer on a
/// machine with no Node must still be able to run the suite, and CI has Node, so the coverage is
/// not optional there. A skip says so in the output rather than passing silently. xUnit 2 cannot
/// skip from inside a test, so the decision is made at discovery by <see cref="NodeFactAttribute"/>.
/// </para>
/// </remarks>
public static class NodeSelfTest
{
    /// <summary>
    /// The directory holding the solution file, found by walking up from the test assembly — so a
    /// checkout at any path works, and a run from anywhere does too.
    /// </summary>
    public static string RepoRoot()
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

    /// <summary>Whether <c>node</c> can be started, decided by scanning PATH.</summary>
    public static bool NodeIsOnPath()
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

    /// <summary>
    /// Runs <paramref name="selfTestPath"/> under node from the repo root and fails the calling
    /// test, with the script's output attached, when it does not exit 0.
    /// </summary>
    public static void Run(string selfTestPath, string what)
    {
        Assert.True(File.Exists(selfTestPath), $"Expected {selfTestPath} to exist.");

        var start = new ProcessStartInfo
        {
            FileName = "node",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot()
        };
        start.ArgumentList.Add(selfTestPath);

        // PATH said node is there, so a failure to start it now is a real problem, not a skip.
        var process = Process.Start(start);
        Assert.NotNull(process);

        var output = new StringBuilder();
        output.Append(process!.StandardOutput.ReadToEnd());
        output.Append(process.StandardError.ReadToEnd());

        // Generous: a self-test is pure computation and finishes in milliseconds, so this only
        // ever fires if node itself hung.
        Assert.True(process.WaitForExit(60_000), $"The {what} self-test did not finish within 60 seconds.");

        Assert.True(process.ExitCode == 0, $"The {what} self-test failed:\n" + output);
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips itself when <c>node</c> is not on PATH. xUnit 2's
/// assertions cannot skip at run time, so the decision is made at discovery: the test then reports
/// as SKIPPED with the reason rather than passing silently or failing a developer who has no Node
/// installed.
/// </summary>
public sealed class NodeFactAttribute : FactAttribute
{
    public NodeFactAttribute()
    {
        if (!NodeSelfTest.NodeIsOnPath())
        {
            Skip = "node is not on PATH, so the JavaScript self-test was not run.";
        }
    }
}
