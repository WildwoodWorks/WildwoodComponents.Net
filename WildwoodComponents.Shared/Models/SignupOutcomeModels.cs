namespace WildwoodComponents.Shared.Models;

// The cross-stack signup outcome, ported from
// packages/wildwood-react-shared/src/registrationSubscription/signupMachine.ts.

/// <summary>
/// What became of one pack. <see cref="SignupPackStatuses.Granted"/> is a pack the registration
/// token set up — there is a subscription row but no payment transaction behind it, so nothing
/// was charged.
/// </summary>
public static class SignupPackStatuses
{
    public const string Trialing = "trialing";
    public const string Active = "active";
    public const string Failed = "failed";
    public const string Granted = "granted";
}

public class SignupPackOutcome
{
    public string AddOnId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>One of <see cref="SignupPackStatuses"/>.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime? TrialEnd { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// The plan the new account ended up on, or null when the signup chose no plan.
/// </summary>
public class SignupOutcomeTier
{
    public string TierId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? PricingId { get; set; }
}

/// <summary>
/// What a registration token sets up, as the server's detailed validation reported it.
/// </summary>
public class SignupTokenGrant
{
    public string TierId { get; set; } = string.Empty;
    public string? PricingId { get; set; }
    public List<string> AddOnIds { get; set; } = new();
    public List<string> FeatureCodes { get; set; } = new();
}

/// <summary>
/// What a finished signup produced, for the host app's completion callback. Granted packs are
/// listed first.
/// </summary>
public class SignupOutcome
{
    public string UserId { get; set; } = string.Empty;
    public SignupOutcomeTier? Tier { get; set; }
    public List<SignupPackOutcome> Packs { get; set; } = new();
    public SignupTokenGrant? TokenGrant { get; set; }

    /// <summary>
    /// The account exists but its plan does not: an account-first signup whose payment step was
    /// abandoned. Set only when it happened, so a success screen can say "plan activation is
    /// pending" instead of "your plan is active".
    /// </summary>
    public bool? PlanActivationPending { get; set; }
}
