namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// The plan a signup's grid opens on when nothing has chosen one.
/// </summary>
/// <remarks>
/// <para>
/// Ported from the JS <c>SignupPlanDefault</c> union
/// (packages/wildwood-react/src/components/registrationSubscription/types.ts), whose two members
/// are the strings <c>none</c> and <c>free</c>.
/// </para>
/// <para>
/// It is a HIGHLIGHT and nothing more: the signup state machine never sees it, no plan is
/// selected, and the visitor still confirms with a click. It lives here rather than beside the
/// machine's own options for exactly that reason.
/// </para>
/// </remarks>
public enum SignupPlanDefault
{
    /// <summary>Open the grid on nothing. The default.</summary>
    None = 0,

    /// <summary>
    /// Open the grid highlighted on the app's free plan — a suggestion the visitor still confirms,
    /// not a choice already made. Ignored once a link or a token's grant has chosen.
    /// </summary>
    Free = 1
}
