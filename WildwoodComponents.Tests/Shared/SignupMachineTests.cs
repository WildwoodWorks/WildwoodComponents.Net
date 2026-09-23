using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The signup reducer: the pay-first order, what a registration token's grant skips, and the step
/// tokens that make a doubled effect or a duplicated callback a no-op. Ported case for case from
/// packages/wildwood-react/src/__tests__/signupMachine.test.ts.
/// </summary>
public class SignupMachineTests
{
    private static readonly SignupRegistrationMode.Result OpenMode = SignupRegistrationMode.Resolve(
        new SignupRegistrationSettings { AllowOpenRegistration = true, AllowTokenRegistration = true });

    private static readonly SignupRegistrationMode.Result OpenNoTokenMode = SignupRegistrationMode.Resolve(
        new SignupRegistrationSettings { AllowOpenRegistration = true, AllowTokenRegistration = false });

    private static readonly SignupRegistrationMode.Result ClosedMode = SignupRegistrationMode.Resolve(
        new SignupRegistrationSettings { AllowOpenRegistration = false, AllowTokenRegistration = false });

    private static SignupState Run(SignupState state, params SignupEvent[] events)
    {
        foreach (var signupEvent in events)
        {
            state = SignupMachine.Transition(state, signupEvent);
        }

        return state;
    }

    /// <summary>Load the mode and the catalog and land on the register form.</summary>
    private static SignupState Loaded(
        SignupMachineOptions? options = null,
        SignupRegistrationMode.Result? mode = null)
    {
        return Run(
            SignupMachine.InitialState(options),
            new SignupEvent.Init(signedIn: false),
            new SignupEvent.ModeLoaded(mode ?? OpenMode),
            new SignupEvent.CatalogLoaded(new SignupCatalogNames(
                new Dictionary<string, string> { { "tier-pro", "Pro" } },
                new Dictionary<string, string> { { "pack-a", "Pack A" }, { "pack-b", "Pack B" } })));
    }

    [Fact(DisplayName = "walks a free plan: register, token, plan, create, disclaimers, done")]
    public void WalksAFreePlan()
    {
        var state = Loaded();
        Assert.Equal(SignupStep.Register, state.Step);

        state = SignupMachine.Transition(state, new SignupEvent.RegisterSubmitted("a@example.com"));
        Assert.Equal(SignupStep.Token, state.Step);

        state = SignupMachine.Transition(state, new SignupEvent.TokenSkipped());
        Assert.Equal(SignupStep.Plan, state.Step);

        // A free plan needs no card, so the payment step is skipped.
        state = SignupMachine.Transition(state, new SignupEvent.PlanChosen("tier-pro", requiresPayment: false));
        Assert.Equal(SignupStep.Packs, state.Step);

        state = SignupMachine.Transition(state, new SignupEvent.PacksChosen(Array.Empty<string>()));
        Assert.Equal(SignupStep.Creating, state.Step);
        Assert.False(state.Token is null || state.Token.Length == 0);

        state = SignupMachine.Transition(state, new SignupEvent.AccountCreated(state.Token!, "user-1"));
        Assert.Equal(SignupStep.Disclaimers, state.Step);

        state = SignupMachine.Transition(state, new SignupEvent.DisclaimersAccepted());
        Assert.Equal(SignupStep.Done, state.Step);

        Assert.NotNull(state.Outcome);
        Assert.Equal("user-1", state.Outcome!.UserId);
        Assert.Equal("tier-pro", state.Outcome.Tier?.TierId);
        Assert.Equal("Pro", state.Outcome.Tier?.Name);
        Assert.Null(state.Outcome.Tier?.PricingId);
        Assert.Empty(state.Outcome.Packs);
        Assert.Null(state.Outcome.TokenGrant);
        Assert.Null(state.Outcome.PlanActivationPending);
    }

    [Fact(DisplayName = "a paid plan takes the card before the account exists")]
    public void APaidPlanTakesTheCardBeforeTheAccountExists()
    {
        var state = Run(
            Loaded(),
            new SignupEvent.RegisterSubmitted("a@example.com"),
            new SignupEvent.TokenSkipped(),
            new SignupEvent.PlanChosen("tier-pro", "atp-1", requiresPayment: true),
            new SignupEvent.PacksChosen(Array.Empty<string>()));
        Assert.Equal(SignupStep.Payment, state.Step);

        state = SignupMachine.Transition(state, new SignupEvent.PaymentCompleted("txn-1"));
        Assert.Equal(SignupStep.Creating, state.Step);
        Assert.Equal("txn-1", state.PaymentTransactionId);
    }

    [Fact(DisplayName = "a token grant skips the plan and the payment and drops the packs it already covers")]
    public void ATokenGrantSkipsThePlanAndThePaymentAndDropsCoveredPacks()
    {
        var state = Loaded();
        state = SignupMachine.Transition(state, new SignupEvent.RegisterSubmitted("a@example.com"));
        state = SignupMachine.Transition(state, new SignupEvent.TokenCheckStarted());

        state = SignupMachine.Transition(state, new SignupEvent.TokenAccepted(
            state.Token!,
            "INVITE-1",
            new SignupTokenGrant
            {
                TierId = "tier-pro",
                PricingId = "atp-1",
                AddOnIds = new List<string> { "pack-a" },
                FeatureCodes = new List<string> { "DOCUMENTS" }
            }));

        // Straight past plan AND payment: the token issuer is paying.
        Assert.Equal(SignupStep.Packs, state.Step);

        state = SignupMachine.Transition(state, new SignupEvent.PacksChosen(new[] { "pack-a", "pack-b" }));
        Assert.Equal(new[] { "pack-b" }, state.PacksToBuy);
        Assert.Equal(SignupStep.Creating, state.Step);

        state = SignupMachine.Transition(
            state,
            new SignupEvent.AccountCreated(state.Token!, "user-1", requiresDisclaimers: false));

        // The extra pack still has to be bought once the account is signed in.
        Assert.Equal(SignupStep.PackCheckout, state.Step);

        state = SignupMachine.Transition(state, new SignupEvent.PackCheckoutFinished(
            state.Token!,
            new[]
            {
                new SignupPackOutcome { AddOnId = "pack-b", Name = "Pack B", Status = SignupPackStatuses.Active }
            }));

        Assert.Equal(SignupStep.Done, state.Step);
        Assert.Equal(2, state.Outcome!.Packs.Count);
        Assert.Equal("pack-a", state.Outcome.Packs[0].AddOnId);
        Assert.Equal("Pack A", state.Outcome.Packs[0].Name);
        Assert.Equal(SignupPackStatuses.Granted, state.Outcome.Packs[0].Status);
        Assert.Equal("pack-b", state.Outcome.Packs[1].AddOnId);
        Assert.Equal("Pack B", state.Outcome.Packs[1].Name);
        Assert.Equal(SignupPackStatuses.Active, state.Outcome.Packs[1].Status);
        Assert.Equal("tier-pro", state.Outcome.Tier?.TierId);
        Assert.Equal("Pro", state.Outcome.Tier?.Name);
        Assert.Equal("atp-1", state.Outcome.Tier?.PricingId);
        Assert.Equal(new[] { "DOCUMENTS" }, state.Outcome.TokenGrant?.FeatureCodes);
    }

    [Fact(DisplayName = "tokenMode 'required' skips the packs as well")]
    public void TokenModeRequiredSkipsThePacksAsWell()
    {
        var inviteMode = SignupRegistrationMode.Resolve(null, SignupTokenMode.Required);
        var state = Loaded(new SignupMachineOptions { TokenMode = SignupTokenMode.Required }, inviteMode);
        state = SignupMachine.Transition(state, new SignupEvent.RegisterSubmitted("a@example.com"));
        Assert.Equal(SignupStep.Token, state.Step);

        // A required token cannot be skipped.
        Assert.Same(state, SignupMachine.Transition(state, new SignupEvent.TokenSkipped()));

        state = SignupMachine.Transition(state, new SignupEvent.TokenCheckStarted());
        state = SignupMachine.Transition(state, new SignupEvent.TokenAccepted(
            state.Token!,
            "INVITE-1",
            new SignupTokenGrant { TierId = "tier-pro" }));
        Assert.Equal(SignupStep.Creating, state.Step);
    }

    [Fact(DisplayName = "tokenMode 'required' skips the plan even when the token carries no plan")]
    public void TokenModeRequiredSkipsThePlanEvenWithNoPlanOnTheToken()
    {
        var inviteMode = SignupRegistrationMode.Resolve(null, SignupTokenMode.Required);
        var state = Loaded(new SignupMachineOptions { TokenMode = SignupTokenMode.Required }, inviteMode);
        state = Run(
            state,
            new SignupEvent.RegisterSubmitted("a@example.com"),
            new SignupEvent.TokenCheckStarted());
        state = SignupMachine.Transition(state, new SignupEvent.TokenAccepted(state.Token!, "INVITE-2"));

        // Redeeming an invite is "take what the invite gives", not a shopping trip.
        Assert.Equal(SignupStep.Creating, state.Step);
    }

    [Fact(DisplayName = "a plan the link already chose takes the plan step out of the flow")]
    public void APlanTheLinkAlreadyChoseTakesThePlanStepOutOfTheFlow()
    {
        // The catalog vetted the link's plan before the form opened.
        var state = Run(
            SignupMachine.InitialState(new SignupMachineOptions { PackSelection = SignupPackSelection.None }),
            new SignupEvent.Init(signedIn: false),
            new SignupEvent.ModeLoaded(OpenNoTokenMode),
            new SignupEvent.SelectionResolved("tier-pro", "atp-1", requiresPayment: true),
            new SignupEvent.CatalogLoaded(new SignupCatalogNames(
                new Dictionary<string, string> { { "tier-pro", "Pro" } })));
        Assert.Equal(SignupStep.Register, state.Step);
        Assert.True(state.PlanPreset);

        state = SignupMachine.Transition(state, new SignupEvent.RegisterSubmitted("a@example.com"));

        // Straight to the card: the plan was chosen on the pricing page, and it is a paid one.
        Assert.Equal(SignupStep.Payment, state.Step);
        Assert.Equal("tier-pro", state.Selection.TierId);
        Assert.Equal("atp-1", state.Selection.PricingId);
        Assert.Empty(state.Selection.AddOnIds);

        // ... and "change plan" is still a way back to the grid.
        state = SignupMachine.Transition(state, new SignupEvent.GoTo(SignupStep.Plan));
        Assert.Equal(SignupStep.Plan, state.Step);
    }

    [Fact(DisplayName = "sends a plan changed before the form is filled in back to the form, not onward")]
    public void SendsAPlanChangedBeforeTheFormBackToTheForm()
    {
        // A signup link preselected a plan, and the visitor pressed "change plan" before typing.
        var state = Run(
            SignupMachine.InitialState(new SignupMachineOptions { PackSelection = SignupPackSelection.None }),
            new SignupEvent.Init(signedIn: false),
            new SignupEvent.ModeLoaded(OpenNoTokenMode),
            new SignupEvent.SelectionResolved("tier-pro", "atp-1", requiresPayment: true),
            new SignupEvent.CatalogLoaded(new SignupCatalogNames(
                new Dictionary<string, string> { { "tier-pro", "Pro" }, { "tier-free", "Starter" } })),
            new SignupEvent.GoTo(SignupStep.Plan));
        Assert.Equal(SignupStep.Plan, state.Step);

        // A free plan: without the gate this would have gone straight to creating with no details.
        state = SignupMachine.Transition(state, new SignupEvent.PlanChosen("tier-free", requiresPayment: false));
        Assert.Equal(SignupStep.Register, state.Step);
        Assert.Equal("tier-free", state.Selection.TierId);
        Assert.False(state.FormSubmitted);

        // The form now finishes the signup, and the plan step is not asked again.
        state = SignupMachine.Transition(state, new SignupEvent.RegisterSubmitted("a@example.com"));
        Assert.Equal(SignupStep.Creating, state.Step);
        Assert.Equal("tier-free", state.Selection.TierId);
    }

    [Fact(DisplayName = "sends a paid plan changed before the form is filled in back to the form, not to a card")]
    public void SendsAPaidPlanChangedBeforeTheFormBackToTheForm()
    {
        var state = Run(
            SignupMachine.InitialState(new SignupMachineOptions { PackSelection = SignupPackSelection.None }),
            new SignupEvent.Init(signedIn: false),
            new SignupEvent.ModeLoaded(OpenNoTokenMode),
            new SignupEvent.SelectionResolved("tier-free", requiresPayment: false),
            new SignupEvent.CatalogLoaded(new SignupCatalogNames(
                new Dictionary<string, string> { { "tier-pro", "Pro" } })),
            new SignupEvent.GoTo(SignupStep.Plan),
            new SignupEvent.PlanChosen("tier-pro", "atp-1", requiresPayment: true));

        // No card may be asked for before the form: a payment taken there has no account to attach to.
        Assert.Equal(SignupStep.Register, state.Step);
        Assert.True(state.PlanRequiresPayment);

        state = SignupMachine.Transition(state, new SignupEvent.RegisterSubmitted("a@example.com"));
        Assert.Equal(SignupStep.Payment, state.Step);
    }

    [Fact(DisplayName = "never reaches payment or creating from a pack chosen before the form")]
    public void NeverReachesPaymentOrCreatingFromAPackChosenBeforeTheForm()
    {
        var state = Run(
            SignupMachine.InitialState(),
            new SignupEvent.Init(signedIn: false),
            new SignupEvent.ModeLoaded(OpenNoTokenMode),
            new SignupEvent.SelectionResolved("tier-pro", requiresPayment: true),
            new SignupEvent.CatalogLoaded(),
            new SignupEvent.GoTo(SignupStep.Packs),
            new SignupEvent.PacksChosen(new[] { "pack-a" }));

        Assert.Equal(SignupStep.Register, state.Step);
        Assert.Equal(new[] { "pack-a" }, state.PacksToBuy);
    }

    [Fact(DisplayName = "carries the packs the link asked for into the checkout without a pack step")]
    public void CarriesTheLinksPacksIntoTheCheckoutWithoutAPackStep()
    {
        var state = Run(
            SignupMachine.InitialState(new SignupMachineOptions { PackSelection = SignupPackSelection.None }),
            new SignupEvent.Init(signedIn: false),
            new SignupEvent.ModeLoaded(OpenNoTokenMode),
            new SignupEvent.SelectionResolved("tier-pro", addOnIds: new[] { "pack-a" }, requiresPayment: false),
            new SignupEvent.CatalogLoaded(new SignupCatalogNames(
                addOns: new Dictionary<string, string> { { "pack-a", "Pack A" } })));

        state = SignupMachine.Transition(state, new SignupEvent.RegisterSubmitted("a@example.com"));
        Assert.Equal(SignupStep.Creating, state.Step);
        Assert.Equal(new[] { "pack-a" }, state.PacksToBuy);
    }

    [Fact(DisplayName = "refuses a selection once the form has been submitted")]
    public void RefusesASelectionOnceTheFormHasBeenSubmitted()
    {
        var state = Run(Loaded(), new SignupEvent.RegisterSubmitted("a@example.com"));

        // A catalog reloading underneath must not rewrite what the visitor is buying.
        Assert.Same(state, SignupMachine.Transition(state, new SignupEvent.SelectionResolved("tier-other")));
    }

    [Fact(DisplayName = "planSelection 'skip' goes past the plan step")]
    public void PlanSelectionSkipGoesPastThePlanStep()
    {
        var state = Loaded(new SignupMachineOptions
        {
            PlanSelection = SignupPlanSelection.Skip,
            PackSelection = SignupPackSelection.None
        });
        state = SignupMachine.Transition(state, new SignupEvent.RegisterSubmitted("a@example.com"));
        state = SignupMachine.Transition(state, new SignupEvent.TokenSkipped());
        Assert.Equal(SignupStep.Creating, state.Step);
    }

    [Fact(DisplayName = "skips the token step when the app offers no token path")]
    public void SkipsTheTokenStepWhenTheAppOffersNoTokenPath()
    {
        var state = Loaded(null, OpenNoTokenMode);
        state = SignupMachine.Transition(state, new SignupEvent.RegisterSubmitted("a@example.com"));
        Assert.Equal(SignupStep.Plan, state.Step);
    }

    [Fact(DisplayName = "shows the closed panel when registration is off")]
    public void ShowsTheClosedPanelWhenRegistrationIsOff()
    {
        var state = Loaded(null, ClosedMode);
        Assert.Equal(SignupStep.Closed, state.Step);
    }

    [Fact(DisplayName = "ignores a result carrying a stale step token")]
    public void IgnoresAResultCarryingAStaleStepToken()
    {
        var state = Loaded(new SignupMachineOptions
        {
            PlanSelection = SignupPlanSelection.Skip,
            PackSelection = SignupPackSelection.None
        });
        state = Run(state, new SignupEvent.RegisterSubmitted("a@example.com"), new SignupEvent.TokenSkipped());
        Assert.Equal(SignupStep.Creating, state.Step);
        var stale = state.Token!;

        // The effect re-runs: the step is re-entered with a new token.
        state = SignupMachine.Transition(state, new SignupEvent.GoTo(SignupStep.Register));
        state = Run(state, new SignupEvent.RegisterSubmitted("a@example.com"), new SignupEvent.TokenSkipped());
        var fresh = state.Token!;
        Assert.NotEqual(stale, fresh);

        var ignored = SignupMachine.Transition(state, new SignupEvent.AccountCreated(stale, "ghost"));
        Assert.Same(state, ignored);
        Assert.Equal(string.Empty, ignored.UserId);

        state = SignupMachine.Transition(state, new SignupEvent.AccountCreated(fresh, "user-1"));
        Assert.Equal(SignupStep.Disclaimers, state.Step);
    }

    [Fact(DisplayName = "ignores a duplicated result event")]
    public void IgnoresADuplicatedResultEvent()
    {
        var state = Loaded(new SignupMachineOptions
        {
            PlanSelection = SignupPlanSelection.Skip,
            PackSelection = SignupPackSelection.None
        });
        state = Run(state, new SignupEvent.RegisterSubmitted("a@example.com"), new SignupEvent.TokenSkipped());
        var token = state.Token!;

        state = SignupMachine.Transition(state, new SignupEvent.AccountCreated(token, "user-1"));
        Assert.Equal(SignupStep.Disclaimers, state.Step);

        // The same callback firing twice must not advance anything.
        var again = SignupMachine.Transition(state, new SignupEvent.AccountCreated(token, "user-2"));
        Assert.Same(state, again);
        Assert.Equal("user-1", again.UserId);
    }

    [Fact(DisplayName = "latches \"already signed in\" at the first INIT only")]
    public void LatchesAlreadySignedInAtTheFirstInitOnly()
    {
        var state = SignupMachine.InitialState();
        Assert.False(state.AlreadySignedInLatched);

        state = SignupMachine.Transition(state, new SignupEvent.Init(signedIn: false));
        Assert.True(state.Initialized);
        Assert.False(state.AlreadySignedInLatched);

        // The visitor logs in mid-flow; that is not "you were already signed in".
        var after = SignupMachine.Transition(state, new SignupEvent.Init(signedIn: true));
        Assert.Same(state, after);
        Assert.False(after.AlreadySignedInLatched);
    }

    [Fact(DisplayName = "a failed account creation can be retried from the same step")]
    public void AFailedAccountCreationCanBeRetriedFromTheSameStep()
    {
        var state = Loaded(new SignupMachineOptions
        {
            PlanSelection = SignupPlanSelection.Skip,
            PackSelection = SignupPackSelection.None
        });
        state = Run(state, new SignupEvent.RegisterSubmitted("a@example.com"), new SignupEvent.TokenSkipped());

        state = SignupMachine.Transition(
            state,
            new SignupEvent.AccountFailed(state.Token!, "Email already in use"));
        Assert.Equal(SignupStep.Failed, state.Step);
        Assert.Equal("Email already in use", state.Error);
        Assert.Equal(SignupStep.Creating, state.RetryFrom);

        state = SignupMachine.Transition(state, new SignupEvent.Retry());
        Assert.Equal(SignupStep.Creating, state.Step);
        Assert.Null(state.Error);
    }

    [Fact(DisplayName = "a rejected token is a form error, not a failed flow")]
    public void ARejectedTokenIsAFormErrorNotAFailedFlow()
    {
        var state = Loaded();
        state = SignupMachine.Transition(state, new SignupEvent.RegisterSubmitted("a@example.com"));
        state = SignupMachine.Transition(state, new SignupEvent.TokenCheckStarted());
        state = SignupMachine.Transition(
            state,
            new SignupEvent.TokenRejected(state.Token!, "That token has expired"));

        Assert.Equal(SignupStep.Token, state.Step);
        Assert.Equal("That token has expired", state.TokenError);
        Assert.False(state.TokenChecking);
    }
}
