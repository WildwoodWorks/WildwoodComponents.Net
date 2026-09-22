using Microsoft.Playwright;
using WildwoodComponents.Shared.RegistrationSubscription;

namespace WildwoodComponents.Testing;

// The step hooks the Registration and Subscription component publishes, and the readers for them.
//
// Ported from packages/wildwood-react/src/testing/steps.ts. Copy moves between releases - the plan
// step's heading went from "Choose Your Plan" to "Choose a plan", and every label in the label
// contract is host-configurable - but the step NAMES do not move. A spec that waits on a heading
// reads a reworded heading as a hang, so everything here keys on data-ww-step / data-ww-view
// instead.
//
// Where JS declares two string unions, this takes the machines' own enums from Shared and spells
// them with StepNames, the table the Blazor and Razor views render from. So the names a spec waits
// for and the names the views publish cannot drift apart, and WaitForSignupStepAsync(page,
// SignupStep.Done) cannot be misspelled.
//
// Every public entry point takes an IPage; each one is a thin wrapper over an internal overload
// that takes a step READER instead. That is what lets the waiting rules - the predicates, the
// diagnostics, the "which of these did it reach" answer - be tested in this repo, which has no
// browser in CI.

/// <summary>
/// Reads and waits on the signup flow's and the manage view's published steps.
/// </summary>
public static class WildwoodSignupSteps
{
    /// <summary>
    /// How long a signup step is waited for by default - the same 30 seconds JS waits. A caller
    /// waiting out several server round trips at once (registering, paying, activating) passes its
    /// own.
    /// </summary>
    public const int DefaultSignupTimeoutMs = 30_000;

    /// <summary>
    /// How long a plan-change step is waited for by default. Generous, because a plan change can
    /// include a preview, a card, a bank's 3-D Secure challenge and the completion behind it.
    /// </summary>
    public const int DefaultManageTimeoutMs = 90_000;

    /// <summary>What a read says when the component has not painted yet.</summary>
    private const string NotRendered = "(not rendered)";

    #region Reading

    /// <summary>The signup step on screen, or null before the component has painted.</summary>
    /// <remarks>
    /// Reading an attribute waits for the element first, exactly as Playwright's JS
    /// <c>locator.getAttribute()</c> does, so on a page hosting no signup view each read costs the
    /// page's default timeout before answering null.
    /// </remarks>
    public static Task<string?> SignupStepAsync(IPage page)
    {
        return ReadStepAsync(page, WildwoodTestSelectors.SignupStepSelector);
    }

    /// <summary>The manage view's plan-change step, or null before it has painted.</summary>
    public static Task<string?> ManageStepAsync(IPage page)
    {
        return ReadStepAsync(page, WildwoodTestSelectors.ManageViewSelector);
    }

    private static async Task<string?> ReadStepAsync(IPage page, string selector)
    {
        if (page is null)
        {
            throw new ArgumentNullException(nameof(page));
        }

        try
        {
            return await page.Locator(selector).First
                .GetAttributeAsync(WildwoodTestSelectors.StepAttribute)
                .ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            // Nothing matched, or what matched went away mid-read. Both mean "not painted yet",
            // which is an answer rather than a failure while a component is still mounting - so
            // this swallows exactly what JS's `.catch(() => null)` swallows. The wait that called
            // it is what eventually fails, and it fails saying what it was waiting for.
            return null;
        }
    }

    #endregion

    #region Signup

    /// <summary>Waits for the signup flow to reach one named step.</summary>
    public static Task WaitForSignupStepAsync(IPage page, SignupStep step, int timeoutMs = DefaultSignupTimeoutMs)
    {
        return WaitForSignupStepAsync(() => SignupStepAsync(page), step, timeoutMs);
    }

    /// <summary>Waits for the signup flow to LEAVE a named step.</summary>
    public static Task WaitForSignupStepToLeaveAsync(IPage page, SignupStep step, int timeoutMs = DefaultSignupTimeoutMs)
    {
        return WaitForSignupStepToLeaveAsync(() => SignupStepAsync(page), step, timeoutMs);
    }

    internal static async Task WaitForSignupStepAsync(Func<Task<string?>> readStep, SignupStep step, int timeoutMs)
    {
        var wanted = StepNames.ForSignup(step);

        await Poll.UntilValueAsync<string?>(
            readStep,
            current => string.Equals(current, wanted, StringComparison.Ordinal),
            timeoutMs,
            last => "the signup never reached the \"" + wanted + "\" step - it is on \"" + Seen(last) + "\"")
            .ConfigureAwait(false);
    }

    internal static async Task WaitForSignupStepToLeaveAsync(Func<Task<string?>> readStep, SignupStep step, int timeoutMs)
    {
        var wanted = StepNames.ForSignup(step);

        await Poll.UntilValueAsync<string?>(
            readStep,
            current => !string.Equals(current, wanted, StringComparison.Ordinal),
            timeoutMs,
            _ => "the signup stayed on the \"" + wanted + "\" step")
            .ConfigureAwait(false);
    }

    #endregion

    #region Manage

    /// <summary>Waits for the manage view's plan change to reach one named step.</summary>
    public static Task WaitForManageStepAsync(IPage page, PlanChangeStep step, int timeoutMs = DefaultManageTimeoutMs)
    {
        return WaitForManageStepAsync(() => ManageStepAsync(page), step, timeoutMs);
    }

    /// <summary>
    /// Waits for the plan change to reach any one of several steps, and says which it reached.
    /// </summary>
    /// <remarks>
    /// Needed because legitimate outcomes diverge: a priced option with no trial is charged while
    /// the card is collected, so a refusal never leaves <see cref="PlanChangeStep.CollectingPayment"/>,
    /// while a trial saves the card and the prorated charge is confirmed later, so a refusal lands
    /// on <see cref="PlanChangeStep.Failed"/>. A spec that assumed one of those would be wrong half
    /// the time.
    /// </remarks>
    public static Task<PlanChangeStep> WaitForAnyManageStepAsync(
        IPage page,
        IReadOnlyCollection<PlanChangeStep> steps,
        int timeoutMs = DefaultManageTimeoutMs)
    {
        return WaitForAnyManageStepAsync(() => ManageStepAsync(page), steps, timeoutMs);
    }

    internal static async Task WaitForManageStepAsync(Func<Task<string?>> readStep, PlanChangeStep step, int timeoutMs)
    {
        var wanted = StepNames.ForPlanChange(step);

        await Poll.UntilValueAsync<string?>(
            readStep,
            current => string.Equals(current, wanted, StringComparison.Ordinal),
            timeoutMs,
            last => "the plan change never reached \"" + wanted + "\" - it is on \"" + Seen(last) + "\"")
            .ConfigureAwait(false);
    }

    internal static async Task<PlanChangeStep> WaitForAnyManageStepAsync(
        Func<Task<string?>> readStep,
        IReadOnlyCollection<PlanChangeStep> steps,
        int timeoutMs)
    {
        if (steps is null)
        {
            throw new ArgumentNullException(nameof(steps));
        }

        if (steps.Count == 0)
        {
            // A wait for nothing would poll until the budget ran out and then report an empty list,
            // which reads as a component defect. It is a spec defect, so say so at once.
            throw new ArgumentException("Name at least one step to wait for.", nameof(steps));
        }

        var wanted = new List<string>(steps.Count);
        foreach (var step in steps) wanted.Add(StepNames.ForPlanChange(step));

        var reached = await Poll.UntilValueAsync<string?>(
            readStep,
            current => current is not null && wanted.Contains(current),
            timeoutMs,
            last => "the plan change never reached any of [" + string.Join(", ", wanted)
                + "] - it is on \"" + Seen(last) + "\"")
            .ConfigureAwait(false);

        foreach (var step in steps)
        {
            if (string.Equals(StepNames.ForPlanChange(step), reached, StringComparison.Ordinal)) return step;
        }

        // Unreachable: the poll only returns a value its predicate accepted, and the predicate
        // accepts only a name this same list produced. It is here because the compiler cannot know
        // that, and it says something useful rather than nothing if the two ever disagree.
        throw new WildwoodContractException(
            "the plan change reported the step \"" + Seen(reached) + "\", which is not one of ["
            + string.Join(", ", wanted) + "]");
    }

    #endregion

    #region Recording

    /// <summary>
    /// Starts recording step transitions. Call this BEFORE the first navigation.
    /// </summary>
    /// <remarks>
    /// A spec can only prove a step never happened by watching throughout - polling after the fact
    /// cannot tell "never entered" from "entered and left". The observer is installed as an init
    /// script so that it is already running before the component mounts.
    /// </remarks>
    public static async Task<ISignupStepRecorder> RecordSignupStepsAsync(IPage page)
    {
        if (page is null)
        {
            throw new ArgumentNullException(nameof(page));
        }

        await page.AddInitScriptAsync(StepObserverScript).ConfigureAwait(false);
        return new PageSignupStepRecorder(page);
    }

    /// <summary>The window property the observer publishes what it has seen on.</summary>
    internal const string StepsWindowKey = "__wwSignupSteps";

    /// <summary>
    /// The observer, as a script because that is what <c>AddInitScriptAsync</c> takes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Playwright's .NET binding has no counterpart to the JS overload that serializes a function
    /// and passes it an argument, so the selector is concatenated in rather than travelling as a
    /// parameter. It is the same constant the readers above use.
    /// </para>
    /// <para>
    /// This string is never parsed by the C# compiler, so a syntax error in it would build cleanly
    /// and go unnoticed until a browser ran it. What the tests here can pin - and do - is that it
    /// watches the attribute the readers read and publishes on the property the recorder reads back.
    /// </para>
    /// </remarks>
    internal const string StepObserverScript = @"(() => {
  const selector = '" + WildwoodTestSelectors.SignupStepSelector + @"';
  const seen = [];
  window['" + StepsWindowKey + @"'] = seen;
  const record = () => {
    const box = document.querySelector(selector);
    const step = box?.getAttribute('data-ww-step');
    // Collapse repeats: a step is re-rendered many times over, and the only interesting question
    // is which steps were entered and in what order.
    if (step && seen[seen.length - 1] !== step) seen.push(step);
  };
  const start = () => {
    record();
    // The whole document, because the component's root mounts long after this runs and a stack
    // may swap the step container out rather than only editing its attribute.
    new MutationObserver(record).observe(document.documentElement, {
      subtree: true,
      childList: true,
      attributes: true,
      attributeFilter: ['data-ww-step']
    });
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();";

    /// <summary>The expression the recorder reads the collected steps back with.</summary>
    internal const string StepsReadExpression = "() => window['" + StepsWindowKey + "'] ?? []";

    /// <summary>
    /// Fails when <paramref name="step"/> is among the steps that were entered, naming the whole
    /// route so the reader can see what the flow did instead.
    /// </summary>
    internal static void EnsureNeverEntered(IReadOnlyList<string> entered, SignupStep step)
    {
        if (entered is null)
        {
            throw new ArgumentNullException(nameof(entered));
        }

        var name = StepNames.ForSignup(step);

        foreach (var seen in entered)
        {
            if (!string.Equals(seen, name, StringComparison.Ordinal)) continue;

            throw new WildwoodContractException(
                "the signup entered the \"" + name + "\" step but should not have - steps seen: "
                + string.Join(" -> ", entered));
        }
    }

    private sealed class PageSignupStepRecorder : ISignupStepRecorder
    {
        private readonly IPage _page;

        internal PageSignupStepRecorder(IPage page)
        {
            _page = page;
        }

        public async Task<IReadOnlyList<string>> StepsAsync()
        {
            var steps = await _page.EvaluateAsync<string[]>(StepsReadExpression).ConfigureAwait(false);
            return steps ?? Array.Empty<string>();
        }

        public async Task ExpectNeverEnteredAsync(SignupStep step)
        {
            EnsureNeverEntered(await StepsAsync().ConfigureAwait(false), step);
        }
    }

    #endregion

    /// <summary>What a failure message calls the step it actually found.</summary>
    private static string Seen(string? step)
    {
        return step ?? NotRendered;
    }
}

/// <summary>Records the steps a signup actually passed through, in order.</summary>
public interface ISignupStepRecorder
{
    /// <summary>Every step entered so far, repeats collapsed.</summary>
    Task<IReadOnlyList<string>> StepsAsync();

    /// <summary>
    /// Fails unless a step was never entered - <see cref="SignupStep.Plan"/> and
    /// <see cref="SignupStep.Payment"/> on a token grant, say.
    /// </summary>
    Task ExpectNeverEnteredAsync(SignupStep step);
}
