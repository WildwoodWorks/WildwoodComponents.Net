using WildwoodComponents.Shared.RegistrationSubscription;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The <c>data-ww-step</c> vocabulary, now that one table serves every stack that publishes it and
/// the Playwright helpers that wait on it.
/// </summary>
/// <remarks>
/// <para>
/// The twelve signup names are already pinned twice over, from the views' side: Blazor's
/// <c>Every_step_has_the_name_the_DOM_hook_uses</c> and Razor's
/// <c>Every_step_has_the_name_the_DOM_uses</c> both drive this table through their own
/// <c>StepName</c>. So what is here is what those two do not cover - the two renames stated
/// directly, and the plan-change names, which no test pinned at all before the helpers began
/// waiting on them.
/// </para>
/// <para>
/// These are a CONTRACT, not display copy: a live end-to-end suite locates a step by its name, and
/// every heading around it is host-configurable.
/// </para>
/// </remarks>
public class StepNamesTests
{
    /// <summary>
    /// The machine's <c>Done</c> is spelled <c>success</c> in the DOM, and <c>PackCheckout</c>
    /// keeps its inner capital. Every other signup step is its enum name, lower-cased first letter.
    /// </summary>
    [Fact]
    public void The_two_signup_steps_that_are_not_simply_their_enum_name()
    {
        Assert.Equal("success", StepNames.ForSignup(SignupStep.Done));
        Assert.Equal("packCheckout", StepNames.ForSignup(SignupStep.PackCheckout));
        Assert.Equal("register", StepNames.ForSignup(SignupStep.Register));
    }

    /// <summary>
    /// All nine plan-change names, which the manage view publishes on its root.
    /// </summary>
    /// <remarks>
    /// <c>collectingPayment</c> is the one worth reading twice: it is the step a helper waits on to
    /// tell a refused card apart from a refused change, so a stack that spelled it
    /// <c>collecting-payment</c> would hang every such wait rather than fail it.
    /// </remarks>
    [Theory]
    [InlineData(PlanChangeStep.Idle, "idle")]
    [InlineData(PlanChangeStep.Previewing, "previewing")]
    [InlineData(PlanChangeStep.Confirm, "confirm")]
    [InlineData(PlanChangeStep.CollectingPayment, "collectingPayment")]
    [InlineData(PlanChangeStep.Changing, "changing")]
    [InlineData(PlanChangeStep.Authenticating, "authenticating")]
    [InlineData(PlanChangeStep.Completing, "completing")]
    [InlineData(PlanChangeStep.Done, "done")]
    [InlineData(PlanChangeStep.Failed, "failed")]
    public void Every_plan_change_step_has_the_name_the_DOM_uses(PlanChangeStep step, string expected)
    {
        Assert.Equal(expected, StepNames.ForPlanChange(step));
    }

    /// <summary>
    /// A plan change's <c>done</c> is NOT the signup's <c>success</c>.
    /// </summary>
    /// <remarks>
    /// Both machines have a <c>Done</c> member and they are published under different names, which
    /// is exactly the kind of thing a single shared table invites someone to "tidy up". React
    /// spells them this way and live suites key on both.
    /// </remarks>
    [Fact]
    public void The_two_machines_spell_their_finished_step_differently()
    {
        Assert.Equal("success", StepNames.ForSignup(SignupStep.Done));
        Assert.Equal("done", StepNames.ForPlanChange(PlanChangeStep.Done));
    }
}
