using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The account-first signup order. Ported case for case from
/// packages/wildwood-react/src/__tests__/signupMachine.paymentOrder.test.ts.
/// </summary>
/// <remarks>
/// <see cref="SignupPaymentOrder.AfterAccount"/> is what the store-billed stacks want: a StoreKit
/// or Play purchase that succeeds before a registration that then fails strands a paid subscription
/// with no account to attach it to, which is worse than an account with no plan. So the card moves
/// out of the form's order and sits between the account and the disclaimers — and a customer who
/// walks away from it still finishes the signup, with the plan's activation pending, said in so
/// many words.
/// <para>
/// The default order is asserted by <see cref="SignupMachineTests"/>, unchanged. What is defended
/// here is that the option moves the step and NOTHING else: the same skip rules decide whether
/// there is a card to take at all, and the same step tokens still throw stale results away.
/// </para>
/// </remarks>
public class SignupMachinePaymentOrderTests
{
    private static readonly SignupRegistrationMode.Result OpenMode = SignupRegistrationMode.Resolve(
        new SignupRegistrationSettings { AllowOpenRegistration = true, AllowTokenRegistration = true });

    private static SignupState Run(SignupState state, params SignupEvent[] events)
    {
        foreach (var signupEvent in events)
        {
            state = SignupMachine.Transition(state, signupEvent);
        }

        return state;
    }

    /// <summary>Load the mode and the catalog and land on the register form.</summary>
    private static SignupState Loaded(SignupMachineOptions? options = null)
    {
        var resolved = options ?? new SignupMachineOptions();
        resolved.PaymentOrder = SignupPaymentOrder.AfterAccount;

        return Run(
            SignupMachine.InitialState(resolved),
            new SignupEvent.Init(signedIn: false),
            new SignupEvent.ModeLoaded(OpenMode),
            new SignupEvent.CatalogLoaded(new SignupCatalogNames(
                new Dictionary<string, string> { { "tier-pro", "Pro" } },
                new Dictionary<string, string> { { "pack-a", "Pack A" } })));
    }

    /// <summary>Walk the form with a paid plan chosen, which in this order ends at Creating.</summary>
    private static SignupState ToCreating(SignupMachineOptions? options = null)
    {
        return Run(
            Loaded(options),
            new SignupEvent.RegisterSubmitted("a@example.com"),
            new SignupEvent.TokenSkipped(),
            new SignupEvent.PlanChosen("tier-pro", "atp-1", requiresPayment: true),
            new SignupEvent.PacksChosen(Array.Empty<string>()));
    }

    #region the account-first order

    [Fact(DisplayName = "takes the payment step out of the form order")]
    public void TakesThePaymentStepOutOfTheFormOrder()
    {
        var state = ToCreating();

        // Pay-first would be sitting on payment here; this order has already gone past it.
        Assert.Equal(SignupStep.Creating, state.Step);
        Assert.False(state.Token is null || state.Token.Length == 0);
        Assert.False(state.PaymentAfterAccount);
    }

    [Fact(DisplayName = "asks for the card once the account exists, before the disclaimers")]
    public void AsksForTheCardOnceTheAccountExists()
    {
        var state = ToCreating();
        state = SignupMachine.Transition(state, new SignupEvent.AccountCreated(state.Token!, "user-1"));

        Assert.Equal(SignupStep.Payment, state.Step);
        Assert.True(state.PaymentAfterAccount);
        Assert.Equal("user-1", state.UserId);

        // Where it goes afterwards was decided by the event, and is remembered across the card.
        Assert.True(state.PendingDisclaimers);
    }

    [Fact(DisplayName = "carries on to the disclaimers when the card is given")]
    public void CarriesOnToTheDisclaimersWhenTheCardIsGiven()
    {
        var state = ToCreating();
        state = SignupMachine.Transition(state, new SignupEvent.AccountCreated(state.Token!, "user-1"));
        state = SignupMachine.Transition(state, new SignupEvent.PaymentCompleted("txn-1"));

        Assert.Equal(SignupStep.Disclaimers, state.Step);
        Assert.Equal("txn-1", state.PaymentTransactionId);
        Assert.False(state.PaymentAfterAccount);

        state = SignupMachine.Transition(state, new SignupEvent.DisclaimersAccepted());
        Assert.Equal(SignupStep.Done, state.Step);
        Assert.NotNull(state.Outcome);
        Assert.Equal("user-1", state.Outcome!.UserId);
        Assert.Equal("tier-pro", state.Outcome.Tier?.TierId);
        Assert.Equal("Pro", state.Outcome.Tier?.Name);
        Assert.Equal("atp-1", state.Outcome.Tier?.PricingId);
        Assert.Empty(state.Outcome.Packs);
        Assert.Null(state.Outcome.TokenGrant);
        Assert.Null(state.Outcome.PlanActivationPending);
    }

    [Fact(DisplayName = "goes exactly where ACCOUNT_CREATED would have when there are no disclaimers")]
    public void GoesWhereAccountCreatedWouldHaveWithNoDisclaimers()
    {
        var state = Run(
            Loaded(),
            new SignupEvent.RegisterSubmitted("a@example.com"),
            new SignupEvent.TokenSkipped(),
            new SignupEvent.PlanChosen("tier-pro", "atp-1", requiresPayment: true),
            new SignupEvent.PacksChosen(new[] { "pack-a" }));
        state = SignupMachine.Transition(
            state,
            new SignupEvent.AccountCreated(state.Token!, "user-1", requiresDisclaimers: false));
        Assert.Equal(SignupStep.Payment, state.Step);

        state = SignupMachine.Transition(state, new SignupEvent.PaymentCompleted("txn-1"));

        // Straight to the packs the visitor also asked for, as it would have been without a card.
        Assert.Equal(SignupStep.PackCheckout, state.Step);
    }

    [Fact(DisplayName = "finishes with the plan pending when the customer walks away from the card")]
    public void FinishesWithThePlanPendingWhenTheCardIsWalkedAwayFrom()
    {
        var state = ToCreating();
        state = SignupMachine.Transition(
            state,
            new SignupEvent.AccountCreated(state.Token!, "user-1", requiresDisclaimers: false));
        Assert.Equal(SignupStep.Payment, state.Step);

        state = SignupMachine.Transition(state, new SignupEvent.PaymentAbandoned());

        // The account is real, so it is not thrown away: the signup completes and says what is missing.
        Assert.Equal(SignupStep.Done, state.Step);
        Assert.True(state.PlanActivationPending);
        Assert.True(state.Outcome?.PlanActivationPending);
        Assert.Equal("user-1", state.Outcome?.UserId);
        Assert.Null(state.PaymentTransactionId);
    }

    [Fact(DisplayName = "still shows the disclaimers after an abandoned card")]
    public void StillShowsTheDisclaimersAfterAnAbandonedCard()
    {
        var state = ToCreating();
        state = SignupMachine.Transition(state, new SignupEvent.AccountCreated(state.Token!, "user-1"));
        state = SignupMachine.Transition(state, new SignupEvent.PaymentAbandoned());

        Assert.Equal(SignupStep.Disclaimers, state.Step);
        Assert.True(state.PlanActivationPending);
    }

    [Fact(DisplayName = "carries the pending plan all the way into the outcome when the card is never retried")]
    public void CarriesThePendingPlanIntoTheOutcome()
    {
        var state = ToCreating();
        state = SignupMachine.Transition(state, new SignupEvent.AccountCreated(state.Token!, "user-1"));
        state = SignupMachine.Transition(state, new SignupEvent.PaymentAbandoned());
        Assert.Equal(SignupStep.Disclaimers, state.Step);

        state = SignupMachine.Transition(state, new SignupEvent.DisclaimersAccepted());
        Assert.Equal(SignupStep.Done, state.Step);
        Assert.True(state.Outcome?.PlanActivationPending);
        Assert.Null(state.PaymentTransactionId);
    }

    [Fact(DisplayName = "un-pends the plan when the customer comes back and the card goes through")]
    public void UnPendsThePlanWhenTheCardEventuallyGoesThrough()
    {
        var state = ToCreating();
        state = SignupMachine.Transition(state, new SignupEvent.AccountCreated(state.Token!, "user-1"));
        state = SignupMachine.Transition(state, new SignupEvent.PaymentAbandoned());
        Assert.True(state.PlanActivationPending);

        // Back to the card, and this time it is given.
        state = SignupMachine.Transition(state, new SignupEvent.GoTo(SignupStep.Payment));
        Assert.Equal(SignupStep.Payment, state.Step);
        state = SignupMachine.Transition(state, new SignupEvent.PaymentCompleted("txn-2"));

        // The disclaimers were never accepted, so they are still what comes next.
        Assert.Equal(SignupStep.Disclaimers, state.Step);
        Assert.False(state.PlanActivationPending);

        state = SignupMachine.Transition(state, new SignupEvent.DisclaimersAccepted());
        Assert.Equal(SignupStep.Done, state.Step);

        // A paying customer is never told their plan is still pending.
        Assert.Equal("user-1", state.Outcome?.UserId);
        Assert.Equal("tier-pro", state.Outcome?.Tier?.TierId);
        Assert.Equal("Pro", state.Outcome?.Tier?.Name);
        Assert.Equal("atp-1", state.Outcome?.Tier?.PricingId);
        Assert.Empty(state.Outcome!.Packs);
        Assert.Null(state.Outcome.TokenGrant);
        Assert.Null(state.Outcome.PlanActivationPending);
        Assert.Equal("txn-2", state.PaymentTransactionId);
    }

    [Fact(DisplayName = "ignores PAYMENT_ABANDONED in the pay-first order")]
    public void IgnoresPaymentAbandonedInThePayFirstOrder()
    {
        // Pay-first reaches its payment step with no account behind it, so there is nothing to
        // abandon INTO — backing out there is a step back, which is GO_TO.
        var state = Run(
            SignupMachine.InitialState(),
            new SignupEvent.Init(signedIn: false),
            new SignupEvent.ModeLoaded(OpenMode),
            new SignupEvent.CatalogLoaded(),
            new SignupEvent.RegisterSubmitted("a@example.com"),
            new SignupEvent.TokenSkipped(),
            new SignupEvent.PlanChosen("tier-pro", "atp-1", requiresPayment: true),
            new SignupEvent.PacksChosen(Array.Empty<string>()));
        Assert.Equal(SignupStep.Payment, state.Step);

        var before = state;
        state = SignupMachine.Transition(state, new SignupEvent.PaymentAbandoned());
        Assert.Same(before, state);
    }

    [Fact(DisplayName = "never asks a token grant to pay: the issuer already did")]
    public void NeverAsksATokenGrantToPay()
    {
        var state = Run(
            Loaded(),
            new SignupEvent.RegisterSubmitted("a@example.com"),
            new SignupEvent.TokenCheckStarted());
        state = SignupMachine.Transition(state, new SignupEvent.TokenAccepted(
            state.Token!,
            "INVITE-1",
            new SignupTokenGrant { TierId = "tier-pro", PricingId = "atp-1" }));
        Assert.Equal(SignupStep.Packs, state.Step);

        state = SignupMachine.Transition(state, new SignupEvent.PacksChosen(Array.Empty<string>()));
        Assert.Equal(SignupStep.Creating, state.Step);

        state = SignupMachine.Transition(
            state,
            new SignupEvent.AccountCreated(state.Token!, "user-1", requiresDisclaimers: false));
        Assert.Equal(SignupStep.Done, state.Step);
        Assert.False(state.PaymentAfterAccount);
    }

    [Fact(DisplayName = "never asks a free plan to pay")]
    public void NeverAsksAFreePlanToPay()
    {
        var state = Run(
            Loaded(),
            new SignupEvent.RegisterSubmitted("a@example.com"),
            new SignupEvent.TokenSkipped(),
            new SignupEvent.PlanChosen("tier-pro", requiresPayment: false),
            new SignupEvent.PacksChosen(Array.Empty<string>()));
        state = SignupMachine.Transition(
            state,
            new SignupEvent.AccountCreated(state.Token!, "user-1", requiresDisclaimers: false));
        Assert.Equal(SignupStep.Done, state.Step);
    }

    [Fact(DisplayName = "does not ask twice when the card was somehow already given")]
    public void DoesNotAskTwiceWhenTheCardWasAlreadyGiven()
    {
        var state = ToCreating();
        state = SignupMachine.Transition(state, new SignupEvent.AccountCreated(state.Token!, "user-1"));
        state = SignupMachine.Transition(state, new SignupEvent.PaymentCompleted("txn-1"));
        Assert.Equal(SignupStep.Disclaimers, state.Step);

        // A start-over that walks the form again carries the transaction already taken, so the card
        // step has nothing left to ask for.
        state = Run(
            state,
            new SignupEvent.GoTo(SignupStep.Register),
            new SignupEvent.RegisterSubmitted("a@example.com"),
            new SignupEvent.TokenSkipped(),
            new SignupEvent.PacksChosen(Array.Empty<string>()));
        Assert.Equal(SignupStep.Creating, state.Step);

        state = SignupMachine.Transition(
            state,
            new SignupEvent.AccountCreated(state.Token!, "user-1", requiresDisclaimers: false));
        Assert.Equal(SignupStep.Done, state.Step);
    }

    [Fact(DisplayName = "still throws a stale account result away")]
    public void StillThrowsAStaleAccountResultAway()
    {
        var state = ToCreating();
        var stale = StepToken.Issue();
        Assert.Same(state, SignupMachine.Transition(state, new SignupEvent.AccountCreated(stale, "user-1")));
        Assert.Same(state, SignupMachine.Transition(state, new SignupEvent.AccountFailed(stale, "no")));
    }

    #endregion

    #region GO_TO and RESET in the account-first order

    [Fact(DisplayName = "refuses to open a card step before there is an account")]
    public void RefusesToOpenACardStepBeforeThereIsAnAccount()
    {
        var state = Run(
            Loaded(),
            new SignupEvent.RegisterSubmitted("a@example.com"),
            new SignupEvent.TokenSkipped());
        Assert.Equal(SignupStep.Plan, state.Step);

        // There is no pre-account card step in this order, so the machine stays where it is.
        Assert.Same(state, SignupMachine.Transition(state, new SignupEvent.GoTo(SignupStep.Payment)));
    }

    [Fact(DisplayName = "re-opens the card step once the account exists")]
    public void ReOpensTheCardStepOnceTheAccountExists()
    {
        var state = ToCreating();
        state = SignupMachine.Transition(state, new SignupEvent.AccountCreated(state.Token!, "user-1"));
        state = SignupMachine.Transition(state, new SignupEvent.PaymentAbandoned());
        Assert.Equal(SignupStep.Disclaimers, state.Step);

        state = SignupMachine.Transition(state, new SignupEvent.GoTo(SignupStep.Payment));
        Assert.Equal(SignupStep.Payment, state.Step);
        Assert.True(state.PaymentAfterAccount);
    }

    [Fact(DisplayName = "refuses to re-open the card once one has been taken")]
    public void RefusesToReOpenTheCardOnceOneHasBeenTaken()
    {
        var state = ToCreating();
        state = SignupMachine.Transition(state, new SignupEvent.AccountCreated(state.Token!, "user-1"));
        state = SignupMachine.Transition(state, new SignupEvent.PaymentCompleted("txn-1"));
        Assert.Equal(SignupStep.Disclaimers, state.Step);

        // The plan is paid for; there is nothing left to ask the customer for.
        Assert.Same(state, SignupMachine.Transition(state, new SignupEvent.GoTo(SignupStep.Payment)));
    }

    [Fact(DisplayName = "refuses to re-open the card once the signup has moved on to the packs")]
    public void RefusesToReOpenTheCardOnceTheSignupIsBuyingPacks()
    {
        var state = Run(
            Loaded(),
            new SignupEvent.RegisterSubmitted("a@example.com"),
            new SignupEvent.TokenSkipped(),
            new SignupEvent.PlanChosen("tier-pro", "atp-1", requiresPayment: true),
            new SignupEvent.PacksChosen(new[] { "pack-a" }));
        state = SignupMachine.Transition(
            state,
            new SignupEvent.AccountCreated(state.Token!, "user-1", requiresDisclaimers: false));
        state = SignupMachine.Transition(state, new SignupEvent.PaymentAbandoned());
        Assert.Equal(SignupStep.PackCheckout, state.Step);

        // Coming back through the card from here would re-run disclaimers already passed and
        // restart a checkout that may already be charging, so the machine stays where it is.
        Assert.Same(state, SignupMachine.Transition(state, new SignupEvent.GoTo(SignupStep.Payment)));
    }

    [Fact(DisplayName = "refuses to re-open the card once the outcome is out")]
    public void RefusesToReOpenTheCardOnceTheOutcomeIsOut()
    {
        var state = ToCreating();
        state = SignupMachine.Transition(
            state,
            new SignupEvent.AccountCreated(state.Token!, "user-1", requiresDisclaimers: false));
        state = SignupMachine.Transition(state, new SignupEvent.PaymentAbandoned());
        Assert.Equal(SignupStep.Done, state.Step);

        // Done refuses every GO_TO: the outcome has been handed to the host already.
        Assert.Same(state, SignupMachine.Transition(state, new SignupEvent.GoTo(SignupStep.Payment)));
        Assert.Same(state, SignupMachine.Transition(state, new SignupEvent.GoTo(SignupStep.Register)));
    }

    [Fact(DisplayName = "keeps the order across a start-over")]
    public void KeepsTheOrderAcrossAStartOver()
    {
        var state = ToCreating();
        state = SignupMachine.Transition(state, new SignupEvent.Reset());
        Assert.Equal(SignupPaymentOrder.AfterAccount, state.Options.PaymentOrder);
        Assert.False(state.PaymentAfterAccount);
        Assert.False(state.PlanActivationPending);
    }

    [Fact(DisplayName = "defaults to the pay-first order when nothing asks otherwise")]
    public void DefaultsToThePayFirstOrder()
    {
        Assert.Equal(SignupPaymentOrder.BeforeAccount, SignupMachine.InitialState().Options.PaymentOrder);
    }

    #endregion
}
