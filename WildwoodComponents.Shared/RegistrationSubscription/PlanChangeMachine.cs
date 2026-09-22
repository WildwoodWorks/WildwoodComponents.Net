using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Shared.RegistrationSubscription;

// Changing an existing subscriber's plan, as a pure reducer. Ported from
// packages/wildwood-react-shared/src/registrationSubscription/planChangeMachine.ts.
//
//   idle -> previewing -> confirm -> [collectingPayment] -> changing
//        -> [authenticating -> completing] -> done
//
// Every layout confirms: the preview says what the change costs today and what it gains or loses,
// and nobody is billed without seeing it. From there the change either applies straight away, or
// the processor wants the prorated charge authenticated (3-D Secure) — which is a "not yet", not a
// refusal: confirm the client secret, then complete the parked change by its PendingChangeId.
// "Processing" means the money is in and the server is still applying the change, so completion is
// retried rather than reported as a failure.
//
// CollectingPayment is the pre-3-D-Secure path: a host that supplies its own payment handler, or
// the built-in card modal, produces a PaymentTransactionId before the change is posted at all.

/// <summary>
/// The plan-change reducer. Pure apart from issuing step tokens, and it returns the SAME state
/// instance for an event it ignores.
/// </summary>
public static class PlanChangeMachine
{
    /// <summary>
    /// TS <c>MAX_PLAN_CHANGE_COMPLETE_ATTEMPTS</c>. How many times a "processing" answer is retried
    /// before the flow gives up and says so.
    /// </summary>
    public const int MaxPlanChangeCompleteAttempts = 5;

    private const string PreviewRefused = "The plan change could not be priced.";
    private const string ChangeRefused = "The plan change was refused.";
    private const string StillApplying =
        "The payment went through but the plan change is still being applied. Refresh in a moment.";

    /// <summary>TS <c>initialPlanChangeState</c>.</summary>
    public static PlanChangeState InitialState(PlanChangeMachineOptions? options = null)
    {
        var source = options ?? new PlanChangeMachineOptions();
        return new PlanChangeState
        {
            Step = PlanChangeStep.Idle,
            Token = null,
            AppId = source.AppId ?? string.Empty,
            TierId = source.TierId ?? string.Empty,
            PricingId = source.PricingId,
            Immediate = source.Immediate ?? true,
            Preview = null,
            Result = null,
            CompleteAttempts = 0,
            Error = null,
            RetryFrom = null
        };
    }

    /// <summary>TS <c>planChangeTransition</c>, using the process-wide step-token issuer.</summary>
    public static PlanChangeState Transition(PlanChangeState state, PlanChangeEvent changeEvent)
    {
        return Transition(state, changeEvent, StepToken.Default);
    }

    /// <summary>TS <c>planChangeTransition</c> with an explicit token issuer.</summary>
    public static PlanChangeState Transition(
        PlanChangeState state,
        PlanChangeEvent changeEvent,
        StepTokenIssuer issuer)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (changeEvent is null)
        {
            throw new ArgumentNullException(nameof(changeEvent));
        }

        if (issuer is null)
        {
            throw new ArgumentNullException(nameof(issuer));
        }

        switch (changeEvent)
        {
            case PlanChangeEvent.PreviewRequested previewRequested:
                return OnPreviewRequested(state, previewRequested, issuer);

            case PlanChangeEvent.PreviewReceived previewReceived:
                return OnPreviewReceived(state, previewReceived, issuer);

            case PlanChangeEvent.PreviewFailed previewFailed:
                if (state.Step != PlanChangeStep.Previewing
                    || !StepToken.IsCurrentStep(state.Token, previewFailed.Token))
                {
                    return state;
                }

                return Fail(state, previewFailed.Message, PlanChangeStep.Previewing);

            case PlanChangeEvent.Confirmed confirmed:
                return OnConfirmed(state, confirmed, issuer);

            case PlanChangeEvent.PaymentCompleted paymentCompleted:
                if (state.Step != PlanChangeStep.CollectingPayment)
                {
                    return state;
                }

                var paid = state.Copy();
                paid.PaymentTransactionId = paymentCompleted.PaymentTransactionId;
                return Enter(paid, PlanChangeStep.Changing, issuer);

            case PlanChangeEvent.PaymentFailed paymentFailed:
                if (state.Step != PlanChangeStep.CollectingPayment)
                {
                    return state;
                }

                return Fail(state, paymentFailed.Message, PlanChangeStep.CollectingPayment);

            case PlanChangeEvent.PaymentCancelled:
                if (state.Step != PlanChangeStep.CollectingPayment)
                {
                    return state;
                }

                return Enter(state, PlanChangeStep.Confirm, issuer);

            case PlanChangeEvent.ChangeResult changeResult:
                if (state.Step != PlanChangeStep.Changing
                    || !StepToken.IsCurrentStep(state.Token, changeResult.Token))
                {
                    return state;
                }

                var changed = state.Copy();
                changed.Token = null;
                return ApplyChangeResult(changed, changeResult.Result, PlanChangeStep.Changing, issuer);

            case PlanChangeEvent.ChangeFailed changeFailed:
                if (state.Step != PlanChangeStep.Changing
                    || !StepToken.IsCurrentStep(state.Token, changeFailed.Token))
                {
                    return state;
                }

                return Fail(state, changeFailed.Message, PlanChangeStep.Changing);

            case PlanChangeEvent.Authenticated authenticated:
                if (state.Step != PlanChangeStep.Authenticating
                    || !StepToken.IsCurrentStep(state.Token, authenticated.Token))
                {
                    return state;
                }

                var authed = state.Copy();
                authed.CompleteAttempts = 0;
                return Enter(authed, PlanChangeStep.Completing, issuer);

            case PlanChangeEvent.AuthFailed authFailed:
                if (state.Step != PlanChangeStep.Authenticating
                    || !StepToken.IsCurrentStep(state.Token, authFailed.Token))
                {
                    return state;
                }

                return Fail(state, authFailed.Message, PlanChangeStep.Authenticating);

            case PlanChangeEvent.CompleteResult completeResult:
                if (state.Step != PlanChangeStep.Completing
                    || !StepToken.IsCurrentStep(state.Token, completeResult.Token))
                {
                    return state;
                }

                var completed = state.Copy();
                completed.Token = null;
                return ApplyChangeResult(completed, completeResult.Result, PlanChangeStep.Completing, issuer);

            case PlanChangeEvent.CompleteFailed completeFailed:
                if (state.Step != PlanChangeStep.Completing
                    || !StepToken.IsCurrentStep(state.Token, completeFailed.Token))
                {
                    return state;
                }

                return Fail(state, completeFailed.Message, PlanChangeStep.Completing);

            case PlanChangeEvent.Retry:
                return OnRetry(state, issuer);

            case PlanChangeEvent.Reset:
                return InitialState(new PlanChangeMachineOptions
                {
                    AppId = state.AppId,
                    TierId = state.TierId,
                    PricingId = state.PricingId,
                    Immediate = state.Immediate
                });

            default:
                return state;
        }
    }

    #region Event handlers

    private static PlanChangeState OnPreviewRequested(
        PlanChangeState state,
        PlanChangeEvent.PreviewRequested requested,
        StepTokenIssuer issuer)
    {
        // Re-previewing while one is in flight supersedes it (a doubled mount effect, or the
        // customer flipping the billing frequency); the older answer is then dropped as stale.
        if (state.Step != PlanChangeStep.Idle
            && state.Step != PlanChangeStep.Failed
            && state.Step != PlanChangeStep.Done
            && state.Step != PlanChangeStep.Confirm
            && state.Step != PlanChangeStep.Previewing)
        {
            return state;
        }

        var next = state.Copy();
        next.AppId = requested.AppId;
        next.TierId = requested.TierId;
        next.PricingId = requested.PricingId;
        next.Immediate = requested.Immediate ?? state.Immediate;
        next.Preview = null;
        next.Result = null;
        next.CompleteAttempts = 0;
        next.ClientSecret = null;
        next.PendingChangeId = null;
        return Enter(next, PlanChangeStep.Previewing, issuer);
    }

    private static PlanChangeState OnPreviewReceived(
        PlanChangeState state,
        PlanChangeEvent.PreviewReceived received,
        StepTokenIssuer issuer)
    {
        if (state.Step != PlanChangeStep.Previewing || !StepToken.IsCurrentStep(state.Token, received.Token))
        {
            return state;
        }

        var preview = received.Preview;
        if (preview is null || !preview.Success)
        {
            var refused = state.Copy();
            refused.Preview = preview;
            return Fail(refused, preview?.ErrorMessage ?? PreviewRefused, PlanChangeStep.Previewing);
        }

        var priced = state.Copy();
        priced.Preview = preview;
        return Enter(priced, PlanChangeStep.Confirm, issuer);
    }

    private static PlanChangeState OnConfirmed(
        PlanChangeState state,
        PlanChangeEvent.Confirmed confirmed,
        StepTokenIssuer issuer)
    {
        if (state.Step != PlanChangeStep.Confirm)
        {
            return state;
        }

        var collect = confirmed.CollectPayment ?? NeedsPaymentFirst(state);

        var next = state;
        if (confirmed.Immediate is not null)
        {
            next = state.Copy();
            next.Immediate = confirmed.Immediate.Value;
        }

        return Enter(next, collect ? PlanChangeStep.CollectingPayment : PlanChangeStep.Changing, issuer);
    }

    private static PlanChangeState OnRetry(PlanChangeState state, StepTokenIssuer issuer)
    {
        if (state.Step != PlanChangeStep.Failed || state.RetryFrom is null)
        {
            return state;
        }

        // The completion budget belongs to one automatic run of retries, not to the customer: a
        // manual "Try Again" that inherited an exhausted count would give up on its first answer.
        var next = state.Copy();
        next.CompleteAttempts = 0;
        return Enter(next, state.RetryFrom.Value, issuer);
    }

    #endregion

    #region Flow helpers

    private static PlanChangeState Enter(PlanChangeState state, PlanChangeStep step, StepTokenIssuer issuer)
    {
        var startsWork = step == PlanChangeStep.Previewing
            || step == PlanChangeStep.Changing
            || step == PlanChangeStep.Authenticating
            || step == PlanChangeStep.Completing;

        var next = state.Copy();
        next.Step = step;
        next.Token = startsWork ? issuer.Issue() : null;
        next.Error = null;
        next.ErrorCode = null;
        next.RetryFrom = null;
        return next;
    }

    private static PlanChangeState Fail(
        PlanChangeState state,
        string message,
        PlanChangeStep retryFrom,
        string? errorCode = null)
    {
        var next = state.Copy();
        next.Step = PlanChangeStep.Failed;
        next.Token = null;
        next.Error = message;
        next.ErrorCode = errorCode;
        next.RetryFrom = retryFrom;
        return next;
    }

    /// <summary>
    /// Whether the change has to be paid for up front rather than through the 3-D Secure path.
    /// TS <c>needsPaymentFirst</c>.
    /// </summary>
    private static bool NeedsPaymentFirst(PlanChangeState state)
    {
        if (state.PaymentTransactionId is { Length: > 0 })
        {
            return false;
        }

        var preview = state.Preview;
        if (preview is null)
        {
            return false;
        }

        return preview.PaymentRequired && !preview.PaymentBypassAllowed;
    }

    /// <summary>
    /// Read the server's answer to a change or a completion and route on it.
    /// TS <c>applyChangeResult</c>.
    /// </summary>
    private static PlanChangeState ApplyChangeResult(
        PlanChangeState state,
        AppTierChangeResultModel? result,
        PlanChangeStep from,
        StepTokenIssuer issuer)
    {
        var next = state.Copy();
        next.Result = result;

        if (result is null)
        {
            return Fail(next, ChangeRefused, from);
        }

        if (result.Success)
        {
            next.Step = PlanChangeStep.Done;
            next.Token = null;
            next.Error = null;
            next.ErrorCode = null;
            next.RetryFrom = null;
            return next;
        }

        // "Not yet", not "no": the processor accepted the change and is waiting on the customer.
        if (result.RequiresAction == true
            && result.ClientSecret is { Length: > 0 }
            && result.PendingChangeId is { Length: > 0 })
        {
            next.ClientSecret = result.ClientSecret;
            next.PendingChangeId = result.PendingChangeId;
            next.CompleteAttempts = 0;
            return Enter(next, PlanChangeStep.Authenticating, issuer);
        }

        // The money is in; the server is still applying the change. Ask again shortly.
        if (result.Processing == true)
        {
            var attempts = state.CompleteAttempts + 1;
            next.CompleteAttempts = attempts;

            if (attempts >= MaxPlanChangeCompleteAttempts)
            {
                return Fail(
                    next,
                    result.ErrorMessage is { Length: > 0 } ? result.ErrorMessage : StillApplying,
                    PlanChangeStep.Completing,
                    result.ErrorCode);
            }

            next.PendingChangeId = result.PendingChangeId ?? state.PendingChangeId;
            return Enter(next, PlanChangeStep.Completing, issuer);
        }

        return Fail(
            next,
            result.ErrorMessage is { Length: > 0 } ? result.ErrorMessage : ChangeRefused,
            from,
            result.ErrorCode);
    }

    #endregion
}
