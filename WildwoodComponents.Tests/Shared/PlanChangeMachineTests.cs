using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The plan-change reducer: every layout confirms the preview, 3-D Secure is a "not yet" rather
/// than a refusal, and "processing" is retried instead of reported as a failure. Ported case for
/// case from packages/wildwood-react/src/__tests__/planChangeMachine.test.ts.
/// </summary>
public class PlanChangeMachineTests
{
    private static TierChangePreviewModel Preview(Action<TierChangePreviewModel>? overrides = null)
    {
        var preview = new TierChangePreviewModel
        {
            Success = true,
            IsUpgrade = true,
            IsDowngrade = false,
            IsBillingFrequencyChange = false,
            PaymentRequired = false,
            PaymentBypassAllowed = false,
            PaymentProviderAvailable = true,
            FeaturesGained = new List<string>(),
            FeaturesLost = new List<string>(),
            Currency = "USD",
            DaysRemainingInPeriod = 12,
            AllowImmediateChange = true,
            AllowScheduledChange = true
        };
        overrides?.Invoke(preview);
        return preview;
    }

    private static AppTierChangeResultModel ChangeResult(Action<AppTierChangeResultModel>? overrides = null)
    {
        var result = new AppTierChangeResultModel
        {
            Success = false,
            ErrorMessage = string.Empty,
            IsScheduled = false
        };
        overrides?.Invoke(result);
        return result;
    }

    private static PlanChangeState Previewed(TierChangePreviewModel previewModel)
    {
        var state = PlanChangeMachine.Transition(
            PlanChangeMachine.InitialState(),
            new PlanChangeEvent.PreviewRequested("app-1", "tier-pro", "atp-1"));

        return PlanChangeMachine.Transition(
            state,
            new PlanChangeEvent.PreviewReceived(state.Token!, previewModel));
    }

    [Fact(DisplayName = "previews, confirms and changes")]
    public void PreviewsConfirmsAndChanges()
    {
        var state = Previewed(Preview());
        Assert.Equal(PlanChangeStep.Confirm, state.Step);
        Assert.True(state.Preview?.IsUpgrade);

        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.Confirmed());
        Assert.Equal(PlanChangeStep.Changing, state.Step);

        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.ChangeResult(
            state.Token!,
            ChangeResult(r => r.Success = true)));
        Assert.Equal(PlanChangeStep.Done, state.Step);
        Assert.Null(state.Error);
    }

    [Fact(DisplayName = "parks on 3-D Secure, then completes")]
    public void ParksOnThreeDSecureThenCompletes()
    {
        var state = Previewed(Preview(p =>
        {
            p.PaymentRequired = true;
            p.PaymentBypassAllowed = true;
            p.ProratedChargeToday = 12.5m;
        }));
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.Confirmed());
        Assert.Equal(PlanChangeStep.Changing, state.Step);

        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.ChangeResult(
            state.Token!,
            ChangeResult(r =>
            {
                r.RequiresAction = true;
                r.ClientSecret = "pi_secret";
                r.PendingChangeId = "pending-1";
                r.AmountDue = 12.5m;
                r.Currency = "USD";
            })));
        Assert.Equal(PlanChangeStep.Authenticating, state.Step);
        Assert.Equal("pi_secret", state.ClientSecret);
        Assert.Equal("pending-1", state.PendingChangeId);

        // "Not yet" is not a refusal: nothing is shown as an error.
        Assert.Null(state.Error);

        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.Authenticated(state.Token!));
        Assert.Equal(PlanChangeStep.Completing, state.Step);

        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.CompleteResult(
            state.Token!,
            ChangeResult(r => r.Success = true)));
        Assert.Equal(PlanChangeStep.Done, state.Step);
    }

    [Fact(DisplayName = "retries completion while the server answers processing")]
    public void RetriesCompletionWhileTheServerAnswersProcessing()
    {
        var state = Previewed(Preview());
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.Confirmed());
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.ChangeResult(
            state.Token!,
            ChangeResult(r =>
            {
                r.RequiresAction = true;
                r.ClientSecret = "s";
                r.PendingChangeId = "pending-1";
            })));
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.Authenticated(state.Token!));

        var firstToken = state.Token!;
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.CompleteResult(
            firstToken,
            ChangeResult(r => r.Processing = true)));
        Assert.Equal(PlanChangeStep.Completing, state.Step);
        Assert.Equal(1, state.CompleteAttempts);

        // A fresh token, so the driver's effect runs the completion again.
        Assert.NotEqual(firstToken, state.Token);

        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.CompleteResult(
            state.Token!,
            ChangeResult(r => r.Success = true)));
        Assert.Equal(PlanChangeStep.Done, state.Step);
    }

    [Fact(DisplayName = "gives up after too many processing answers")]
    public void GivesUpAfterTooManyProcessingAnswers()
    {
        var state = Previewed(Preview());
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.Confirmed());
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.ChangeResult(
            state.Token!,
            ChangeResult(r =>
            {
                r.RequiresAction = true;
                r.ClientSecret = "s";
                r.PendingChangeId = "pending-1";
            })));
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.Authenticated(state.Token!));

        for (var i = 0; i < PlanChangeMachine.MaxPlanChangeCompleteAttempts; i++)
        {
            state = PlanChangeMachine.Transition(state, new PlanChangeEvent.CompleteResult(
                state.Token!,
                ChangeResult(r => r.Processing = true)));
        }

        Assert.Equal(PlanChangeStep.Failed, state.Step);
        Assert.Equal(PlanChangeMachine.MaxPlanChangeCompleteAttempts, state.CompleteAttempts);
        Assert.Equal(PlanChangeStep.Completing, state.RetryFrom);
    }

    [Fact(DisplayName = "starts the completion budget over on a manual retry")]
    public void StartsTheCompletionBudgetOverOnAManualRetry()
    {
        var state = Previewed(Preview());
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.Confirmed());
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.ChangeResult(
            state.Token!,
            ChangeResult(r =>
            {
                r.RequiresAction = true;
                r.ClientSecret = "s";
                r.PendingChangeId = "pending-1";
            })));
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.Authenticated(state.Token!));

        for (var i = 0; i < PlanChangeMachine.MaxPlanChangeCompleteAttempts; i++)
        {
            state = PlanChangeMachine.Transition(state, new PlanChangeEvent.CompleteResult(
                state.Token!,
                ChangeResult(r => r.Processing = true)));
        }

        Assert.Equal(PlanChangeStep.Failed, state.Step);

        // The budget belongs to one automatic run of retries: the customer's own retry gets a fresh
        // one, or it would give up on its first answer.
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.Retry());
        Assert.Equal(PlanChangeStep.Completing, state.Step);
        Assert.Equal(0, state.CompleteAttempts);
    }

    [Fact(DisplayName = "carries the timing the customer chose into the change")]
    public void CarriesTheTimingTheCustomerChose()
    {
        var state = Previewed(Preview(p =>
        {
            p.IsUpgrade = false;
            p.IsDowngrade = true;
        }));
        Assert.True(state.Immediate);

        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.Confirmed(immediate: false));
        Assert.Equal(PlanChangeStep.Changing, state.Step);
        Assert.False(state.Immediate);
    }

    [Fact(DisplayName = "reports the server errorCode on a refusal")]
    public void ReportsTheServerErrorCodeOnARefusal()
    {
        var state = Previewed(Preview());
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.Confirmed());
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.ChangeResult(
            state.Token!,
            ChangeResult(r =>
            {
                r.ErrorMessage = "That change is already under way";
                r.ErrorCode = TierChangeErrorCodes.TierChangeAlreadyInProgress;
            })));

        Assert.Equal(PlanChangeStep.Failed, state.Step);
        Assert.Equal("That change is already under way", state.Error);
        Assert.Equal(TierChangeErrorCodes.TierChangeAlreadyInProgress, state.ErrorCode);
        Assert.Equal(PlanChangeStep.Changing, state.RetryFrom);
    }

    [Fact(DisplayName = "collects a payment first on the legacy path")]
    public void CollectsAPaymentFirstOnTheLegacyPath()
    {
        // PaymentRequired with no bypass: the host's payment handler (or the built-in modal) runs
        // before the change is posted at all.
        var state = Previewed(Preview(p =>
        {
            p.PaymentRequired = true;
            p.PaymentBypassAllowed = false;
            p.NewPrice = 39m;
        }));
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.Confirmed());
        Assert.Equal(PlanChangeStep.CollectingPayment, state.Step);

        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.PaymentCompleted("txn-9"));
        Assert.Equal(PlanChangeStep.Changing, state.Step);
        Assert.Equal("txn-9", state.PaymentTransactionId);

        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.ChangeResult(
            state.Token!,
            ChangeResult(r => r.Success = true)));
        Assert.Equal(PlanChangeStep.Done, state.Step);
    }

    [Fact(DisplayName = "a cancelled payment goes back to the confirmation, not to a failure")]
    public void ACancelledPaymentGoesBackToTheConfirmation()
    {
        var state = Previewed(Preview(p => p.PaymentRequired = true));
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.Confirmed());
        state = PlanChangeMachine.Transition(state, new PlanChangeEvent.PaymentCancelled());
        Assert.Equal(PlanChangeStep.Confirm, state.Step);
        Assert.Null(state.Error);
    }

    [Fact(DisplayName = "ignores a result carrying a stale step token")]
    public void IgnoresAResultCarryingAStaleStepToken()
    {
        var state = PlanChangeMachine.Transition(
            PlanChangeMachine.InitialState(),
            new PlanChangeEvent.PreviewRequested("app-1", "tier-pro"));
        var stale = state.Token!;
        state = PlanChangeMachine.Transition(
            state,
            new PlanChangeEvent.PreviewRequested("app-1", "tier-pro"));

        var ignored = PlanChangeMachine.Transition(
            state,
            new PlanChangeEvent.PreviewReceived(stale, Preview()));
        Assert.Same(state, ignored);
        Assert.Equal(PlanChangeStep.Previewing, ignored.Step);
    }
}
