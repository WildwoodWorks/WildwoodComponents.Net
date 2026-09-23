using System.Globalization;

namespace WildwoodComponents.Shared.RegistrationSubscription;

// Step tokens: what makes the registration/subscription machines safe to drive from a UI.
//
// Every state that starts async work is issued a fresh token when it is entered, and the token is
// stored on the state. The result event carries the token it was started with, and the reducer
// ignores any result whose token is not the one the state is currently waiting on.
//
// That single rule covers three real hazards at once:
//   - A component that mounts twice starts the work twice; the second start re-enters the same
//     state with a NEW token, and the first result is dropped as stale.
//   - A payment SDK callback that fires twice cannot advance the machine twice — the second copy
//     carries a token the machine has already moved past.
//   - A retry supersedes the attempt it replaced instead of racing it.
//
// Ported from packages/wildwood-react-shared/src/registrationSubscription/stepTokens.ts, where a
// token is the string `step-${++counter}`. The same shape is kept here so a token logged by one
// stack reads the same as a token logged by another.

/// <summary>
/// Issues step tokens. Monotonic within the issuer, so a token is never reused and a late result
/// can always be told apart from the attempt that replaced it.
/// </summary>
/// <remarks>
/// The reducers take an issuer so a test can hold its own and get literal, deterministic values
/// (<c>step-1</c>, <c>step-2</c>, …) rather than sharing the process-wide counter with every other
/// test running in parallel. Tokens are unique within one issuer; do not mix issuers in one flow.
/// </remarks>
public sealed class StepTokenIssuer
{
    private long _counter;

    /// <summary>Issue a token for a new async step. Safe to call from any thread.</summary>
    public string Issue()
    {
        var next = Interlocked.Increment(ref _counter);
        return "step-" + next.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// The process-wide token issuer and the staleness rule the reducers apply.
/// </summary>
public static class StepToken
{
    /// <summary>
    /// The issuer the reducers use when none is supplied. The only mutable state in this namespace.
    /// </summary>
    public static StepTokenIssuer Default { get; } = new StepTokenIssuer();

    /// <summary>Issue a token from <see cref="Default"/>.</summary>
    public static string Issue()
    {
        return Default.Issue();
    }

    /// <summary>
    /// Whether a result event belongs to the step the state is currently waiting on.
    /// </summary>
    /// <remarks>
    /// A missing token on either side means "not waiting" / "no token", which is never a match: a
    /// result that arrives after the machine stopped waiting is stale by definition. JS tests this
    /// with <c>Boolean(stateToken)</c>, so an empty string is as good as null on both sides.
    /// </remarks>
    public static bool IsCurrentStep(string? stateToken, string? eventToken)
    {
        if (stateToken is null || stateToken.Length == 0)
        {
            return false;
        }

        return string.Equals(stateToken, eventToken, StringComparison.Ordinal);
    }
}
