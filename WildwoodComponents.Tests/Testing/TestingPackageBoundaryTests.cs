using System.Xml.Linq;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Testing;

/// <summary>
/// The package boundary that keeps a browser driver out of everything a host ships.
/// </summary>
/// <remarks>
/// <para>
/// The JS helpers keep Playwright out of their built module with <c>import type</c>, which erases
/// at compile time. C# has no type-only import, so this port uses the only equivalent it has: a
/// separate package nothing ships. Adding <c>Microsoft.Playwright</c> to WildwoodComponents.Blazor
/// or WildwoodComponents.Razor would copy its driver - about 100 MB of Node and JavaScript, before
/// any browser is installed - into the build output of every consuming app, and Shared is worse
/// still: both UI packages reference it, as does the netstandard2.0 leg WebForms consumes.
/// </para>
/// <para>
/// That boundary is a property of five project files, and nothing but a reader's attention
/// otherwise keeps it. One <c>ProjectReference</c> added for convenience by someone wiring up a
/// test would undo it silently, because everything would still build.
/// </para>
/// <para>
/// The references are read as XML rather than searched for as text, so that a project file may go
/// on EXPLAINING the boundary in a comment - as WildwoodComponents.Testing's own does - without
/// tripping the test that enforces it.
/// </para>
/// </remarks>
public class TestingPackageBoundaryTests
{
    /// <summary>Every package and project a project file references, by name.</summary>
    private static IReadOnlyList<string> References(string package)
    {
        var path = Path.Combine(NodeSelfTest.RepoRoot(), package, package + ".csproj");
        Assert.True(File.Exists(path), $"Expected {package}.csproj to exist at {path}.");

        var names = new List<string>();

        foreach (var element in XDocument.Load(path).Descendants())
        {
            if (element.Name.LocalName is not ("PackageReference" or "ProjectReference")) continue;

            var include = (string?)element.Attribute("Include");
            if (include is not { Length: > 0 }) continue;

            // A project reference is a path; what matters is which project it names.
            names.Add(element.Name.LocalName == "ProjectReference"
                ? Path.GetFileNameWithoutExtension(include.Replace('\\', Path.DirectorySeparatorChar))
                : include);
        }

        return names;
    }

    [Theory]
    [InlineData("WildwoodComponents.Blazor")]
    [InlineData("WildwoodComponents.Razor")]
    [InlineData("WildwoodComponents.Shared")]
    [InlineData("WildwoodComponents.WebForms")]
    public void No_shipped_package_depends_on_the_browser_driver(string package)
    {
        var references = References(package);

        Assert.DoesNotContain("Microsoft.Playwright", references);
        Assert.DoesNotContain("WildwoodComponents.Testing", references);
    }

    /// <summary>
    /// The helpers reference Shared and nothing else of ours.
    /// </summary>
    /// <remarks>
    /// Shared is for the machines' step enums and the <c>StepNames</c> table the views publish.
    /// Blazor and Razor are deliberately absent: what these helpers drive is the DOM, so a
    /// reference to either would be a dependency on a type where the contract is markup - and it
    /// would drag a UI package into a test-only build for nothing.
    /// </remarks>
    [Fact]
    public void The_helpers_reference_the_shared_library_and_no_UI_package()
    {
        var references = References("WildwoodComponents.Testing");

        Assert.Contains("WildwoodComponents.Shared", references);
        Assert.Contains("Microsoft.Playwright", references);

        Assert.DoesNotContain("WildwoodComponents.Blazor", references);
        Assert.DoesNotContain("WildwoodComponents.Razor", references);
    }

    /// <summary>The helpers are in the solution, so the build and the suite see them.</summary>
    [Fact]
    public void The_helpers_are_in_the_solution()
    {
        var solution = File.ReadAllText(
            Path.Combine(NodeSelfTest.RepoRoot(), "WildwoodComponents.Net.slnx"));

        Assert.Contains(
            "WildwoodComponents.Testing/WildwoodComponents.Testing.csproj", solution, StringComparison.Ordinal);
    }
}
