using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Testing;

namespace WildwoodComponents.Tests.Testing;

/// <summary>
/// The waiting rules of the Playwright step helpers, exercised without a browser.
/// </summary>
/// <remarks>
/// <para>
/// Every public entry point takes an <c>IPage</c> and none of them can run here - there is no
/// browser in this repository's CI, and a helper that needed one could only be proved by the
/// end-to-end smoke spec. So the rules themselves sit behind an internal overload that takes a step
/// READER, and these drive that: which value ends a wait, what a failure says, and which enum
/// member a "wait for any of these" answers with.
/// </para>
/// <para>
/// What is deliberately NOT covered here is anything that needs a page: reading the attribute, the
/// init script actually running, and the recorder's round trip through the browser.
/// </para>
/// </remarks>
public class WildwoodSignupStepsTests
{
    /// <summary>A step reader that answers the given values in turn, then repeats the last one.</summary>
    private static Func<Task<string?>> Reads(params string?[] values)
    {
        var index = 0;
        return () =>
        {
            var value = values[index];
            if (index < values.Length - 1) index++;
            return Task.FromResult(value);
        };
    }

    #region Signup

    /// <summary>
    /// A wait for <see cref="SignupStep.Done"/> ends on the DOM's <c>success</c>.
    /// </summary>
    /// <remarks>
    /// The one step whose DOM name is not its enum name, and the whole reason these helpers take
    /// enums rather than strings: a spec asks for the step the machine has, and the rename to the
    /// name live suites locate by happens in one place, in Shared.
    /// </remarks>
    [Fact]
    public async Task WaitForSignupStep_EndsOnTheNameTheDomPublishes()
    {
        await WildwoodSignupSteps.WaitForSignupStepAsync(
            Reads("creating", "success"), SignupStep.Done, timeoutMs: 5_000);
    }

    [Fact]
    public async Task WaitForSignupStep_FailsNamingTheStepWantedAndTheStepSeen()
    {
        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupSteps.WaitForSignupStepAsync(Reads("register"), SignupStep.Plan, timeoutMs: 0));

        Assert.Contains("never reached the \"plan\" step", failure.Message, StringComparison.Ordinal);
        Assert.Contains("it is on \"register\"", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A component that has not painted reads as null, and the failure says so in words.
    /// </summary>
    /// <remarks>
    /// An empty pair of quotes would read as "the attribute is there and blank", which sends the
    /// reader looking at the wrong half of the problem: nothing has rendered at all.
    /// </remarks>
    [Fact]
    public async Task WaitForSignupStep_CallsAnUnpaintedComponentNotRendered()
    {
        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupSteps.WaitForSignupStepAsync(Reads(new string?[] { null }), SignupStep.Register, timeoutMs: 0));

        Assert.Contains("it is on \"(not rendered)\"", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitForSignupStepToLeave_EndsAsSoonAsTheStepChanges()
    {
        await WildwoodSignupSteps.WaitForSignupStepToLeaveAsync(
            Reads("failed", "failed", "register"), SignupStep.Failed, timeoutMs: 5_000);
    }

    /// <summary>
    /// A view that has gone away has left the step.
    /// </summary>
    /// <remarks>
    /// The predicate is "not this step", exactly as JS spells it, so null counts. The alternative -
    /// waiting for another NAMED step - would hang on a flow that navigates away, which is what a
    /// successful signup does.
    /// </remarks>
    [Fact]
    public async Task WaitForSignupStepToLeave_CountsAViewThatIsGone()
    {
        await WildwoodSignupSteps.WaitForSignupStepToLeaveAsync(
            Reads("failed", null), SignupStep.Failed, timeoutMs: 5_000);
    }

    [Fact]
    public async Task WaitForSignupStepToLeave_FailsNamingTheStepItStayedOn()
    {
        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupSteps.WaitForSignupStepToLeaveAsync(Reads("failed"), SignupStep.Failed, timeoutMs: 0));

        Assert.StartsWith("the signup stayed on the \"failed\" step", failure.Message, StringComparison.Ordinal);
    }

    #endregion

    #region Manage

    /// <summary>The plan-change names are camelCase, so a two-word step round-trips.</summary>
    [Fact]
    public async Task WaitForManageStep_EndsOnTheCamelCaseName()
    {
        await WildwoodSignupSteps.WaitForManageStepAsync(
            Reads("previewing", "collectingPayment"), PlanChangeStep.CollectingPayment, timeoutMs: 5_000);
    }

    /// <summary>
    /// "Any of these" answers with the step it reached, not merely that it reached one.
    /// </summary>
    /// <remarks>
    /// The answer is what makes the helper worth having: a priced option with no trial is charged
    /// while the card is collected, so a refusal never leaves <c>collectingPayment</c>, while a
    /// trial saves the card and the prorated charge is confirmed later, so a refusal lands on
    /// <c>failed</c>. A spec has to be told which happened.
    /// </remarks>
    [Fact]
    public async Task WaitForAnyManageStep_AnswersWithTheStepItReached()
    {
        var reached = await WildwoodSignupSteps.WaitForAnyManageStepAsync(
            Reads("changing", "collectingPayment"),
            new[] { PlanChangeStep.Done, PlanChangeStep.CollectingPayment, PlanChangeStep.Failed },
            timeoutMs: 5_000);

        Assert.Equal(PlanChangeStep.CollectingPayment, reached);
    }

    [Fact]
    public async Task WaitForAnyManageStep_FailsListingEveryStepItWouldHaveAccepted()
    {
        var failure = await Assert.ThrowsAsync<WildwoodContractException>(
            () => WildwoodSignupSteps.WaitForAnyManageStepAsync(
                Reads("previewing"),
                new[] { PlanChangeStep.Done, PlanChangeStep.Failed },
                timeoutMs: 0));

        Assert.Contains("[done, failed]", failure.Message, StringComparison.Ordinal);
        Assert.Contains("it is on \"previewing\"", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Waiting for none of them is a spec defect and says so at once.
    /// </summary>
    /// <remarks>
    /// Left to the poll it would spend the whole budget and then report an empty list, which reads
    /// as the component never getting anywhere - the wrong half of the codebase to go looking in.
    /// </remarks>
    [Fact]
    public async Task WaitForAnyManageStep_RefusesAnEmptyList()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => WildwoodSignupSteps.WaitForAnyManageStepAsync(
                Reads("idle"), Array.Empty<PlanChangeStep>(), timeoutMs: 5_000));
    }

    #endregion

    #region The recorder

    [Fact]
    public void EnsureNeverEntered_PassesWhenTheStepWasNeverEntered()
    {
        WildwoodSignupSteps.EnsureNeverEntered(
            new[] { "loading", "token", "creating", "success" }, SignupStep.Payment);
    }

    /// <summary>
    /// The failure names the whole route, because the interesting part is what the flow did instead.
    /// </summary>
    /// <remarks>
    /// This is the assertion a token-grant spec is built on - a granted plan must never ask for a
    /// card - and "it entered payment" on its own does not say whether the token was read at all.
    /// </remarks>
    [Fact]
    public void EnsureNeverEntered_FailsNamingEveryStepThatWasEntered()
    {
        var failure = Assert.Throws<WildwoodContractException>(() => WildwoodSignupSteps.EnsureNeverEntered(
            new[] { "loading", "token", "plan", "payment" }, SignupStep.Payment));

        Assert.Contains("entered the \"payment\" step", failure.Message, StringComparison.Ordinal);
        Assert.Contains("loading -> token -> plan -> payment", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The observer publishes on the property the reader reads back.
    /// </summary>
    /// <remarks>
    /// Two halves of one contract that live in two strings, neither of which the C# compiler looks
    /// inside: rename the property in the script alone and the recorder reports an empty list
    /// forever, which reads as "the flow entered no steps" rather than as a broken helper.
    /// </remarks>
    [Fact]
    public void The_observer_and_the_reader_agree_on_the_window_property()
    {
        Assert.Contains(
            "window['" + WildwoodSignupSteps.StepsWindowKey + "']",
            WildwoodSignupSteps.StepObserverScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "window['" + WildwoodSignupSteps.StepsWindowKey + "']",
            WildwoodSignupSteps.StepsReadExpression,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The observer watches the attribute the step is published in, and only that one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>attributeFilter</c> is what makes watching the whole document affordable: the root
    /// mounts long after an init script runs and a stack may swap the step container out rather
    /// than edit its attribute, so the observer has to be on <c>document.documentElement</c> with
    /// <c>subtree</c> - and without the filter that is every attribute write on the page.
    /// </para>
    /// <para>
    /// It also has to read the same element the waits read, or a spec could be told a step was
    /// never entered that the waits watched the flow pass through.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_observer_watches_the_step_attribute_on_the_element_the_waits_read()
    {
        var script = WildwoodSignupSteps.StepObserverScript;

        Assert.Contains(
            "attributeFilter: ['" + WildwoodTestSelectors.StepAttribute + "']", script, StringComparison.Ordinal);
        Assert.Contains(
            "getAttribute('" + WildwoodTestSelectors.StepAttribute + "')", script, StringComparison.Ordinal);
        Assert.Contains(
            "const selector = '" + WildwoodTestSelectors.SignupStepSelector + "'", script, StringComparison.Ordinal);
        Assert.Contains("document.documentElement", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Repeats are collapsed, so the recorded route is the steps entered and their order.
    /// </summary>
    /// <remarks>
    /// Without this the list is a render log - the same step dozens of times over - and the
    /// "never entered" assertion still works while the route it prints on failure becomes unreadable.
    /// </remarks>
    [Fact]
    public void The_observer_collapses_repeats()
    {
        Assert.Contains(
            "if (step && seen[seen.length - 1] !== step) seen.push(step);",
            WildwoodSignupSteps.StepObserverScript,
            StringComparison.Ordinal);
    }

    #endregion
}
