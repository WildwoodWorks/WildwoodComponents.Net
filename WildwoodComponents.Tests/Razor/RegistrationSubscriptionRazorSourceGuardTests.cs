using System.Text.RegularExpressions;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// No price is ever written into the Razor registration + subscription surface.
/// </summary>
/// <remarks>
/// <para>
/// The Razor twin of <c>RegistrationSubscriptionSourceGuardTests</c>, itself the port of the
/// dynamic-pricing guard React enforces over its own sources. Every price the surface shows comes
/// off the live public catalog: there is no fallback price, no remembered price and no "from"
/// price. A literal is how that rule gets broken in practice — somebody hard-codes a zero for a
/// missing amount, or writes last quarter's figure into a placeholder — and neither shows up as a
/// failing behaviour test, because the number LOOKS right until the operator changes it.
/// </para>
/// <para>
/// Razor puts more of the surface in the browser than Blazor does, so the guard covers the script
/// as well as the C# and the markup. The stylesheet is NOT scanned: a CSS length like
/// <c>0.75rem</c> matches the cents pattern and is not a price.
/// </para>
/// </remarks>
public class RegistrationSubscriptionRazorSourceGuardTests
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

    private static string PackageRoot()
    {
        return Path.Combine(RepoRoot(), "WildwoodComponents.Razor");
    }

    /// <summary>Every shipped file that can state a price, relative to the package root.</summary>
    private static List<string> GuardedFiles()
    {
        return
        [
            Path.Combine("Components", "RegistrationSubscription", "RegistrationSubscriptionPricingViewComponent.cs"),
            Path.Combine("Components", "RegistrationSubscription", "RegistrationSubscriptionPricingDecisions.cs"),
            Path.Combine("Models", "RegistrationSubscriptionPricingModels.cs"),
            Path.Combine("Views", "Shared", "Components", "RegistrationSubscriptionPricing", "Default.cshtml"),
            Path.Combine("Views", "Shared", "_RegSubPackCardBody.cshtml"),
            Path.Combine("wwwroot", "js", "regsub-pricing.js")
        ];
    }

    public static TheoryData<string> GuardedSources()
    {
        var data = new TheoryData<string>();
        foreach (var file in GuardedFiles()) data.Add(file);
        return data;
    }

    [Fact]
    public void The_guarded_sources_are_where_this_test_thinks_they_are()
    {
        foreach (var file in GuardedFiles())
        {
            var path = Path.Combine(PackageRoot(), file);
            Assert.True(File.Exists(path), $"Expected {path} to exist.");
        }
    }

    [Theory]
    [MemberData(nameof(GuardedSources))]
    public void No_source_states_a_price_on_its_own_authority(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(PackageRoot(), relativePath));

        var currency = CurrencyLiteral.Match(source);
        Assert.True(
            !currency.Success,
            $"{relativePath} carries a currency literal ('{currency.Value}'): every amount comes off the live catalog.");

        var cents = CentsLiteral.Match(source);
        Assert.True(
            !cents.Success,
            $"{relativePath} carries a price literal ('{cents.Value}'): every amount comes off the live catalog.");
    }

    /// <summary>
    /// The stylesheet's every custom property carries a fallback, so the surface looks right in a
    /// host that never imported the theme file. A bare reference is a known defect class here.
    /// </summary>
    [Fact]
    public void Every_custom_property_in_the_stylesheet_has_a_fallback()
    {
        var css = File.ReadAllText(Path.Combine(PackageRoot(), "wwwroot", "css", "regsub.css"));
        var bare = new Regex(@"var\(--ww-[a-zA-Z0-9-]+\)").Match(css);

        Assert.True(
            !bare.Success,
            $"regsub.css uses '{bare.Value}' with no fallback value.");
    }
}
