using System.Text.RegularExpressions;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Testing;

namespace WildwoodComponents.Tests.Testing;

/// <summary>
/// The signup drivers' rules, exercised without a browser.
/// </summary>
/// <remarks>
/// <para>
/// Every public entry point takes an <c>IPage</c> or an <c>ILocator</c> and wraps it at once in the
/// package's internal seam - the dozen page operations the drivers perform. These tests drive that
/// seam with a fake, which is what lets the parts worth proving run here: the order the gate is
/// ticked in, the bounded budgets, the 429 back-off, the preference order the failure message is
/// read in, and the response handler coming off the page however the loop ends.
/// </para>
/// <para>
/// What is deliberately NOT covered is the seam's own implementation. Each of its members is one
/// locator call plus the catch that turns "nothing matched" into an answer; only a browser can
/// prove those, and the end-to-end smoke spec is where they are proved.
/// </para>
/// </remarks>
public class WildwoodSignupFlowsTests
{
    private static readonly string Accept = WildwoodTestSelectors.AcceptSelector;
    private static readonly string Retry = WildwoodTestSelectors.RetrySelector;
    private static readonly string AcceptOrRetry = WildwoodSignupFlows.AcceptOrRetrySelector;
    private static readonly string Gate = WildwoodTestSelectors.DisclaimerCheckSelectors[0];
    private static readonly string LooseGate = WildwoodTestSelectors.DisclaimerCheckSelectors[1];

    /// <summary>Options that fail fast, so a deliberate failure does not spend a real budget.</summary>
    /// <remarks>
    /// The polls in <c>FinishSignupAsync</c> read every 100ms until their budget runs out, so a
    /// test of a wait that never ends would otherwise sit on the shipped two-minute window.
    /// </remarks>
    private static WildwoodTestingOptions Quick()
    {
        return new WildwoodTestingOptions
        {
            ProcessingTimeoutMs = 0,
            DisclaimerRenderTimeoutMs = 0,
            LeaveFailedTimeoutMs = 0,
            SuccessTimeoutMs = 0
        };
    }

    #region The registration form

    /// <summary>
    /// Every field in the contract is filled, in the contract's own order.
    /// </summary>
    /// <remarks>
    /// The order is <c>RegistrationFieldOrder</c> rather than a list written out here, so a field
    /// added to the contract has to be given a value in the driver before it compiles - and the
    /// form is filled top to bottom, as a visitor fills it.
    /// </remarks>
    [Fact]
    public async Task FillRegistrationForm_fills_every_contract_field_in_form_order()
    {
        var surface = new FakeSurface();

        await WildwoodSignupFlows.FillRegistrationFormAsync(surface, new RegistrationFields
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Username = "ada",
            Email = "ada@example.test",
            Password = "Correct-Horse-9!"
        });

        Assert.Equal(
            new[]
            {
                "fill " + Field(WildwoodTestSelectors.RegistrationField.FirstName) + "=Ada",
                "fill " + Field(WildwoodTestSelectors.RegistrationField.LastName) + "=Lovelace",
                "fill " + Field(WildwoodTestSelectors.RegistrationField.Username) + "=ada",
                "fill " + Field(WildwoodTestSelectors.RegistrationField.Email) + "=ada@example.test",
                "fill " + Field(WildwoodTestSelectors.RegistrationField.Password) + "=Correct-Horse-9!",
                "fill " + Field(WildwoodTestSelectors.RegistrationField.ConfirmPassword) + "=Correct-Horse-9!"
            },
            surface.Actions);
    }

    [Fact]
    public async Task SubmitRegistrationForm_clicks_the_register_panel_submit()
    {
        var surface = new FakeSurface();

        await WildwoodSignupFlows.SubmitRegistrationFormAsync(surface);

        Assert.Equal(new[] { "click " + WildwoodTestSelectors.SubmitRegisterSelector }, surface.Actions);
    }

    private static string Field(WildwoodTestSelectors.RegistrationField field)
    {
        return WildwoodTestSelectors.RegistrationFieldSelector(field);
    }

    #endregion

    #region The consent banner

    /// <summary>
    /// No banner is the common case and costs one wait, not a failure.
    /// </summary>
    /// <remarks>
    /// The wait is what makes the driver worth calling: a bare visibility read never waits, so a
    /// banner painting a beat after the navigation would slip past it and take the clicks aimed at
    /// the page for the rest of the spec.
    /// </remarks>
    [Fact]
    public async Task DismissConsentBanner_returns_quietly_when_no_banner_appears()
    {
        var surface = new FakeSurface();

        await WildwoodSignupFlows.DismissConsentBannerAsync(surface, new WildwoodTestingOptions());

        Assert.Equal(new[] { "waitVisible .ww-consent-banner" }, surface.Actions);
    }

    [Fact]
    public async Task DismissConsentBanner_answers_the_banner_and_waits_for_it_to_go()
    {
        var surface = new FakeSurface();
        surface.VisibleSelectors.Add(".ww-consent-banner");
        surface.OnClick = (page, _) => page.VisibleSelectors.Remove(".ww-consent-banner");

        await WildwoodSignupFlows.DismissConsentBannerAsync(surface, new WildwoodTestingOptions());

        Assert.Equal(
            new[]
            {
                "waitVisible .ww-consent-banner",
                "click .ww-consent-banner .ww-consent-btn-primary",
                "waitHidden .ww-consent-banner"
            },
            surface.Actions);
    }

    /// <summary>
    /// A banner that was answered and stayed is reported as the component's failure, not as a
    /// timeout on an anonymous locator.
    /// </summary>
    [Fact]
    public async Task DismissConsentBanner_fails_when_the_banner_stays()
    {
        var surface = new FakeSurface();
        surface.VisibleSelectors.Add(".ww-consent-banner");

        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupFlows.DismissConsentBannerAsync(surface, new WildwoodTestingOptions()));

        Assert.Contains("never went away", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Both consent budgets reach the wait they name.</summary>
    [Fact]
    public async Task DismissConsentBanner_waits_for_as_long_as_the_options_say()
    {
        var surface = new FakeSurface();
        surface.VisibleSelectors.Add(".ww-consent-banner");
        surface.OnClick = (page, _) => page.VisibleSelectors.Remove(".ww-consent-banner");

        await WildwoodSignupFlows.DismissConsentBannerAsync(
            surface, new WildwoodTestingOptions { ConsentBannerWaitMs = 11, ConsentBannerHiddenMs = 22 });

        Assert.Equal(new[] { 11, 22 }, surface.Timeouts);
    }

    #endregion

    #region The gate in front of Accept

    /// <summary>
    /// The gate is ticked BEFORE Accept is clicked.
    /// </summary>
    /// <remarks>
    /// Both .NET stacks keep Accept <c>disabled</c> until every required box is ticked, and a click
    /// waits for actionability - so ticking afterwards, or not at all, leaves the driver hanging on
    /// the button until the spec's own timeout and then blaming the server for it.
    /// </remarks>
    [Fact]
    public async Task AcceptDisclaimers_ticks_the_gate_before_clicking_accept()
    {
        var surface = OneAcceptWaiting();
        surface.Counts[Gate] = 2;

        await WildwoodSignupFlows.AcceptDisclaimersAsync(surface, null, new WildwoodTestingOptions());

        Assert.Equal(
            new[] { "check " + Gate + "#0", "check " + Gate + "#1", "click " + Accept },
            surface.Actions.Where(action => !action.StartsWith("wait", StringComparison.Ordinal)
                && !action.StartsWith("delay", StringComparison.Ordinal)).ToArray());
    }

    /// <summary>
    /// The first gate selector that matches anything IS the gate; the looser fallback is not also
    /// run.
    /// </summary>
    /// <remarks>
    /// The tick ticks every box the winning selector matches, so running the later entries too
    /// would opt the run into whatever else the panel happens to host.
    /// </remarks>
    [Fact]
    public async Task AcceptDisclaimers_consults_only_the_first_gate_selector_that_matches()
    {
        var surface = OneAcceptWaiting();
        surface.Counts[Gate] = 1;
        surface.Counts[LooseGate] = 3;

        await WildwoodSignupFlows.AcceptDisclaimersAsync(surface, null, new WildwoodTestingOptions());

        Assert.Contains("check " + Gate + "#0", surface.Actions);
        Assert.DoesNotContain(surface.Actions, action => action.StartsWith("check " + LooseGate, StringComparison.Ordinal));
    }

    /// <summary>A box the visitor's stack already ticked is left alone.</summary>
    [Fact]
    public async Task AcceptDisclaimers_leaves_an_already_ticked_box_alone()
    {
        var surface = OneAcceptWaiting();
        surface.Counts[Gate] = 2;
        surface.Ticked.Add(Gate + "#0");

        await WildwoodSignupFlows.AcceptDisclaimersAsync(surface, null, new WildwoodTestingOptions());

        Assert.DoesNotContain("check " + Gate + "#0", surface.Actions);
        Assert.Contains("check " + Gate + "#1", surface.Actions);
    }

    /// <summary>A stack with no gate - React - is ticked nothing and clicks Accept.</summary>
    [Fact]
    public async Task AcceptDisclaimers_ticks_nothing_when_the_stack_has_no_gate()
    {
        var surface = OneAcceptWaiting();

        await WildwoodSignupFlows.AcceptDisclaimersAsync(surface, null, new WildwoodTestingOptions());

        Assert.DoesNotContain(surface.Actions, action => action.StartsWith("check ", StringComparison.Ordinal));
        Assert.Contains("click " + Accept, surface.Actions);
    }

    #endregion

    #region The accept loop

    /// <summary>
    /// Nothing to accept returns without a click - and without pretending it did something.
    /// </summary>
    /// <remarks>
    /// The settle wait on entry is what makes that answer trustworthy: the component fetches its
    /// pending list when the step mounts, so an immediate count of 0 cannot tell "nothing left to
    /// accept" from "the list has not arrived".
    /// </remarks>
    [Fact]
    public async Task AcceptDisclaimers_returns_without_clicking_when_nothing_is_pending()
    {
        var surface = new FakeSurface();

        await WildwoodSignupFlows.AcceptDisclaimersAsync(surface, null, new WildwoodTestingOptions());

        Assert.Equal(new[] { "waitVisible " + AcceptOrRetry }, surface.Actions);
    }

    /// <summary>A caller that is already done does not wait for a panel it has finished with.</summary>
    [Fact]
    public async Task AcceptDisclaimers_skips_the_settle_wait_when_the_caller_is_already_done()
    {
        var surface = new FakeSurface();

        await WildwoodSignupFlows.AcceptDisclaimersAsync(
            surface, () => Task.FromResult(true), new WildwoodTestingOptions());

        Assert.Empty(surface.Actions);
    }

    /// <summary>The load-failure retry is clicked, and a retry that never goes away fails saying so.</summary>
    /// <remarks>
    /// Waiting only for an Accept would time out on a button that is never coming, and report the
    /// disclaimers as never accepted rather than as never loaded.
    /// </remarks>
    [Fact]
    public async Task AcceptDisclaimers_clicks_the_components_own_retry_and_gives_up_on_it()
    {
        var surface = new FakeSurface();
        surface.VisibleSelectors.Add(Retry);
        surface.VisibleSelectors.Add(AcceptOrRetry);

        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupFlows.AcceptDisclaimersAsync(
                surface, null, new WildwoodTestingOptions { AcceptAttempts = 2 }));

        Assert.Contains("never loaded", failure.Message, StringComparison.Ordinal);
        Assert.Equal(2, surface.Actions.Count(action => action == "click " + Retry));
    }

    /// <summary>The pause after a retry click is the one the options name.</summary>
    [Fact]
    public async Task AcceptDisclaimers_pauses_after_a_retry_for_as_long_as_the_options_say()
    {
        var surface = new FakeSurface();
        surface.VisibleSelectors.Add(Retry);
        surface.VisibleSelectors.Add(AcceptOrRetry);

        await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupFlows.AcceptDisclaimersAsync(
                surface, null, new WildwoodTestingOptions { AcceptAttempts = 1, RetryPauseMs = 9 }));

        Assert.Contains("delay 9", surface.Actions);
    }

    /// <summary>
    /// An Accept that never goes away is a defect, and is reported as one after the budget.
    /// </summary>
    [Fact]
    public async Task AcceptDisclaimers_gives_up_on_an_accept_that_never_converges()
    {
        var surface = OneAcceptWaiting();
        surface.OnClick = (_, _) => { };

        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupFlows.AcceptDisclaimersAsync(
                surface, null, new WildwoodTestingOptions { AcceptAttempts = 3 }));

        Assert.Contains("did not converge after 3 clicks", failure.Message, StringComparison.Ordinal);
        Assert.Equal(3, surface.Actions.Count(action => action == "click " + Accept));
    }

    /// <summary>
    /// A refused POST is named with its status, because the component gives no sign of one.
    /// </summary>
    /// <remarks>
    /// The component leaves the button where it is when acceptance is refused, so without the
    /// watched response this reads as "the button does nothing".
    /// </remarks>
    [Fact]
    public async Task AcceptDisclaimers_names_the_status_when_the_server_refused()
    {
        var surface = OneAcceptWaiting();
        surface.OnClick = (page, _) => page.Watcher!.Record("https://api.test/api/disclaimeracceptance/accept", 500);

        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupFlows.AcceptDisclaimersAsync(
                surface, null, new WildwoodTestingOptions { AcceptAttempts = 2 }));

        Assert.Contains("refused by the server - last response HTTP 500, after 2 attempts",
            failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A 429 is waited out rather than counted, and it never spends the click budget.
    /// </summary>
    /// <remarks>
    /// Acceptance shares the API's per-IP auth rate limit with login and register, so a suite
    /// enrolling several users a minute from one address meets this routinely. Six clicks under a
    /// three-click budget is the point: a refused attempt proved nothing, so it must not count
    /// towards "this Accept is stuck".
    /// </remarks>
    [Fact]
    public async Task AcceptDisclaimers_waits_out_a_rate_limit_without_spending_the_click_budget()
    {
        var surface = OneAcceptWaiting();
        surface.OnClick = (page, _) => page.Watcher!.Record("https://api.test/api/disclaimeracceptance/accept", 429);

        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupFlows.AcceptDisclaimersAsync(
                surface,
                null,
                new WildwoodTestingOptions { AcceptAttempts = 3, RateLimitWaits = 5, RateLimitPauseMs = 11 }));

        Assert.Contains("kept returning HTTP 429", failure.Message, StringComparison.Ordinal);
        Assert.Contains("per-IP auth rate limit", failure.Message, StringComparison.Ordinal);
        Assert.Equal(6, surface.Actions.Count(action => action == "click " + Accept));
        Assert.Equal(5, surface.Actions.Count(action => action == "delay 11"));
    }

    /// <summary>
    /// A host that proxies acceptance elsewhere is read by ITS pattern, not the default.
    /// </summary>
    /// <remarks>
    /// The proof is the message: under the default pattern this URL matches nothing, the status
    /// stays 0, and the loop would run its click budget out and report "did not converge" instead.
    /// </remarks>
    [Fact]
    public async Task AcceptDisclaimers_reads_the_response_by_the_pattern_the_options_name()
    {
        var surface = OneAcceptWaiting();
        surface.OnClick = (page, _) => page.Watcher!.Record("https://app.test/terms/agree", 429);

        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupFlows.AcceptDisclaimersAsync(
                surface,
                null,
                new WildwoodTestingOptions
                {
                    AcceptResponsePattern = new Regex("terms/agree"),
                    AcceptAttempts = 3,
                    RateLimitWaits = 0
                }));

        Assert.Contains("kept returning HTTP 429", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The settle wait, the gate's tick and the pause after a click spend the named budgets.</summary>
    [Fact]
    public async Task AcceptDisclaimers_spends_the_budgets_the_options_name()
    {
        var surface = OneAcceptWaiting();
        surface.Counts[Gate] = 1;

        await WildwoodSignupFlows.AcceptDisclaimersAsync(
            surface,
            null,
            new WildwoodTestingOptions { DisclaimerSettleMs = 33, DisclaimerGateTimeoutMs = 44, AcceptPauseMs = 7 });

        Assert.Equal(new[] { 33, 44 }, surface.Timeouts);
        Assert.Contains("delay 7", surface.Actions);
    }

    /// <summary>
    /// One click's answer is not read as the next one's.
    /// </summary>
    /// <remarks>
    /// Without the reset before each click, a single 429 would be re-read on every later pass and
    /// the loop would keep backing off over a status nothing answered with any more - and would
    /// then blame the server in a failure that was really "the button is stuck".
    /// </remarks>
    [Fact]
    public async Task AcceptDisclaimers_forgets_the_previous_response_before_each_click()
    {
        var surface = OneAcceptWaiting();
        var clicks = 0;
        surface.OnClick = (page, _) =>
        {
            if (++clicks == 1) page.Watcher!.Record("https://api.test/api/disclaimeracceptance/accept", 429);
        };

        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupFlows.AcceptDisclaimersAsync(
                surface, null, new WildwoodTestingOptions { AcceptAttempts = 3, RateLimitWaits = 5 }));

        Assert.Contains("did not converge", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The caller's "done" ends the loop as soon as the click lands.
    /// </summary>
    [Fact]
    public async Task AcceptDisclaimers_stops_as_soon_as_the_caller_says_it_is_done()
    {
        var surface = OneAcceptWaiting();
        var done = false;
        surface.OnClick = (_, _) => done = true;

        await WildwoodSignupFlows.AcceptDisclaimersAsync(
            surface, () => Task.FromResult(done), new WildwoodTestingOptions());

        Assert.Equal(1, surface.Actions.Count(action => action == "click " + Accept));
    }

    /// <summary>
    /// The response handler comes back off the page however the loop ends.
    /// </summary>
    /// <remarks>
    /// Every bounded-failure path here throws, and a handler left attached to a long-lived page
    /// fires for every later navigation in the spec and keeps its closure alive with it. The pairing
    /// is a <c>using</c> for that reason, so the throwing paths cannot be the ones that leak.
    /// </remarks>
    [Fact]
    public async Task AcceptDisclaimers_detaches_the_response_handler_even_when_it_fails()
    {
        var surface = OneAcceptWaiting();
        surface.OnClick = (_, _) => { };

        await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupFlows.AcceptDisclaimersAsync(
                surface, null, new WildwoodTestingOptions { AcceptAttempts = 1 }));

        Assert.Equal(1, surface.Subscriptions);
        Assert.Equal(1, surface.Disposals);
    }

    /// <summary>A panel whose Accept goes away after one click is accepted in one click.</summary>
    private static FakeSurface OneAcceptWaiting()
    {
        var surface = new FakeSurface();
        surface.Counts[Accept] = 1;
        surface.VisibleSelectors.Add(AcceptOrRetry);
        surface.OnClick = (page, selector) =>
        {
            if (selector == Accept) page.Counts[Accept] = 0;
        };
        return surface;
    }

    #endregion

    #region The watched response

    /// <summary>
    /// Both the direct API path and the proxied one count; an unrelated response does not.
    /// </summary>
    /// <remarks>
    /// A browser-side stack POSTs <c>disclaimeracceptance/accept</c> (or <c>accept-bulk</c>, which
    /// the same pattern matches); Razor proxies acceptance through <c>disclaimer-gate/accept</c>.
    /// </remarks>
    [Theory]
    [InlineData("https://api.test/api/disclaimeracceptance/accept", 429)]
    [InlineData("https://api.test/api/disclaimeracceptance/accept-bulk", 429)]
    [InlineData("https://app.test/api/wildwood-regsub/disclaimer-gate/accept", 429)]
    public void The_watcher_records_an_acceptance_response(string url, int status)
    {
        var watcher = new AcceptResponseWatcher(null);

        watcher.Record(url, status);

        Assert.Equal(status, watcher.LastStatus);
    }

    [Fact]
    public void The_watcher_ignores_everything_else_the_page_fetched()
    {
        var watcher = new AcceptResponseWatcher(null);

        watcher.Record("https://api.test/api/disclaimeracceptance/accept", 429);
        watcher.Record("https://api.test/api/apptiers/public", 200);

        Assert.Equal(429, watcher.LastStatus);

        watcher.Reset();

        Assert.Equal(0, watcher.LastStatus);
    }

    /// <summary>
    /// A host that proxies acceptance elsewhere names its own path, and that REPLACES the default.
    /// </summary>
    [Fact]
    public void The_watcher_honours_a_hosts_own_pattern()
    {
        var watcher = new AcceptResponseWatcher(new Regex("terms/agree"));

        watcher.Record("https://api.test/api/disclaimeracceptance/accept", 429);
        Assert.Equal(0, watcher.LastStatus);

        watcher.Record("https://app.test/terms/agree", 429);
        Assert.Equal(429, watcher.LastStatus);
    }

    #endregion

    #region Finishing

    /// <summary>
    /// The failure text is read in PREFERENCE order, not as one selector list.
    /// </summary>
    /// <remarks>
    /// The class fallback names a paragraph the processing steps share, which on a stack that keeps
    /// every step panel in the DOM sits EARLIER in the document than the real error - so a joined
    /// list would report the boilerplate as the cause of a genuine failure.
    /// </remarks>
    [Fact]
    public async Task FirstPresentText_prefers_the_error_hook_over_the_shared_class()
    {
        var surface = new FakeSurface();
        var hook = WildwoodTestSelectors.SignupFailureMessageSelectors[0];
        var shared = WildwoodTestSelectors.SignupFailureMessageSelectors[1];
        surface.Counts[hook] = 1;
        surface.Counts[shared] = 1;
        surface.Texts[hook] = "Your card was declined.";
        surface.Texts[shared] = "Please wait while we set things up.";

        var message = await WildwoodSignupFlows.FirstPresentTextAsync(
            surface, WildwoodTestSelectors.SignupFailureMessageSelectors);

        Assert.Equal("Your card was declined.", message);
    }

    /// <summary>The class fallback still answers on a build that renders no error hook.</summary>
    [Fact]
    public async Task FirstPresentText_falls_back_when_the_hook_is_absent()
    {
        var surface = new FakeSurface();
        var shared = WildwoodTestSelectors.SignupFailureMessageSelectors[1];
        surface.Counts[shared] = 1;
        surface.Texts[shared] = "Something went wrong.";

        var message = await WildwoodSignupFlows.FirstPresentTextAsync(
            surface, WildwoodTestSelectors.SignupFailureMessageSelectors);

        Assert.Equal("Something went wrong.", message);
    }

    /// <summary>Nothing on screen is an empty message, not a failure of its own.</summary>
    [Fact]
    public async Task FirstPresentText_answers_empty_when_nothing_matched()
    {
        Assert.Equal(
            string.Empty,
            await WildwoodSignupFlows.FirstPresentTextAsync(
                new FakeSurface(), WildwoodTestSelectors.SignupFailureMessageSelectors));
    }

    /// <summary>
    /// A failure is retried by clicking Try Again, and the flow finishes on the next pass.
    /// </summary>
    /// <remarks>
    /// The flow tracks completed sub-steps, so its retry resumes where it failed rather than
    /// registering the user a second time.
    /// </remarks>
    [Fact]
    public async Task FinishSignup_retries_a_failed_processing_step()
    {
        var surface = SuccessPanel();
        var steps = new StepScript("failed", "success");
        surface.Counts[WildwoodTestSelectors.SignupFailureMessageSelectors[0]] = 1;

        await WildwoodSignupFlows.FinishSignupAsync(surface, steps.ReadAsync, null, null, Quick());

        Assert.Equal(
            new[]
            {
                "click " + WildwoodTestSelectors.SignupRetrySelector,
                "click " + WildwoodTestSelectors.SignupGetStartedSelector
            },
            surface.Actions);
    }

    /// <summary>
    /// The retry budget is spent, then the failure is reported with what the panel said.
    /// </summary>
    [Fact]
    public async Task FinishSignup_gives_up_after_the_retry_budget_and_quotes_the_panel()
    {
        var surface = new FakeSurface();
        var steps = new StepScript("failed", "creating", "failed");
        var hook = WildwoodTestSelectors.SignupFailureMessageSelectors[0];
        surface.Counts[hook] = 1;
        surface.Texts[hook] = "Your card was declined.";

        var options = Quick();
        options.SignupRetries = 1;

        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupFlows.FinishSignupAsync(surface, steps.ReadAsync, null, null, options));

        Assert.Equal("Signup processing failed: Your card was declined.", failure.Message);
        Assert.Equal(1, surface.Actions.Count(action => action == "click " + WildwoodTestSelectors.SignupRetrySelector));
    }

    /// <summary>
    /// The failed step has to be LEFT before the flow is polled again.
    /// </summary>
    /// <remarks>
    /// Without this wait the next poll re-reads the same failure, which is still on screen, and
    /// burns a retry on it - so a two-retry budget would be spent on one failure.
    /// </remarks>
    [Fact]
    public async Task FinishSignup_waits_for_the_failed_step_to_be_left_before_polling_again()
    {
        var surface = new FakeSurface();
        surface.Counts[WildwoodTestSelectors.SignupFailureMessageSelectors[0]] = 1;

        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupFlows.FinishSignupAsync(
                surface, new StepScript("failed").ReadAsync, null, null, Quick()));

        Assert.Contains("stayed on the \"failed\" step", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, surface.Actions.Count(action => action == "click " + WildwoodTestSelectors.SignupRetrySelector));
    }

    /// <summary>
    /// A flow that goes nowhere names the step it is stuck on.
    /// </summary>
    /// <remarks>
    /// "Timed out" on its own reads as the product hanging, when it usually means a step is spelled
    /// differently or a hook was never rendered.
    /// </remarks>
    [Fact]
    public async Task FinishSignup_names_the_step_it_never_left()
    {
        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupFlows.FinishSignupAsync(
                new FakeSurface(), new StepScript("creating").ReadAsync, null, null, Quick()));

        Assert.Contains("never reached success, disclaimers or a failure", failure.Message, StringComparison.Ordinal);
        Assert.Contains("it is on \"creating\"", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A flow that stops on the disclaimers step accepts them, scoped to the disclaimers panel.
    /// </summary>
    /// <remarks>
    /// The scope matters: the accept loop's class fallbacks are loose, and the page also hosts the
    /// success panel's own primary button.
    /// </remarks>
    [Fact]
    public async Task FinishSignup_accepts_the_disclaimers_it_stops_on()
    {
        var surface = new FakeSurface();
        var steps = new StepScript("disclaimers", "success");
        surface.Counts[Accept] = 1;
        surface.VisibleSelectors.Add(AcceptOrRetry);
        surface.OnClick = (page, selector) =>
        {
            if (selector != Accept) return;
            page.Counts[Accept] = 0;
            page.VisibleSelectors.Add(".ww-signup-success");
        };

        await WildwoodSignupFlows.FinishSignupAsync(surface, steps.ReadAsync, null, null, Quick());

        Assert.Contains(".ww-signup-disclaimers", surface.Scopes);
        Assert.Equal(
            new[] { "click " + Accept, "click " + WildwoodTestSelectors.SignupGetStartedSelector },
            surface.Actions.Where(action => action.StartsWith("click ", StringComparison.Ordinal)).ToArray());
    }

    /// <summary>
    /// A disclaimers step that renders neither control nor the success panel says exactly that.
    /// </summary>
    [Fact]
    public async Task FinishSignup_fails_when_the_disclaimers_step_renders_nothing()
    {
        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupFlows.FinishSignupAsync(
                new FakeSurface(), new StepScript("disclaimers").ReadAsync, null, null, Quick()));

        Assert.Contains(
            "rendered neither an Accept, its retry, nor the success panel",
            failure.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Accepting the disclaimers is not the finish: the flow still has to reach success.
    /// </summary>
    /// <remarks>
    /// The accept loop stops as soon as nothing is left to accept, which can be a beat before the
    /// component has moved on - so the wait that follows it is what makes the driver's answer mean
    /// "the signup finished" rather than "the disclaimers are done".
    /// </remarks>
    [Fact]
    public async Task FinishSignup_still_waits_for_success_once_the_disclaimers_are_done()
    {
        var surface = new FakeSurface();
        surface.VisibleSelectors.Add(".ww-signup-success");

        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupFlows.FinishSignupAsync(
                surface, new StepScript("disclaimers", "creating").ReadAsync, null, null, Quick()));

        Assert.Contains("never reached the \"success\" step", failure.Message, StringComparison.Ordinal);
        Assert.Contains("it is on \"creating\"", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The success text is asserted before the click that navigates away from the panel.
    /// </summary>
    /// <remarks>
    /// It cannot be left to the caller: the panel is the only place the completion message appears,
    /// the final click leaves it, and the disclaimers step can sit between payment and success - so
    /// a caller asserting straight after the card sees the disclaimer instead.
    /// </remarks>
    [Fact]
    public async Task FinishSignup_asserts_the_success_text_before_clicking_through()
    {
        var surface = SuccessPanel();
        surface.TextShown = false;

        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupFlows.FinishSignupAsync(
                surface, new StepScript("success").ReadAsync, "You are all set", null, Quick()));

        Assert.Equal("the success panel never showed \"You are all set\"", failure.Message);
        Assert.DoesNotContain("click " + WildwoodTestSelectors.SignupGetStartedSelector, surface.Actions);
    }

    /// <summary>The pattern form names the pattern, since there is no literal to quote.</summary>
    [Fact]
    public async Task FinishSignup_names_the_pattern_the_success_panel_never_showed()
    {
        var surface = SuccessPanel();
        surface.TextShown = false;

        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupFlows.FinishSignupAsync(
                surface, new StepScript("success").ReadAsync, null, new Regex("Welcome, .+"), Quick()));

        Assert.Equal("the success panel never showed Welcome, .+", failure.Message);
        Assert.Equal("Welcome, .+", surface.TextWaitedFor);
    }

    [Fact]
    public async Task FinishSignup_clicks_the_success_panels_own_button_last()
    {
        var surface = SuccessPanel();
        var options = Quick();
        options.SuccessTextTimeoutMs = 55;

        await WildwoodSignupFlows.FinishSignupAsync(
            surface, new StepScript("success").ReadAsync, "You are all set", null, options);

        Assert.Equal(
            new[] { "click " + WildwoodTestSelectors.SignupGetStartedSelector },
            surface.Actions.Where(action => action.StartsWith("click ", StringComparison.Ordinal)).ToArray());
        Assert.Equal(new[] { 55 }, surface.Timeouts);
    }

    /// <summary>Nothing is asserted about the panel's text when the caller named none.</summary>
    [Fact]
    public async Task FinishSignup_asserts_no_text_when_the_caller_named_none()
    {
        var surface = SuccessPanel();

        await WildwoodSignupFlows.FinishSignupAsync(
            surface, new StepScript("success").ReadAsync, null, null, Quick());

        Assert.DoesNotContain(surface.Actions, action => action.StartsWith("waitText", StringComparison.Ordinal));
    }

    /// <summary>A page showing only the success panel - nothing pending, nothing failed.</summary>
    private static FakeSurface SuccessPanel()
    {
        var surface = new FakeSurface();
        surface.VisibleSelectors.Add(".ww-signup-success");
        return surface;
    }

    #endregion

    /// <summary>A step reader that answers the given values in turn, then repeats the last one.</summary>
    private sealed class StepScript
    {
        private readonly string?[] _values;
        private int _index;

        internal StepScript(params string?[] values)
        {
            _values = values;
        }

        internal Task<string?> ReadAsync()
        {
            var value = _values[_index];
            if (_index < _values.Length - 1) _index++;
            return Task.FromResult(value);
        }
    }

    /// <summary>
    /// The seam, scripted: what matches, what is visible, what one click changes.
    /// </summary>
    /// <remarks>
    /// Reads answer from the dictionaries, actions are appended to <see cref="Actions"/> in order -
    /// which is what lets a test assert that the gate was ticked BEFORE the click rather than only
    /// that both happened. <see cref="Scope"/> answers with the same surface, so a test scripts one
    /// page rather than a tree of them; <see cref="Scopes"/> records what was scoped to.
    /// </remarks>
    private sealed class FakeSurface : IFlowSurface
    {
        internal List<string> Actions { get; } = new();

        internal List<string> Scopes { get; } = new();

        internal Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

        internal HashSet<string> VisibleSelectors { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, string?> Texts { get; } = new(StringComparer.Ordinal);

        internal HashSet<string> Ticked { get; } = new(StringComparer.Ordinal);

        /// <summary>Every budget the drivers handed down, in the order they spent them.</summary>
        /// <remarks>
        /// Fifteen interchangeable int budgets are fifteen chances to hand down the wrong one, and
        /// the values never reach the action log - so they are recorded here instead.
        /// </remarks>
        internal List<int> Timeouts { get; } = new();

        internal Action<FakeSurface, string>? OnClick { get; set; }

        internal AcceptResponseWatcher? Watcher { get; private set; }

        internal int Subscriptions { get; private set; }

        internal int Disposals { get; private set; }

        internal bool TextShown { get; set; } = true;

        internal string? TextWaitedFor { get; private set; }

        public IFlowSurface Scope(string selector)
        {
            Scopes.Add(selector);
            return this;
        }

        public Task FillAsync(string selector, string value)
        {
            Actions.Add("fill " + selector + "=" + value);
            return Task.CompletedTask;
        }

        public Task ClickAsync(string selector)
        {
            Actions.Add("click " + selector);
            OnClick?.Invoke(this, selector);
            return Task.CompletedTask;
        }

        public Task<int> CountAsync(string selector)
        {
            return Task.FromResult(Counts.TryGetValue(selector, out var count) ? count : 0);
        }

        public Task<bool> IsVisibleAsync(string selector)
        {
            return Task.FromResult(VisibleSelectors.Contains(selector));
        }

        public Task<bool> WaitForVisibleAsync(string selector, int timeoutMs)
        {
            Actions.Add("waitVisible " + selector);
            Timeouts.Add(timeoutMs);
            return Task.FromResult(VisibleSelectors.Contains(selector));
        }

        public Task<bool> WaitForHiddenAsync(string selector, int timeoutMs)
        {
            Actions.Add("waitHidden " + selector);
            Timeouts.Add(timeoutMs);
            return Task.FromResult(!VisibleSelectors.Contains(selector));
        }

        public Task<string?> TextAsync(string selector)
        {
            return Task.FromResult(Texts.TryGetValue(selector, out var text) ? text : null);
        }

        public Task<bool> IsCheckedAsync(string selector, int index)
        {
            return Task.FromResult(Ticked.Contains(Box(selector, index)));
        }

        public Task<bool> CheckAsync(string selector, int index, int timeoutMs)
        {
            Actions.Add("check " + Box(selector, index));
            Timeouts.Add(timeoutMs);
            Ticked.Add(Box(selector, index));
            return Task.FromResult(true);
        }

        public Task<bool> WaitForTextAsync(string? text, Regex? pattern, int timeoutMs)
        {
            TextWaitedFor = pattern?.ToString() ?? text;
            Actions.Add("waitText " + TextWaitedFor);
            Timeouts.Add(timeoutMs);
            return Task.FromResult(TextShown);
        }

        public Task DelayAsync(int ms)
        {
            Actions.Add("delay " + ms);
            return Task.CompletedTask;
        }

        public IDisposable WatchAcceptResponses(AcceptResponseWatcher watcher)
        {
            Subscriptions++;
            Watcher = watcher;
            return new Subscription(this);
        }

        private static string Box(string selector, int index)
        {
            return selector + "#" + index;
        }

        private sealed class Subscription : IDisposable
        {
            private readonly FakeSurface _surface;

            internal Subscription(FakeSurface surface)
            {
                _surface = surface;
            }

            public void Dispose()
            {
                _surface.Disposals++;
            }
        }
    }
}
