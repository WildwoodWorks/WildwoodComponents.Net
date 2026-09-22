using Microsoft.Playwright;
using WildwoodComponents.Shared.RegistrationSubscription;

namespace WildwoodComponents.Testing.Smoke;

// What a browser proves about WildwoodComponents.Testing that WildwoodComponents.Tests cannot.
//
// The unit suite exercises every rule in these drivers behind the IFlowSurface seam: the preference
// order, the bounded budgets, the 429 back-off, the gate tick before the click, the response
// handler coming back off the page. What it cannot exercise is the seam ITSELF - one locator call
// per operation - and the browser behaviours those calls lean on. Each check below names which of
// those it is standing on.
//
// Budgets are cut down throughout (a 250 ms rate-limit pause against the shipped 20 s, a 10 s
// processing window against the shipped 120 s). That is the reason WildwoodTestingOptions exists,
// and it is why a run that proves a bound is reached takes seconds rather than minutes. The rule
// being proved is the same one either way; only the number is smaller.

/// <summary>One named check, and the page state it needs.</summary>
internal sealed record ContractCheck(string Name, Func<CheckContext, Task> RunAsync);

/// <summary>What a check is handed: a fresh page, the fixture server, and where it lives.</summary>
internal sealed class CheckContext
{
    internal required IPage Page { get; init; }

    internal required string BaseUrl { get; init; }

    internal required FixtureServer Server { get; init; }

    internal Task<IResponse?> GoToSignupAsync()
    {
        return Page.GotoAsync(BaseUrl + "/signup", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    }

    internal Task ShowAsync(string step)
    {
        return Page.EvaluateAsync("step => window.__wwShow(step)", step);
    }

    internal Task<int> ClicksAsync(string control)
    {
        return Page.EvaluateAsync<int>("name => window.__wwClicks[name]", control);
    }
}

/// <summary>The checks, in the order a run reports them.</summary>
internal static class ContractChecks
{
    /// <summary>A registration this run can fill in without colliding with anything.</summary>
    private static readonly RegistrationFields User = new()
    {
        FirstName = "Ada",
        LastName = "Lovelace",
        Username = "ada.smoke",
        Email = "ada.smoke@example.invalid",
        Password = "Correct-Horse-9!"
    };

    internal static IReadOnlyList<ContractCheck> All { get; } = new[]
    {
        new ContractCheck("the step reader answers every contract name on the Razor shape", EveryStepNameAsync),
        new ContractCheck("a wait refuses to return while the flow is still on the step", StaysOnStepAsync),
        new ContractCheck("a wait ends because the step changed, not because it had already", RealTransitionAsync),
        new ContractCheck("the form is filled through data-ww-field, with no ids to fall back on", FillsByHookAsync),
        new ContractCheck("submit is found by its action hook where there is no form behind it", SubmitsByHookAsync),
        new ContractCheck("the failure text is read from its own hook, not the shared boilerplate", ReadsFailureTextAsync),
        new ContractCheck("Accept is never mistaken for a retry, and its gate is ticked first", AcceptsDisclaimersAsync),
        new ContractCheck("the accept loop waits out an HTTP 429 and carries on", BacksOffOnRateLimitAsync),
        new ContractCheck("Try Again is scoped off the view root, and the signup finishes", FinishesSignupAsync),
        new ContractCheck("the recorder sees the steps entered, and none that were not", RecordsStepsAsync),
        new ContractCheck("the consent banner is answered, and its absence is not a failure", AnswersConsentAsync),
        new ContractCheck("the manage reader answers every plan-change name", EveryManageStepAsync)
    };

    /// <summary>
    /// Every published step name is readable where eleven other panels each carry one of their own.
    /// </summary>
    /// <remarks>
    /// The browser behaviour on trial is document order: <c>SignupStepSelector</c> is a list whose
    /// first arm is the root's mirrored step and whose second is a panel's, and <c>.First</c>
    /// resolves it in DOM order rather than in list order. On this shape every one of the twelve
    /// panels matches the second arm, so a reader that lost the ordering would answer "loading"
    /// forever - and every wait in every host's suite would hang while every "wait for it to leave"
    /// returned at once.
    /// </remarks>
    private static async Task EveryStepNameAsync(CheckContext context)
    {
        await context.GoToSignupAsync();

        // The page starts on `loading` and moves itself on, so this first wait also proves the
        // reader survives a step it did not begin on.
        await WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.Register, 10_000);

        foreach (var name in FixturePages.SignupStepNames)
        {
            await context.ShowAsync(name);
            Expect.Equal(
                name,
                await WildwoodSignupSteps.SignupStepAsync(context.Page),
                "the reader did not answer with the step the page is showing.");
        }

        // Every member of the machine's enum is spelled by one of the names above. The list is
        // written out independently of StepNames (see the FixturePages header), so this is what stops
        // the walk agreeing with itself: a step added to the machine, or renamed in the table, fails
        // here rather than quietly going undriven.
        foreach (var step in Enum.GetValues<SignupStep>())
        {
            Expect.True(
                Array.IndexOf(FixturePages.SignupStepNames, StepNames.ForSignup(step)) >= 0,
                "StepNames spells " + step + " as \"" + StepNames.ForSignup(step)
                + "\", which is not one of the twelve names the DOM contract publishes.");
        }

        // The two names that are NOT the enum member lower-cased, so the StepNames table is pinned
        // against the DOM rather than against itself.
        await context.ShowAsync("success");
        await WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.Done, 5_000);

        await context.ShowAsync("packCheckout");
        await WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.PackCheckout, 5_000);
    }

    /// <summary>
    /// The single most important behaviour in the package, and what it says when it gives up.
    /// </summary>
    /// <remarks>
    /// A reader that has stopped resolving answers null, null is not the step being waited on, and
    /// every "wait for it to leave" then returns immediately - so whole suites pass while asserting
    /// nothing. Proving the wait REFUSES to return while the flow is still on the step is the only
    /// way to catch that.
    /// </remarks>
    private static async Task StaysOnStepAsync(CheckContext context)
    {
        await context.GoToSignupAsync();
        await WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.Register, 10_000);

        var stayed = await Expect.FailureAsync(
            () => WildwoodSignupSteps.WaitForSignupStepToLeaveAsync(context.Page, SignupStep.Register, 2_000),
            "the wait returned while the flow was still on the register step.");

        Expect.Equal(
            "the signup stayed on the \"register\" step (waited 2s)",
            stayed.Message,
            "the \"stayed on the step\" failure no longer says what it was waiting for.");

        var never = await Expect.FailureAsync(
            () => WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.Done, 1_000),
            "the wait reported reaching a step the page never showed.");

        Expect.Equal(
            "the signup never reached the \"success\" step - it is on \"register\" (waited 1s)",
            never.Message,
            "the \"never reached\" failure no longer names the step the page is on.");
    }

    /// <summary>The wait ends on a real transition, armed before the transition happens.</summary>
    private static async Task RealTransitionAsync(CheckContext context)
    {
        await context.GoToSignupAsync();
        await WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.Register, 10_000);

        // Armed first, moved second: what is proved is that the wait ended because the step changed,
        // not that it was already off `register` when anyone asked.
        var left = WildwoodSignupSteps.WaitForSignupStepToLeaveAsync(context.Page, SignupStep.Register, 10_000);
        await context.Page.EvaluateAsync("() => setTimeout(() => window.__wwShow('plan'), 300)");
        await left;

        await WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.Plan, 5_000);
    }

    /// <summary>
    /// Every field is filled through <c>data-ww-field</c>, with no id anywhere to fall back on.
    /// </summary>
    /// <remarks>
    /// The React ids are a fallback the selector accepts, and a server-rendered surface cannot offer
    /// them: Razor suffixes its ids per component instance. This page carries none, which is the
    /// state those surfaces are permanently in.
    /// </remarks>
    private static async Task FillsByHookAsync(CheckContext context)
    {
        await context.GoToSignupAsync();
        await WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.Register, 10_000);

        await WildwoodSignupFlows.FillRegistrationFormAsync(context.Page, User);

        Expect.Equal(0, await context.Page.Locator("[data-ww-field][id]").CountAsync(),
            "a field grew an id, so these assertions would no longer be about the contract hook.");

        await ExpectFieldAsync(context, "firstName", User.FirstName);
        await ExpectFieldAsync(context, "lastName", User.LastName);
        await ExpectFieldAsync(context, "username", User.Username);
        await ExpectFieldAsync(context, "email", User.Email);
        await ExpectFieldAsync(context, "password", User.Password);

        // The confirmation takes the password itself - the one value the driver decides rather than
        // being handed.
        await ExpectFieldAsync(context, "confirmPassword", User.Password);
    }

    /// <summary>
    /// The submit is reached by its action hook where <c>button[type="submit"]</c> cannot reach it.
    /// </summary>
    /// <remarks>
    /// The pay-first surfaces take the card before the account exists, so there is no
    /// <c>&lt;form&gt;</c> to submit and the control is an ordinary <c>type="button"</c>.
    /// </remarks>
    private static async Task SubmitsByHookAsync(CheckContext context)
    {
        await context.GoToSignupAsync();
        await WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.Register, 10_000);

        Expect.Equal(0, await context.Page.Locator("form").CountAsync(),
            "the fixture grew a form, so this no longer proves the action hook was what was used.");
        Expect.Equal(1, await context.Page.Locator(WildwoodTestSelectors.SubmitRegisterSelector).CountAsync(),
            "the submit selector no longer resolves to exactly one control on this shape.");

        await WildwoodSignupFlows.SubmitRegistrationFormAsync(context.Page);

        await WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.Creating, 5_000);
        Expect.Equal(1, await context.ClicksAsync("submitRegister"),
            "the click did not land on the register submit.");
    }

    /// <summary>
    /// The failure is reported from <c>data-ww-error-message</c>, not from the boilerplate that sits
    /// earlier in the document.
    /// </summary>
    /// <remarks>
    /// This is the rule the failure-message selectors are an ORDERED list for. A CSS selector list
    /// resolves in document order, so a comma-joined form would hand back the <c>creating</c> panel's
    /// "this will only take a moment" - which on this shape is still in the document, and earlier
    /// than the real error. Only a browser can decide that ordering.
    /// </remarks>
    private static async Task ReadsFailureTextAsync(CheckContext context)
    {
        await context.GoToSignupAsync();
        await WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.Register, 10_000);
        await WildwoodSignupFlows.SubmitRegistrationFormAsync(context.Page);
        await WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.Failed, 10_000);

        var options = new WildwoodTestingOptions { SignupRetries = 0, ProcessingTimeoutMs = 10_000 };

        var failed = await Expect.FailureAsync(
            () => WildwoodSignupFlows.FinishSignupAsync(context.Page, options),
            "the finish driver reported success on a signup that failed.");

        Expect.Equal(
            "Signup processing failed: " + FixturePages.FailureText,
            failed.Message,
            "the reported cause is not the text the failed panel's own hook carries.");
        Expect.True(
            !failed.Message.Contains(FixturePages.ProcessingBoilerplate, StringComparison.Ordinal),
            "the reported cause is the boilerplate the processing panels share.");
        Expect.Equal(0, await context.ClicksAsync("retry"),
            "Try Again was clicked although no retries were allowed.");
    }

    /// <summary>
    /// An Accept that names itself is never taken for the load retry, and its gate is ticked first.
    /// </summary>
    /// <remarks>
    /// Two browser behaviours at once. The retry fallback is
    /// <c>.ww-disclaimer-actions .ww-btn-primary:not(.ww-btn-block):not([data-ww-disclaimer-action])</c>
    /// and this page is the markup it was tightened for - an Accept All sitting in
    /// <c>.ww-disclaimer-actions</c> as a plain <c>.ww-btn-primary</c>. Without the exclusion the
    /// loop clicks Accept as though it were a retry and then reports that disclaimers which loaded
    /// perfectly well never loaded. And Accept ships DISABLED here: a click waits for actionability
    /// rather than failing, so without the gate tick the driver hangs on the button.
    /// </remarks>
    private static async Task AcceptsDisclaimersAsync(CheckContext context)
    {
        context.Server.RateLimitedAccepts = 0;

        await context.GoToSignupAsync();
        await WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.Register, 10_000);
        await context.ShowAsync("disclaimers");

        Expect.Equal(0, await context.Page.Locator(WildwoodTestSelectors.RetrySelector).CountAsync(),
            "the retry selector matches the Accept button, which is the bug it was tightened for.");
        Expect.True(
            !await context.Page.Locator(WildwoodTestSelectors.AcceptSelector).First.IsEnabledAsync(),
            "Accept is not gated here, so ticking the gate first proves nothing.");

        await WildwoodSignupFlows.AcceptDisclaimersAsync(
            context.Page.Locator(".ww-signup-disclaimers"),
            () => context.Page.Locator(".ww-signup-success").IsVisibleAsync(),
            new WildwoodTestingOptions { AcceptPauseMs = 600, DisclaimerSettleMs = 5_000 });

        Expect.Equal(1, await context.ClicksAsync("accept"), "Accept was not clicked exactly once.");
        Expect.Equal("success", await WildwoodSignupSteps.SignupStepAsync(context.Page),
            "accepting did not move the flow on.");
    }

    /// <summary>
    /// A refused acceptance is waited out rather than spending the click budget on refusals.
    /// </summary>
    /// <remarks>
    /// The back-off itself is unit-tested behind the seam; what a browser adds is the other half of
    /// it. <c>PlaywrightFlowSurface.WatchAcceptResponses</c> subscribes to the page's response event,
    /// the staged Accept makes a genuine same-origin POST, and the status that reaches the watcher is
    /// the browser's own report of it. Nothing without a browser can show that wiring works.
    /// </remarks>
    private static async Task BacksOffOnRateLimitAsync(CheckContext context)
    {
        context.Server.RateLimitedAccepts = 2;

        await context.GoToSignupAsync();
        await WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.Register, 10_000);
        await context.ShowAsync("disclaimers");

        await WildwoodSignupFlows.AcceptDisclaimersAsync(
            context.Page.Locator(".ww-signup-disclaimers"),
            () => context.Page.Locator(".ww-signup-success").IsVisibleAsync(),
            new WildwoodTestingOptions
            {
                AcceptPauseMs = 600,
                DisclaimerSettleMs = 5_000,
                RateLimitPauseMs = 250,
                // One click, so a refusal that was allowed to spend the click budget would fail the
                // run rather than be absorbed by a generous one.
                AcceptAttempts = 1
            });

        Expect.Equal(3, await context.ClicksAsync("accept"),
            "the two refused clicks and the one that succeeded did not all happen.");
        Expect.Equal(0, context.Server.RateLimitedAccepts, "the staged rate limit was not spent.");
        Expect.Equal("success", await WildwoodSignupSteps.SignupStepAsync(context.Page),
            "the flow did not reach success after the rate limit lifted.");
    }

    /// <summary>
    /// The whole finish: a failure, a retry that lands on Try Again, disclaimers, success.
    /// </summary>
    /// <remarks>
    /// The scoping rule is what this is really about. Every step-scoped selector carries
    /// <c>:not([data-ww-view])</c> because the root carries the active step too, so
    /// <c>[data-ww-step="failed"] .ww-btn-primary</c> would match every primary button under the root
    /// the moment the flow is on <c>failed</c> - including the hidden register panel's submit, which
    /// precedes the real Try Again in document order. The first match would then be a hidden button
    /// and the click would wait on it until the budget ran out: a hang, on the very shape the
    /// mirroring exists to support. The register click count is what proves where the click landed.
    /// </remarks>
    private static async Task FinishesSignupAsync(CheckContext context)
    {
        context.Server.RateLimitedAccepts = 1;

        await context.GoToSignupAsync();
        await WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.Register, 10_000);
        await WildwoodSignupFlows.FillRegistrationFormAsync(context.Page, User);
        await WildwoodSignupFlows.SubmitRegistrationFormAsync(context.Page);

        await WildwoodSignupFlows.FinishSignupAsync(
            context.Page,
            FixturePages.SuccessText,
            new WildwoodTestingOptions
            {
                ProcessingTimeoutMs = 15_000,
                SignupRetries = 1,
                LeaveFailedTimeoutMs = 5_000,
                SuccessTimeoutMs = 10_000,
                SuccessTextTimeoutMs = 5_000,
                DisclaimerSettleMs = 5_000,
                DisclaimerRenderTimeoutMs = 10_000,
                AcceptPauseMs = 600,
                RateLimitPauseMs = 250
            });

        Expect.Equal(1, await context.ClicksAsync("submitRegister"),
            "the register submit was clicked again, so Try Again reached the wrong button.");
        Expect.Equal(1, await context.ClicksAsync("retry"), "Try Again was not clicked exactly once.");
        Expect.Equal(2, await context.ClicksAsync("accept"),
            "the refused accept and the one that succeeded did not both happen.");
        Expect.Equal(1, await context.ClicksAsync("getStarted"),
            "the success panel's own control was not the one clicked.");
    }

    /// <summary>
    /// The recorder sees every step entered, in order, and answers honestly about one that was not.
    /// </summary>
    /// <remarks>
    /// Installed as an init script so the observer is already running before the page paints - the
    /// one thing polling after the fact cannot do, because it cannot tell "never entered" from
    /// "entered and left". The negative assertion is checked BOTH ways round: a recorder that
    /// collected nothing would satisfy <c>ExpectNeverEntered</c> for every step there is, so the
    /// check also proves it refuses a step that WAS entered.
    /// </remarks>
    private static async Task RecordsStepsAsync(CheckContext context)
    {
        var recorder = await WildwoodSignupSteps.RecordSignupStepsAsync(context.Page);

        await context.GoToSignupAsync();
        await WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.Register, 10_000);
        await WildwoodSignupFlows.SubmitRegistrationFormAsync(context.Page);
        await WildwoodSignupSteps.WaitForSignupStepAsync(context.Page, SignupStep.Failed, 10_000);

        var seen = await recorder.StepsAsync();
        var settled = new List<string>();
        foreach (var step in seen)
        {
            if (step is "register" or "creating" or "failed") settled.Add(step);
        }

        Expect.Equal("register -> creating -> failed", string.Join(" -> ", settled),
            "the recorder did not see the steps this walk went through, in order.\n    it saw: "
            + string.Join(" -> ", seen));

        await recorder.ExpectNeverEnteredAsync(SignupStep.Payment);

        var entered = await Expect.FailureAsync(
            () => recorder.ExpectNeverEnteredAsync(SignupStep.Register),
            "the recorder accepted a step that the walk really did enter.");

        Expect.True(
            entered.Message.Contains("steps seen:", StringComparison.Ordinal),
            "the recorder's refusal no longer names the route the flow took.");
    }

    /// <summary>
    /// The banner is answered and waited out, and a page with none is not reported as a failure.
    /// </summary>
    private static async Task AnswersConsentAsync(CheckContext context)
    {
        await context.Page.GotoAsync(
            context.BaseUrl + "/consent",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await WildwoodSignupFlows.DismissConsentBannerAsync(
            context.Page, new WildwoodTestingOptions { ConsentBannerWaitMs = 5_000, ConsentBannerHiddenMs = 5_000 });

        Expect.Equal(1, await context.Page.EvaluateAsync<int>("() => window.__wwConsentClicks"),
            "the banner's own accept was not what was clicked.");

        // Most environments configure no banner at all, so the budget is spent and the driver returns
        // having done nothing. A driver that threw here would fail every such environment.
        await context.GoToSignupAsync();
        await WildwoodSignupFlows.DismissConsentBannerAsync(
            context.Page, new WildwoodTestingOptions { ConsentBannerWaitMs = 500 });
    }

    /// <summary>
    /// Every plan-change name is readable from the manage root, and "any of these" says which.
    /// </summary>
    /// <remarks>
    /// The manage reader keys on the root, where every stack puts the step, so this is the simpler
    /// of the two shapes. <c>WaitForAnyManageStepAsync</c> exists because legitimate outcomes diverge
    /// - a priced change with no trial never leaves <c>collectingPayment</c> when it is refused,
    /// while a trial's refusal lands on <c>failed</c> - so the step it REACHED is the answer, not
    /// just that it reached one.
    /// </remarks>
    private static async Task EveryManageStepAsync(CheckContext context)
    {
        await context.Page.GotoAsync(
            context.BaseUrl + "/manage",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        foreach (var name in FixturePages.PlanChangeStepNames)
        {
            await context.Page.EvaluateAsync("step => window.__wwShowManage(step)", name);
            Expect.Equal(name, await WildwoodSignupSteps.ManageStepAsync(context.Page),
                "the manage reader did not answer with the step the root is carrying.");
        }

        foreach (var step in Enum.GetValues<PlanChangeStep>())
        {
            Expect.True(
                Array.IndexOf(FixturePages.PlanChangeStepNames, StepNames.ForPlanChange(step)) >= 0,
                "StepNames spells " + step + " as \"" + StepNames.ForPlanChange(step)
                + "\", which is not one of the nine names the DOM contract publishes.");
        }

        await context.Page.EvaluateAsync("() => window.__wwShowManage('collectingPayment')");
        await WildwoodSignupSteps.WaitForManageStepAsync(context.Page, PlanChangeStep.CollectingPayment, 5_000);

        await context.Page.EvaluateAsync("() => window.__wwShowManage('idle')");
        var reached = await WildwoodSignupSteps.WaitForAnyManageStepAsync(
            context.Page,
            new[] { PlanChangeStep.Confirm, PlanChangeStep.Idle, PlanChangeStep.Failed },
            5_000);

        Expect.True(reached == PlanChangeStep.Idle, "the wait did not report which step it reached.");
    }

    private static async Task ExpectFieldAsync(CheckContext context, string field, string expected)
    {
        var actual = await context.Page.Locator("[data-ww-field=\"" + field + "\"]").InputValueAsync();
        Expect.Equal(expected, actual, "the \"" + field + "\" field was not filled with what it was given.");
    }
}
