using System.Text.RegularExpressions;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// No price is ever written into the registration + subscription component.
/// </summary>
/// <remarks>
/// <para>
/// The port of the dynamic-pricing guard React enforces over its own sources
/// (react/__tests__/RegistrationAndSubscription.pricing.test.tsx). Every price the component shows
/// comes off the live public catalog: there is no fallback price, no remembered price and no
/// "from" price. A literal is how that rule gets broken in practice — somebody hard-codes "$0.00"
/// for a missing amount, or writes last quarter's figure into a placeholder — and neither shows up
/// as a failing behaviour test, because the number LOOKS right until the operator changes it.
/// </para>
/// <para>
/// Only shipped C# and Razor markup is scanned. The scoped stylesheet is not: a CSS length like
/// <c>0.75rem</c> matches the cents pattern and is not a price.
/// </para>
/// </remarks>
public class RegistrationSubscriptionSourceGuardTests
{
    /// <summary>A currency sign followed by a figure.</summary>
    private static readonly Regex CurrencyLiteral = new(@"\$\s*\d", RegexOptions.Compiled);

    /// <summary>An amount with cents.</summary>
    private static readonly Regex CentsLiteral = new(@"\b\d+\.\d\d\b", RegexOptions.Compiled);

    /// <summary>
    /// The directory holding the solution file, found by walking up from the test assembly — so a
    /// checkout at any path works, and a run from anywhere does too.
    /// </summary>
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

    private static string ComponentRoot()
    {
        return Path.Combine(
            RepoRoot(), "WildwoodComponents.Blazor", "Components", "RegistrationSubscription");
    }

    private static List<string> SourceFiles()
    {
        var root = ComponentRoot();
        var files = new List<string>();

        foreach (var pattern in new[] { "*.cs", "*.razor" })
        {
            foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            {
                files.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
            }
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    public static TheoryData<string> ComponentSources()
    {
        var data = new TheoryData<string>();
        foreach (var file in SourceFiles()) data.Add(file);
        return data;
    }

    [Fact]
    public void The_component_sources_are_where_this_test_thinks_they_are()
    {
        Assert.True(Directory.Exists(ComponentRoot()), $"Expected {ComponentRoot()} to exist.");
        Assert.True(SourceFiles().Count > 5, "the guard found almost no sources to scan");
    }

    [Theory]
    [MemberData(nameof(ComponentSources))]
    public void No_source_states_a_price_on_its_own_authority(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(ComponentRoot(), relativePath));

        var currency = CurrencyLiteral.Match(source);
        Assert.True(
            !currency.Success,
            $"{relativePath} carries a currency literal ('{currency.Value}'): every amount comes off the live catalog.");

        var cents = CentsLiteral.Match(source);
        Assert.True(
            !cents.Success,
            $"{relativePath} carries a price literal ('{cents.Value}'): every amount comes off the live catalog.");
    }
}
