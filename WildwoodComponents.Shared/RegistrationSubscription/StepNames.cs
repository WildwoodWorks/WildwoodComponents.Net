namespace WildwoodComponents.Shared.RegistrationSubscription;

// The data-ww-step vocabulary, in one place.
//
// The step names are a CROSS-STACK CONTRACT rather than display copy. Every label these views
// render is host-configurable through RegistrationSubscriptionLabels - the plan step's heading has
// already moved from "Choose Your Plan" to "Choose a plan" once - so a browser suite that waited on
// a heading would read a reworded heading as a hang. It waits on these names instead, and they are
// the same strings packages/wildwood-react-shared writes into the attribute.
//
// Three surfaces publish them (Blazor's signup and manage views, Razor's signup view) and a fourth
// reads them back (the Playwright helpers in WildwoodComponents.Testing, which is why this table
// lives in Shared: that package references Shared and nothing else of ours). A second copy of the
// table is a step name that can drift in one stack only.

/// <summary>
/// The <c>data-ww-step</c> name for a machine step, for every stack that renders these views.
/// </summary>
public static class StepNames
{
    /// <summary>The <c>data-ww-step</c> name for a signup step.</summary>
    /// <remarks>
    /// The machine's <see cref="SignupStep.Done"/> is spelled <c>success</c> in the DOM, exactly as
    /// JS spells it, because that is the hook live end-to-end suites locate the finished signup by.
    /// </remarks>
    public static string ForSignup(SignupStep step)
    {
        // The ONE rename. `packCheckout` used to be spelled out here beside it, which read as if
        // there were two - there are not: LowerFirst("PackCheckout") already yields exactly that.
        if (step == SignupStep.Done) return "success";

        // Every other step's name is its enum name, lower-cased first letter - the machine's
        // members were named after the TS union members precisely so this holds.
        return LowerFirst(step.ToString());
    }

    /// <summary>The <c>data-ww-step</c> name for a plan change's step.</summary>
    /// <remarks>
    /// No renames here: all nine members are their TS union member spelled in PascalCase, so the
    /// lower-first rule alone reproduces the vocabulary (<c>collectingPayment</c> included).
    /// </remarks>
    public static string ForPlanChange(PlanChangeStep step)
    {
        return LowerFirst(step.ToString());
    }

    private static string LowerFirst(string name)
    {
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
