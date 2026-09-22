using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Shared.RegistrationSubscription;

// The plan-change flow's state, ported from
// packages/wildwood-react-shared/src/registrationSubscription/planChangeMachine.ts.

/// <summary>Where a plan change is.</summary>
public enum PlanChangeStep
{
    Idle = 0,
    Previewing = 1,
    Confirm = 2,
    CollectingPayment = 3,
    Changing = 4,
    Authenticating = 5,
    Completing = 6,
    Done = 7,
    Failed = 8
}

/// <summary>What a plan change starts with.</summary>
public sealed class PlanChangeMachineOptions
{
    public string? AppId { get; set; }

    public string? TierId { get; set; }

    public string? PricingId { get; set; }

    /// <summary>
    /// Apply the change now rather than at the end of the billing period. Defaults to true, so
    /// null here means true.
    /// </summary>
    public bool? Immediate { get; set; }
}

/// <summary>
/// Changing an existing subscriber's plan. Immutable to callers: only
/// <see cref="PlanChangeMachine"/> writes it, and only onto a fresh <see cref="Copy"/>.
/// </summary>
public sealed class PlanChangeState
{
    internal PlanChangeState()
    {
    }

    private PlanChangeState(PlanChangeState source)
    {
        Step = source.Step;
        Token = source.Token;
        AppId = source.AppId;
        TierId = source.TierId;
        PricingId = source.PricingId;
        Immediate = source.Immediate;
        Preview = source.Preview;
        PaymentTransactionId = source.PaymentTransactionId;
        ClientSecret = source.ClientSecret;
        PendingChangeId = source.PendingChangeId;
        Result = source.Result;
        CompleteAttempts = source.CompleteAttempts;
        Error = source.Error;
        ErrorCode = source.ErrorCode;
        RetryFrom = source.RetryFrom;
    }

    public PlanChangeStep Step { get; internal set; } = PlanChangeStep.Idle;

    /// <summary>The async step in flight, or null. Results carrying another token are ignored.</summary>
    public string? Token { get; internal set; }

    public string AppId { get; internal set; } = string.Empty;

    public string TierId { get; internal set; } = string.Empty;

    public string? PricingId { get; internal set; }

    public bool Immediate { get; internal set; } = true;

    public TierChangePreviewModel? Preview { get; internal set; }

    /// <summary>A payment made before the change was posted (the legacy/modal path).</summary>
    public string? PaymentTransactionId { get; internal set; }

    /// <summary>3-D Secure, set when the server parks the change. Secret — never log it.</summary>
    public string? ClientSecret { get; internal set; }

    public string? PendingChangeId { get; internal set; }

    public AppTierChangeResultModel? Result { get; internal set; }

    /// <summary>How many completion attempts the server has answered "processing".</summary>
    public int CompleteAttempts { get; internal set; }

    public string? Error { get; internal set; }

    /// <summary>The server's machine-readable refusal reason, when it sent one.</summary>
    public string? ErrorCode { get; internal set; }

    /// <summary>Which step a RETRY goes back to.</summary>
    public PlanChangeStep? RetryFrom { get; internal set; }

    /// <summary>A field-for-field copy the reducer writes its changes onto.</summary>
    internal PlanChangeState Copy()
    {
        return new PlanChangeState(this);
    }
}
