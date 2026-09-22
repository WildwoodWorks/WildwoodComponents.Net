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
            Path.Combine("Components", "RegistrationSubscription", "RegistrationSubscriptionSignupViewComponent.cs"),
            Path.Combine("Components", "RegistrationSubscription", "RegistrationSubscriptionSignupDecisions.cs"),
            Path.Combine("Components", "RegistrationSubscription", "RegistrationSubscriptionManageViewComponent.cs"),
            Path.Combine("Components", "RegistrationSubscription", "RegistrationSubscriptionManageDecisions.cs"),
            Path.Combine("Components", "RegistrationSubscription", "RegistrationAndSubscriptionViewComponent.cs"),
            Path.Combine("Components", "RegistrationSubscription", "RegistrationAndSubscriptionShell.cs"),
            Path.Combine("Models", "RegistrationSubscriptionPricingModels.cs"),
            Path.Combine("Models", "RegistrationSubscriptionSignupModels.cs"),
            Path.Combine("Models", "RegistrationSubscriptionManageModels.cs"),
            Path.Combine("Models", "RegistrationAndSubscriptionModels.cs"),
            Path.Combine("Views", "Shared", "Components", "RegistrationSubscriptionPricing", "Default.cshtml"),
            Path.Combine("Views", "Shared", "Components", "RegistrationSubscriptionSignup", "Default.cshtml"),
            Path.Combine("Views", "Shared", "Components", "RegistrationSubscriptionManage", "Default.cshtml"),
            Path.Combine("Views", "Shared", "Components", "RegistrationAndSubscription", "Default.cshtml"),
            Path.Combine("Views", "Shared", "_RegSubPackCardBody.cshtml"),
            Path.Combine("Views", "Shared", "_RegSubPlanGrid.cshtml"),
            Path.Combine("Views", "Shared", "_RegSubManageSection.cshtml"),
            Path.Combine("wwwroot", "js", "regsub-pricing.js"),
            Path.Combine("wwwroot", "js", "regsub-machines.js"),
            Path.Combine("wwwroot", "js", "regsub-signup.js"),
            Path.Combine("wwwroot", "js", "regsub-packcheckout.js"),
            Path.Combine("wwwroot", "js", "regsub-planchange.js"),
            Path.Combine("wwwroot", "js", "regsub-manage.js")
        ];
    }

    /// <summary>The browser half of the surface: the two scripts, and nothing else.</summary>
    private static List<string> GuardedScripts()
    {
        return
        [
            Path.Combine("wwwroot", "js", "regsub-pricing.js"),
            Path.Combine("wwwroot", "js", "regsub-machines.js"),
            Path.Combine("wwwroot", "js", "regsub-signup.js"),
            Path.Combine("wwwroot", "js", "regsub-packcheckout.js"),
            Path.Combine("wwwroot", "js", "regsub-planchange.js"),
            Path.Combine("wwwroot", "js", "regsub-manage.js")
        ];
    }

    /// <summary>The markup of the surface.</summary>
    private static List<string> GuardedViews()
    {
        return
        [
            Path.Combine("Views", "Shared", "Components", "RegistrationSubscriptionPricing", "Default.cshtml"),
            Path.Combine("Views", "Shared", "Components", "RegistrationSubscriptionSignup", "Default.cshtml"),
            Path.Combine("Views", "Shared", "Components", "RegistrationSubscriptionManage", "Default.cshtml"),
            Path.Combine("Views", "Shared", "Components", "RegistrationAndSubscription", "Default.cshtml"),
            Path.Combine("Views", "Shared", "_RegSubPackCardBody.cshtml"),
            Path.Combine("Views", "Shared", "_RegSubPlanGrid.cshtml"),
            Path.Combine("Views", "Shared", "_RegSubManageSection.cshtml")
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

    /// <summary>
    /// No script in this surface writes through <c>innerHTML</c>.
    /// </summary>
    /// <remarks>
    /// Everything these scripts paint is either a server-rendered string (a label, a plan name, a
    /// refusal in the server's own words) or a value a person typed. <c>innerHTML</c> with either
    /// of those is a cross-site-scripting hole that reads as ordinary code, so the rule is simply
    /// that it is never used: text goes in with <c>textContent</c> and elements are created with
    /// <c>createElement</c>. The one place server-supplied HTML genuinely has to be rendered as
    /// HTML - a disclaimer whose content format says so - is rendered by the Razor VIEW instead.
    /// </remarks>
    [Theory]
    [MemberData(nameof(GuardedScriptSources))]
    public void No_script_writes_through_innerHTML(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(PackageRoot(), relativePath));

        foreach (var forbidden in new[] { "innerHTML", "outerHTML", "insertAdjacentHTML", "document.write" })
        {
            Assert.True(
                !source.Contains(forbidden, StringComparison.Ordinal),
                $"{relativePath} uses {forbidden}: paint text with textContent and build nodes with createElement.");
        }
    }

    /// <summary>
    /// There is no card-number, expiry or CVC field anywhere in this surface.
    /// </summary>
    /// <remarks>
    /// The Razor signup this replaces had exactly that: a hand-rolled card form whose "Complete
    /// Payment" button shipped disabled and had no handler, so a paid plan dead-ended AND the page
    /// asked for a PAN a Stripe integration must never see. Cards are collected by Stripe Elements
    /// inside the payment component and the pack checkout's SetupIntent, both of which mount an
    /// iframe this package cannot read. This guard is what stops the old shape coming back.
    /// </remarks>
    [Theory]
    [MemberData(nameof(GuardedViewSources))]
    public void No_view_asks_for_a_card_number(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(PackageRoot(), relativePath));

        // Every <input ...> in the view, with whatever attributes it carries.
        foreach (Match input in new Regex(@"<input\b[^>]*>", RegexOptions.IgnoreCase).Matches(source))
        {
            var tag = input.Value;

            foreach (var smell in new[]
                     {
                         "cc-number", "cc-exp", "cc-csc", "cc-name",
                         "cardnumber", "card-number", "card_number",
                         "cardexpiry", "card-expiry", "cvc", "cvv", "securitycode", "security-code"
                     })
            {
                Assert.True(
                    !tag.Contains(smell, StringComparison.OrdinalIgnoreCase),
                    $"{relativePath} has an <input> naming '{smell}': cards are Stripe Elements, never fields of ours.\n{tag}");
            }
        }
    }

    /// <summary>
    /// The two surfaces that change a plan run the SAME driver.
    /// </summary>
    /// <remarks>
    /// <c>&lt;vc:registration-subscription-manage /&gt;</c> and the older
    /// <c>&lt;vc:subscription-admin /&gt;</c> both change a plan, and the part that could drift is
    /// the part that moves money: whether <c>SupportsPaymentAction</c> is sent, whether a 3-D
    /// Secure challenge is put to the customer, and whether the parked change is ever completed.
    /// So neither script may carry a copy — both call <c>regsub-planchange.js</c>, and the manage
    /// root's <c>data-ww-view="manage"</c> is what stops one root getting two drivers.
    /// </remarks>
    [Theory]
    [InlineData("regsub-manage.js")]
    [InlineData("subscription-admin.js")]
    public void Both_plan_change_surfaces_call_the_one_shared_driver(string script)
    {
        var source = File.ReadAllText(Path.Combine(PackageRoot(), "wwwroot", "js", script));

        Assert.Contains("wwRegSubPlanChange", source);

        // The sequence itself belongs to the driver. A copy would start with these.
        foreach (var owned in new[] { "confirmCardPayment", "planChangeTransition", "SupportsPaymentAction" })
        {
            Assert.True(
                !source.Contains(owned, StringComparison.Ordinal),
                $"{script} carries '{owned}': the plan-change sequence lives in regsub-planchange.js only.");
        }
    }

    /// <summary>
    /// The two surfaces that buy packs run the SAME driver, for the same reason: one quote, one
    /// card for the whole basket, and an item the bank has authenticated is always completed.
    /// </summary>
    [Theory]
    [InlineData("regsub-manage.js")]
    [InlineData("regsub-signup.js")]
    public void Both_pack_buying_surfaces_call_the_one_shared_driver(string script)
    {
        var source = File.ReadAllText(Path.Combine(PackageRoot(), "wwwroot", "js", script));

        Assert.Contains("wwRegSubPackCheckout", source);

        foreach (var owned in new[] { "confirmCardSetup", "packCheckoutTransition", "checkout/quote" })
        {
            Assert.True(
                !source.Contains(owned, StringComparison.Ordinal),
                $"{script} carries '{owned}': the pack checkout lives in regsub-packcheckout.js only.");
        }
    }

    public static TheoryData<string> GuardedScriptSources()
    {
        var data = new TheoryData<string>();
        foreach (var file in GuardedScripts()) data.Add(file);
        return data;
    }

    public static TheoryData<string> GuardedViewSources()
    {
        var data = new TheoryData<string>();
        foreach (var file in GuardedViews()) data.Add(file);
        return data;
    }
}
