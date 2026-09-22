using System.Text.RegularExpressions;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// The shared DOM contract the Razor signup surface honours, so one browser suite can drive React,
/// Blazor and this stack by the same <c>data-ww-*</c> names.
/// </summary>
/// <remarks>
/// <para>
/// Blazor pins these by rendering the component and reading the markup back. Razor's views have no
/// such harness here — a ViewComponent test gets the view MODEL, not the HTML — so these read the
/// shipped sources, the way this project's other markup rules are pinned (see
/// <see cref="RegistrationSubscriptionRazorSourceGuardTests"/>). What that can prove is exactly
/// what drifts in practice: a hook renamed in the markup and not in the script, a contract name
/// spelled as this package's own, and the ONE place the active step is readable from.
/// </para>
/// <para>
/// What it cannot prove is that the script runs. That is stated rather than papered over: the
/// mirror itself is one <c>setAttribute</c> call shared with <c>regsub-manage.js</c>, which the
/// manage view exercises.
/// </para>
/// </remarks>
public class RegistrationSubscriptionSignupDomContractTests
{
    private static string PackageRoot() => Path.Combine(NodeSelfTest.RepoRoot(), "WildwoodComponents.Razor");

    private static string SignupView() => File.ReadAllText(Path.Combine(
        PackageRoot(), "Views", "Shared", "Components", "RegistrationSubscriptionSignup", "Default.cshtml"));

    private static string SignupScript() => File.ReadAllText(Path.Combine(
        PackageRoot(), "wwwroot", "js", "regsub-signup.js"));

    /// <summary>
    /// The active step is readable from the ROOT, and the root is what a reader resolves to.
    /// </summary>
    /// <remarks>
    /// This surface keeps all twelve step panels in the DOM and hides eleven of them, so every one
    /// of them carries a <c>data-ww-step</c> of its own and none of them answers "which step is
    /// this flow on". The root does, and a reader that takes the first match of
    /// <c>[data-ww-view="signup"][data-ww-step], [data-ww-view="signup"] [data-ww-step]</c>
    /// resolves it in DOCUMENT order — so the root's attribute has to sit ahead of every panel's,
    /// which is what the ordering below pins. Without it the first match is the <c>loading</c>
    /// panel, forever: every step wait hangs, and a wait for a step to be LEFT returns at once and
    /// wrongly.
    /// </remarks>
    [Fact]
    public void The_root_is_where_the_signup_step_is_read()
    {
        var view = SignupView();

        var root = view.IndexOf("data-ww-view=\"signup\"", StringComparison.Ordinal);
        var rootStep = view.IndexOf("data-ww-step=\"loading\"", StringComparison.Ordinal);
        var firstPanel = view.IndexOf("class=\"ww-signup-step", StringComparison.Ordinal);

        Assert.True(root >= 0, "The signup root no longer carries data-ww-view=\"signup\".");
        Assert.True(firstPanel >= 0, "The signup view renders no step panels.");

        Assert.True(
            rootStep > root,
            "The signup root carries no data-ww-step: the twelve panels each carry one, so with no "
            + "mirror on the root a reader gets whichever panel comes first.");
        Assert.True(
            rootStep < firstPanel,
            "The first data-ww-step in the signup view belongs to a panel, not to the root: a reader "
            + "resolves the list in document order, so the root's has to come first.");
        Assert.Equal(view.IndexOf("data-ww-step=", StringComparison.Ordinal), rootStep);
    }

    /// <summary>
    /// The script keeps the root's step up to date, and hides panels WITHOUT hiding the root.
    /// </summary>
    /// <remarks>
    /// The mirror is the same single line <c>regsub-manage.js</c>'s <c>paintFlow</c> writes. It has
    /// to be a mutation rather than a constant for a second reason beyond the reader: a step
    /// recorder watches the attribute with a <c>MutationObserver</c>, and an attribute that is only
    /// ever server-rendered records nothing. The panel sweep stays scoped to the root through
    /// <c>qa</c> — <c>root.querySelectorAll</c> never returns its own context node — which is what
    /// stops the root's new attribute making the root hide itself.
    /// </remarks>
    [Fact]
    public void The_script_mirrors_the_step_onto_the_root_and_hides_only_panels()
    {
        var script = SignupScript();

        Assert.Contains("root.setAttribute('data-ww-step'", script, StringComparison.Ordinal);
        Assert.Contains("qa('[data-ww-step]')", script, StringComparison.Ordinal);
        Assert.DoesNotContain("document.querySelectorAll('[data-ww-step]')", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The signup's three shared controls carry the cross-stack names, not names one letter away.
    /// </summary>
    /// <remarks>
    /// <c>retry</c>, <c>start-over</c> and <c>complete</c> are worse than absent: they read as the
    /// contract at a glance and match none of it. The prefixed spellings are also what keeps them
    /// apart from this package's OWN <c>data-ww-action</c> vocabulary — the pricing view's plain
    /// <c>retry</c> reloads a catalog and stays exactly as it is.
    /// </remarks>
    [Fact]
    public void The_signup_controls_carry_the_shared_action_vocabulary()
    {
        var view = SignupView();

        foreach (var name in new[] { "submit-register", "signup-retry", "signup-start-over", "signup-get-started" })
        {
            Assert.Contains($"data-ww-action=\"{name}\"", view, StringComparison.Ordinal);
        }

        foreach (var nearMiss in new[] { "retry", "start-over", "complete" })
        {
            Assert.True(
                !view.Contains($"data-ww-action=\"{nearMiss}\"", StringComparison.Ordinal),
                $"The signup view still names '{nearMiss}': the contract spells it 'signup-{nearMiss}' "
                + "(or, for complete, 'signup-get-started').");
        }
    }

    /// <summary>
    /// Every action the signup markup names is handled by the signup script.
    /// </summary>
    /// <remarks>
    /// <c>data-ww-action</c> does double duty on this surface: it carries the contract's names AND
    /// this package's own click vocabulary, read by the switch in <c>regsub-signup.js</c>. So a
    /// rename is only ever half a change, and the half that is easy to forget is silent — the
    /// button simply stops doing anything. This pins the two halves together.
    /// </remarks>
    [Fact]
    public void Every_action_the_signup_markup_names_is_handled_by_the_signup_script()
    {
        var view = SignupView();
        var script = SignupScript();

        var actions = new Regex("data-ww-action=\"([^\"]+)\"")
            .Matches(view)
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(actions);

        foreach (var action in actions)
        {
            Assert.True(
                script.Contains($"case '{action}':", StringComparison.Ordinal),
                $"The signup markup names '{action}' but regsub-signup.js handles no such case: "
                + "the button does nothing at all.");
        }
    }

    /// <summary>
    /// Both disclaimer gates NAME their checkboxes.
    /// </summary>
    /// <remarks>
    /// Accept ships disabled on both surfaces until the required boxes are ticked, and a click on a
    /// disabled button waits for it to become clickable. A suite that cannot find the boxes
    /// therefore hangs on Accept and reports it as the server's fault. There is deliberately no
    /// bare <c>input[type="checkbox"]</c> fallback on the reader's side, so the marker is the only
    /// thing standing between a working gate and that misdiagnosis.
    /// </remarks>
    [Theory]
    [InlineData("RegistrationSubscriptionSignup")]
    [InlineData("Disclaimer")]
    public void The_disclaimer_gate_names_its_checkboxes(string component)
    {
        var view = File.ReadAllText(Path.Combine(
            PackageRoot(), "Views", "Shared", "Components", component, "Default.cshtml"));

        // On the <input> itself, so a mention of the marker in a comment cannot stand in for it.
        Assert.Matches(new Regex(@"<input\b[^>]*\bdata-ww-disclaimer-check\b", RegexOptions.IgnoreCase), view);
    }
}
