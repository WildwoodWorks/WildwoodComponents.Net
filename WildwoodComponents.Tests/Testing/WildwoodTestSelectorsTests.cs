using System.Text.RegularExpressions;
using WildwoodComponents.Testing;

namespace WildwoodComponents.Tests.Testing;

/// <summary>
/// The DOM contract the Playwright helpers key on - the one part of that package a test can
/// exercise with no browser at all, since it is nothing but strings.
/// </summary>
/// <remarks>
/// These are the same strings the Blazor and Razor markup tests assert from the other side, so a
/// hook renamed in one place and not the other fails here or there rather than in a browser suite
/// nobody runs on a pull request.
/// </remarks>
public class WildwoodTestSelectorsTests
{
    /// <summary>
    /// The step reader takes the ROOT'S step where one exists and a panel's everywhere else.
    /// </summary>
    /// <remarks>
    /// A CSS selector list resolves in DOCUMENT order, not list order, so the two forms cannot
    /// express a preference by their position in the string - what they express is that both are
    /// accepted. Razor mirrors the active step onto the root precisely so that its root sits ahead
    /// of its twelve panels in document order; React and Blazor render no step on the root, so the
    /// child is the only match there.
    /// </remarks>
    [Fact]
    public void The_signup_step_is_read_from_the_root_or_from_a_child()
    {
        Assert.Equal(
            "[data-ww-view=\"signup\"][data-ww-step], [data-ww-view=\"signup\"] [data-ww-step]",
            WildwoodTestSelectors.SignupStepSelector);
    }

    [Fact]
    public void The_manage_step_is_read_from_the_view_root()
    {
        Assert.Equal("[data-ww-view=\"manage\"]", WildwoodTestSelectors.ManageViewSelector);
    }

    /// <summary>
    /// Every step-scoped selector excludes the view root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the trap the whole contract turns on. Reading the step and scoping a control to that
    /// step are two different jobs and <c>data-ww-step</c> does both: because Razor mirrors the
    /// active step onto the root so one readable value exists, an unscoped
    /// <c>[data-ww-step="failed"] .ww-btn-primary</c> matches every primary button in the view the
    /// moment the flow is on <c>failed</c> - including the hidden register panel's submit, which
    /// precedes the real Try Again in document order. The first match is then a hidden button, and
    /// clicking it waits on actionability until the spec's own timeout: a hang, on the stack the
    /// mirroring was supposed to fix.
    /// </para>
    /// <para>
    /// So the rule is mechanical and checked mechanically: wherever a selector in this contract
    /// names a step, <c>:not([data-ww-view])</c> follows immediately.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_step_scoped_selector_excludes_the_view_root()
    {
        var unscoped = new Regex("\\[data-ww-step=\"[^\"]+\"\\](?!:not\\(\\[data-ww-view\\]\\))");

        foreach (var selector in EverySelector())
        {
            Assert.False(
                unscoped.IsMatch(selector),
                "A selector scopes to a step without excluding the view root, so on a stack that "
                + "mirrors the step onto the root it reaches the whole view: " + selector);
        }
    }

    /// <summary>
    /// The sweep above is answered by three selectors today, and stays answered by them.
    /// </summary>
    /// <remarks>
    /// The sweep passes trivially for a selector that names no step, which is what lets it cover the
    /// whole contract and catch a future selector that GAINS a step scope. The cost is that it would
    /// also pass if every step scope were removed at once - the rule would hold over nothing. This
    /// names the three that carry one, so the sweep cannot quietly become an assertion about an empty
    /// set. A deliberate fourth is a one-line edit here; an accidental zero is a failure.
    /// </remarks>
    [Fact]
    public void The_step_scoped_selectors_are_the_three_that_name_a_step()
    {
        var namesAStep = new Regex("\\[data-ww-step=\"[^\"]+\"\\]");

        var scoped = EverySelector().Where(selector => namesAStep.IsMatch(selector)).ToList();

        Assert.Equal(
            new[]
            {
                WildwoodTestSelectors.SubmitRegisterSelector,
                WildwoodTestSelectors.SignupRetrySelector,
                WildwoodTestSelectors.SignupFailureMessageSelectors[0],
            },
            scoped);
    }

    /// <summary>
    /// Every selector in the contract, step-scoped or not.
    /// </summary>
    /// <remarks>
    /// Deliberately the WHOLE contract rather than the step-scoped subset. Most entries here name no
    /// step and satisfy the sweep trivially - that is the point: a selector that gains a step scope
    /// tomorrow is covered the day it is written, with nobody having to remember to add it to a list.
    /// The companion test above pins which entries actually carry a step scope today, so the sweep
    /// cannot become vacuous across the board.
    /// </remarks>
    private static IEnumerable<string> EverySelector()
    {
        yield return WildwoodTestSelectors.SubmitRegisterSelector;
        yield return WildwoodTestSelectors.SignupRetrySelector;
        yield return WildwoodTestSelectors.SignupGetStartedSelector;
        yield return WildwoodTestSelectors.AcceptSelector;
        yield return WildwoodTestSelectors.RetrySelector;

        foreach (var selector in WildwoodTestSelectors.SignupFailureMessageSelectors) yield return selector;
        foreach (var selector in WildwoodTestSelectors.DisclaimerCheckSelectors) yield return selector;
    }

    /// <summary>
    /// The register panel's submit is found by its action hook or, failing that, by its type.
    /// </summary>
    /// <remarks>
    /// Never by name: the button says "Continue" when a plan step follows and "Create Account" when
    /// it does not, and both strings are host-configurable. The type fallback exists because a
    /// pay-first stack has no form to submit, so its control is a <c>type="button"</c>.
    /// </remarks>
    [Fact]
    public void The_register_submit_is_found_structurally()
    {
        Assert.Equal(
            "[data-ww-step=\"register\"]:not([data-ww-view]) [data-ww-action=\"submit-register\"], "
            + "[data-ww-step=\"register\"]:not([data-ww-view]) button[type=\"submit\"]",
            WildwoodTestSelectors.SubmitRegisterSelector);
    }

    /// <summary>
    /// The disclaimer retry's class fallback ignores anything that names a disclaimer action.
    /// </summary>
    /// <remarks>
    /// Without the exclusion it matched Accept All on a stack whose Accept All is a plain
    /// <c>.ww-btn-primary</c> in <c>.ww-disclaimer-actions</c> - and because the accept loop tests
    /// RETRY first, the run clicked Accept ten times as if it were a retry and then reported that
    /// the disclaimers never loaded, about disclaimers that had loaded fine.
    /// </remarks>
    [Fact]
    public void The_disclaimer_retry_fallback_never_matches_a_named_action()
    {
        Assert.Contains(
            ":not([data-ww-disclaimer-action])", WildwoodTestSelectors.RetrySelector, StringComparison.Ordinal);
        Assert.Contains(":not(.ww-btn-block)", WildwoodTestSelectors.RetrySelector, StringComparison.Ordinal);
    }

    /// <summary>
    /// The checkbox gate is only ever located by a named hook.
    /// </summary>
    /// <remarks>
    /// There is deliberately no bare <c>input[type="checkbox"]</c> entry: ticking every box a loose
    /// selector matched would opt the run into whatever else the panel hosts, and would quietly
    /// cover for a stack that never adopted the contract - which is the failure the contract exists
    /// to expose.
    /// </remarks>
    [Fact]
    public void The_disclaimer_gate_is_only_found_by_name()
    {
        Assert.Equal(
            new[] { "[data-ww-disclaimer-check]", ".ww-disclaimer-accept input[type=\"checkbox\"]" },
            WildwoodTestSelectors.DisclaimerCheckSelectors);
    }

    /// <summary>
    /// The failure text prefers its own hook, and the shared class is a SEPARATE entry.
    /// </summary>
    /// <remarks>
    /// Preference is why this one is a list rather than a comma-joined selector: the class names a
    /// paragraph every processing step shares, so on a stack that keeps all its panels in the DOM
    /// it matches the <c>creating</c> step's "please wait", which sits earlier in the document than
    /// the real error. Joined into one selector, a genuine failure would be reported as boilerplate.
    /// </remarks>
    [Fact]
    public void The_failure_message_prefers_its_own_hook_over_the_shared_class()
    {
        Assert.Equal(
            new[]
            {
                "[data-ww-step=\"failed\"]:not([data-ww-view]) [data-ww-error-message]",
                ".ww-signup-processing .ww-text-muted"
            },
            WildwoodTestSelectors.SignupFailureMessageSelectors);
    }

    /// <summary>Each registration field is found by the contract hook first and the React id second.</summary>
    [Theory]
    [InlineData(WildwoodTestSelectors.RegistrationField.FirstName, "firstName", "ww-reg-first")]
    [InlineData(WildwoodTestSelectors.RegistrationField.LastName, "lastName", "ww-reg-last")]
    [InlineData(WildwoodTestSelectors.RegistrationField.Username, "username", "ww-reg-username")]
    [InlineData(WildwoodTestSelectors.RegistrationField.Email, "email", "ww-reg-email")]
    [InlineData(WildwoodTestSelectors.RegistrationField.Password, "password", "ww-reg-password")]
    [InlineData(WildwoodTestSelectors.RegistrationField.ConfirmPassword, "confirmPassword", "ww-reg-confirm")]
    public void Each_registration_field_names_its_hook_and_its_id(
        WildwoodTestSelectors.RegistrationField field, string hook, string id)
    {
        Assert.Equal(hook, WildwoodTestSelectors.FieldName(field));
        Assert.Equal(id, WildwoodTestSelectors.FieldId(field));
        Assert.Equal(
            "[data-ww-field=\"" + hook + "\"], #" + id,
            WildwoodTestSelectors.RegistrationFieldSelector(field));
    }

    /// <summary>All six fields are in the contract, in form order.</summary>
    [Fact]
    public void The_registration_form_has_six_fields_in_order()
    {
        Assert.Equal(
            new[]
            {
                WildwoodTestSelectors.RegistrationField.FirstName,
                WildwoodTestSelectors.RegistrationField.LastName,
                WildwoodTestSelectors.RegistrationField.Username,
                WildwoodTestSelectors.RegistrationField.Email,
                WildwoodTestSelectors.RegistrationField.Password,
                WildwoodTestSelectors.RegistrationField.ConfirmPassword
            },
            WildwoodTestSelectors.RegistrationFieldOrder);
    }

    /// <summary>
    /// The acceptance watcher recognises the direct API call and the proxied one.
    /// </summary>
    /// <remarks>
    /// Which one a host makes is a stack decision: a browser-side stack POSTs to the API, while
    /// Razor proxies acceptance through a route of its own. The watcher is what turns a 429 - which
    /// acceptance shares with login and register on the API's per-IP limiter - into a diagnosis
    /// rather than "the button does nothing".
    /// </remarks>
    [Theory]
    [InlineData("https://api.example.com/api/disclaimeracceptance/accept", true)]
    [InlineData("https://api.example.com/api/disclaimeracceptance/accept-bulk", true)]
    [InlineData("https://host.example.com/api/disclaimer-gate/accept", true)]
    [InlineData("https://API.EXAMPLE.COM/api/DisclaimerAcceptance/Accept", true)]
    [InlineData("https://api.example.com/api/disclaimeracceptance/pending/app-1", false)]
    [InlineData("https://api.example.com/api/auth/login", false)]
    public void The_acceptance_watcher_knows_both_paths(string url, bool expected)
    {
        Assert.Equal(expected, WildwoodTestSelectors.MatchesAcceptResponse(url));
    }

    [Fact]
    public void The_acceptance_pattern_can_be_replaced_for_a_host_with_its_own_route()
    {
        var mine = new Regex("terms/agree", RegexOptions.IgnoreCase);

        Assert.True(WildwoodTestSelectors.MatchesAcceptResponse("https://example.com/terms/agree", mine));
        Assert.False(WildwoodTestSelectors.MatchesAcceptResponse(
            "https://example.com/api/disclaimeracceptance/accept", mine));
    }

    /// <summary>
    /// The same URL answers the same way every time.
    /// </summary>
    /// <remarks>
    /// The JS helper needs a guard here that this port does not: <c>RegExp.test</c> on a
    /// <c>/g</c> or <c>/y</c> pattern advances the pattern's own <c>lastIndex</c>, so every other
    /// response would be ignored - a flake nobody would trace back to a regex flag. .NET's
    /// <c>Regex</c> keeps no position between matches. This pins that, so the missing guard is a
    /// recorded difference rather than something a later reader restores by mistake.
    /// </remarks>
    [Fact]
    public void The_acceptance_watcher_is_not_stateful_between_calls()
    {
        const string url = "https://api.example.com/api/disclaimeracceptance/accept";

        Assert.True(WildwoodTestSelectors.MatchesAcceptResponse(url));
        Assert.True(WildwoodTestSelectors.MatchesAcceptResponse(url));
        Assert.True(WildwoodTestSelectors.MatchesAcceptResponse(url));
    }
}
