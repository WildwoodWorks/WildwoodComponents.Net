using System.Collections.ObjectModel;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Shared.RegistrationSubscription;

// The signup flow, as a pure reducer. Ported from
// packages/wildwood-react-shared/src/registrationSubscription/signupMachine.ts.
//
// The default order is pay-first, because the plan's card has to be taken BEFORE the account exists
// — otherwise a card that fails leaves a half-made account on a plan nobody paid for:
//
//   1 mode + catalog loaded
//   2 register form (mounted once the mode is known)
//   3 token check — a plan grant skips the plan and the payment, and removes granted packs;
//     invite redemption (TokenMode.Required) skips the plan and the packs entirely
//   4 plan
//   5 packs
//   6 plan payment (before the account exists)
//   7 register, log in, link, self-subscribe
//   8 disclaimers
//   9 pack checkout (after login, because packs are bought as the signed-in user)
//  10 success
//
// SignupPaymentOrder.AfterAccount swaps 6 and 7, and is what the store-billed stacks want: a
// StoreKit or Play purchase that succeeds before a registration that then fails strands a paid
// subscription with no account to attach it to, which is worse than an account with no plan. The
// payment step then sits between the account and the disclaimers, and a customer who walks away
// from it still finishes the signup — with the plan's activation pending, said in so many words.
//
// Step 2 is a gate, not just an order: nothing past the form may run until it has been submitted.
// A signup link that preselects a plan offers "change plan" beside the form, so a visitor can be at
// the plan grid with an empty form — choosing there takes them back to the form with their new
// plan, never onward to a card form or an account creation that has no details to work with.
//
// No HTTP and no UI here: whatever drives this runs the effects and dispatches results, and the
// views read Step. Async steps carry a step token so a duplicated effect or a callback that fires
// twice cannot advance the flow twice.

/// <summary>
/// The signup reducer. Pure apart from issuing step tokens, and it returns the SAME state instance
/// for an event it ignores, so a host can skip the re-render with
/// <see cref="object.ReferenceEquals(object, object)"/>.
/// </summary>
public static class SignupMachine
{
    /// <summary>The steps the flow walks in order. Everything before Creating may be skipped.</summary>
    private static readonly SignupStep[] FormOrderSteps =
    {
        SignupStep.Register,
        SignupStep.Token,
        SignupStep.Plan,
        SignupStep.Packs,
        SignupStep.Payment,
        SignupStep.Creating
    };

    private static readonly ReadOnlyCollection<SignupStep> FormOrderView =
        new ReadOnlyCollection<SignupStep>(FormOrderSteps);

    /// <summary>TS <c>FORM_ORDER</c>.</summary>
    public static IReadOnlyList<SignupStep> FormOrder
    {
        get { return FormOrderView; }
    }

    /// <summary>TS <c>initialSignupState</c>.</summary>
    public static SignupState InitialState(SignupMachineOptions? options = null)
    {
        var source = options ?? new SignupMachineOptions();
        var addOnIds = Dedupe(source.Selection?.AddOnIds);

        var state = new SignupState
        {
            Step = SignupStep.Loading,
            Token = null,
            Options = new ResolvedSignupOptions(
                source.TokenMode,
                source.PlanSelection,
                source.PackSelection,
                source.PaymentOrder),
            Mode = null,
            ModeReady = false,
            CatalogReady = false,
            Names = SignupCatalogNames.Empty,
            Selection = new SignupSelection(source.Selection?.TierId, source.Selection?.PricingId, addOnIds),
            PlanRequiresPayment = false,
            PlanPreset = false,
            FormSubmitted = false,
            Email = string.Empty,
            UserId = string.Empty,
            PaymentAfterAccount = false,
            PendingDisclaimers = false,
            PlanActivationPending = false,
            TokenChecking = false,
            TokenError = null,
            PacksToBuy = addOnIds,
            AlreadySignedInLatched = false,
            Initialized = false,
            Outcome = null,
            Error = null,
            RetryFrom = null
        };

        return state;
    }

    /// <summary>
    /// TS <c>signupPlanNeedsPayment</c>. Whether the plan still has to be paid for — the one
    /// question both payment positions ask, so the pay-first step and the account-first one can
    /// never disagree about whether there is a card to take. Public because a driver has to ask it
    /// too: its Creating work must leave the plan's activation to an account-first payment step
    /// that is about to run.
    /// </summary>
    public static bool PlanNeedsPayment(SignupState state)
    {
        if (state is null)
        {
            return false;
        }

        // A granted plan is paid for by whoever issued the token, and a free plan takes no card.
        return state.PlanRequiresPayment
            && state.TokenGrant is null
            && state.Selection.TierId is { Length: > 0 };
    }

    /// <summary>TS <c>signupTransition</c>, using the process-wide step-token issuer.</summary>
    public static SignupState Transition(SignupState state, SignupEvent signupEvent)
    {
        return Transition(state, signupEvent, StepToken.Default);
    }

    /// <summary>
    /// TS <c>signupTransition</c> with an explicit token issuer, so a test can assert on literal
    /// token values without sharing the process-wide counter.
    /// </summary>
    public static SignupState Transition(SignupState state, SignupEvent signupEvent, StepTokenIssuer issuer)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (signupEvent is null)
        {
            throw new ArgumentNullException(nameof(signupEvent));
        }

        if (issuer is null)
        {
            throw new ArgumentNullException(nameof(issuer));
        }

        switch (signupEvent)
        {
            case SignupEvent.Init init:
                return OnInit(state, init);

            case SignupEvent.ModeLoaded modeLoaded:
                return OnModeLoaded(state, modeLoaded, issuer);

            case SignupEvent.CatalogLoaded catalogLoaded:
                return OnCatalogLoaded(state, catalogLoaded, issuer);

            case SignupEvent.SelectionResolved selectionResolved:
                return OnSelectionResolved(state, selectionResolved);

            case SignupEvent.LoadFailed loadFailed:
                if (state.Step != SignupStep.Loading)
                {
                    return state;
                }

                return Fail(state, loadFailed.Message, SignupStep.Loading);

            case SignupEvent.RegisterSubmitted registerSubmitted:
                return OnRegisterSubmitted(state, registerSubmitted, issuer);

            case SignupEvent.TokenCheckStarted:
                return OnTokenCheckStarted(state, issuer);

            case SignupEvent.TokenAccepted tokenAccepted:
                return OnTokenAccepted(state, tokenAccepted, issuer);

            case SignupEvent.TokenRejected tokenRejected:
                return OnTokenRejected(state, tokenRejected);

            case SignupEvent.TokenSkipped:
                return OnTokenSkipped(state, issuer);

            case SignupEvent.PlanChosen planChosen:
                return OnPlanChosen(state, planChosen, issuer);

            case SignupEvent.PacksChosen packsChosen:
                return OnPacksChosen(state, packsChosen, issuer);

            case SignupEvent.PaymentCompleted paymentCompleted:
                return OnPaymentCompleted(state, paymentCompleted, issuer);

            case SignupEvent.PaymentAbandoned:
                return OnPaymentAbandoned(state, issuer);

            case SignupEvent.AccountCreated accountCreated:
                return OnAccountCreated(state, accountCreated, issuer);

            case SignupEvent.AccountFailed accountFailed:
                if (state.Step != SignupStep.Creating || !StepToken.IsCurrentStep(state.Token, accountFailed.Token))
                {
                    return state;
                }

                return Fail(state, accountFailed.Message, SignupStep.Creating);

            case SignupEvent.DisclaimersAccepted:
                if (state.Step != SignupStep.Disclaimers)
                {
                    return state;
                }

                return AfterDisclaimers(state, issuer);

            case SignupEvent.PackCheckoutFinished packCheckoutFinished:
                return OnPackCheckoutFinished(state, packCheckoutFinished);

            case SignupEvent.PackCheckoutFailed packCheckoutFailed:
                if (state.Step != SignupStep.PackCheckout
                    || !StepToken.IsCurrentStep(state.Token, packCheckoutFailed.Token))
                {
                    return state;
                }

                return Fail(state, packCheckoutFailed.Message, SignupStep.PackCheckout);

            case SignupEvent.GoTo goTo:
                return OnGoTo(state, goTo, issuer);

            case SignupEvent.Retry:
                return OnRetry(state, issuer);

            case SignupEvent.Reset:
                return OnReset(state);

            default:
                return state;
        }
    }

    #region Event handlers

    private static SignupState OnInit(SignupState state, SignupEvent.Init init)
    {
        // Latched once: a mid-flow login must not look like "you were already signed in".
        if (state.Initialized)
        {
            return state;
        }

        var next = state.Copy();
        next.Initialized = true;
        next.AlreadySignedInLatched = init.SignedIn;
        return next;
    }

    private static SignupState OnModeLoaded(
        SignupState state,
        SignupEvent.ModeLoaded modeLoaded,
        StepTokenIssuer issuer)
    {
        var next = state.Copy();
        next.Mode = modeLoaded.Mode;
        next.ModeReady = true;

        if (state.Step != SignupStep.Loading)
        {
            return next;
        }

        return StartForm(next, issuer);
    }

    private static SignupState OnCatalogLoaded(
        SignupState state,
        SignupEvent.CatalogLoaded catalogLoaded,
        StepTokenIssuer issuer)
    {
        var next = state.Copy();
        next.Names = catalogLoaded.Names ?? state.Names;
        next.CatalogReady = true;

        if (state.Step != SignupStep.Loading)
        {
            return next;
        }

        return StartForm(next, issuer);
    }

    private static SignupState OnSelectionResolved(SignupState state, SignupEvent.SelectionResolved resolved)
    {
        // Only while the form is still ahead: once it has been submitted the plan and pack steps
        // own the selection, and a catalog that reloads underneath must not rewrite what was chosen.
        if (state.Step != SignupStep.Loading && state.Step != SignupStep.Register)
        {
            return state;
        }

        var granted = GrantedAddOnIds(state.TokenGrant);
        var addOnIds = resolved.AddOnIds is not null ? Dedupe(resolved.AddOnIds) : state.Selection.AddOnIds;
        var plannedTier = resolved.TierId is not null;

        var next = state.Copy();
        next.PlanPreset = state.PlanPreset || plannedTier;
        next.PlanRequiresPayment = plannedTier ? resolved.RequiresPayment == true : state.PlanRequiresPayment;
        next.Selection = new SignupSelection(
            resolved.TierId ?? state.Selection.TierId,
            resolved.PricingId ?? state.Selection.PricingId,
            addOnIds);
        next.PacksToBuy = Without(addOnIds, granted);
        return next;
    }

    private static SignupState OnRegisterSubmitted(
        SignupState state,
        SignupEvent.RegisterSubmitted registerSubmitted,
        StepTokenIssuer issuer)
    {
        if (state.Step != SignupStep.Register)
        {
            return state;
        }

        var next = state.Copy();
        next.Email = registerSubmitted.Email;
        next.FormSubmitted = true;
        return Advance(next, SignupStep.Register, issuer);
    }

    private static SignupState OnTokenCheckStarted(SignupState state, StepTokenIssuer issuer)
    {
        if (state.Step != SignupStep.Token)
        {
            return state;
        }

        var next = state.Copy();
        next.Token = issuer.Issue();
        next.TokenChecking = true;
        next.TokenError = null;
        return next;
    }

    private static SignupState OnTokenAccepted(
        SignupState state,
        SignupEvent.TokenAccepted accepted,
        StepTokenIssuer issuer)
    {
        if (state.Step != SignupStep.Token || !StepToken.IsCurrentStep(state.Token, accepted.Token))
        {
            return state;
        }

        // A grant covers its own packs, so they drop out of what still has to be bought.
        var granted = GrantedAddOnIds(accepted.Grant);

        var next = state.Copy();
        next.Token = null;
        next.TokenChecking = false;
        next.TokenError = null;
        next.TokenValue = accepted.Value;
        next.TokenGrant = accepted.Grant;
        next.PacksToBuy = Without(state.PacksToBuy, granted);
        return Advance(next, SignupStep.Token, issuer);
    }

    private static SignupState OnTokenRejected(SignupState state, SignupEvent.TokenRejected rejected)
    {
        if (state.Step != SignupStep.Token || !StepToken.IsCurrentStep(state.Token, rejected.Token))
        {
            return state;
        }

        var next = state.Copy();
        next.Token = null;
        next.TokenChecking = false;
        next.TokenError = rejected.Message;
        return next;
    }

    private static SignupState OnTokenSkipped(SignupState state, StepTokenIssuer issuer)
    {
        // A required token cannot be skipped; the server would refuse the registration anyway.
        if (state.Step != SignupStep.Token || state.Mode?.RequireToken == true)
        {
            return state;
        }

        var next = state.Copy();
        next.Token = null;
        next.TokenChecking = false;
        next.TokenError = null;
        return Advance(next, SignupStep.Token, issuer);
    }

    private static SignupState OnPlanChosen(SignupState state, SignupEvent.PlanChosen chosen, StepTokenIssuer issuer)
    {
        if (state.Step != SignupStep.Plan)
        {
            return state;
        }

        var next = state.Copy();
        next.Selection = new SignupSelection(chosen.TierId, chosen.PricingId, state.Selection.AddOnIds);
        next.PlanRequiresPayment = chosen.RequiresPayment == true;

        // Chosen is chosen: coming back through the form must not ask for a plan again.
        next.PlanPreset = true;
        return Advance(next, SignupStep.Plan, issuer);
    }

    private static SignupState OnPacksChosen(SignupState state, SignupEvent.PacksChosen chosen, StepTokenIssuer issuer)
    {
        if (state.Step != SignupStep.Packs)
        {
            return state;
        }

        var picked = Dedupe(chosen.AddOnIds);
        var granted = GrantedAddOnIds(state.TokenGrant);

        var next = state.Copy();
        next.Selection = new SignupSelection(state.Selection.TierId, state.Selection.PricingId, picked);
        next.PacksToBuy = Without(picked, granted);
        return Advance(next, SignupStep.Packs, issuer);
    }

    private static SignupState OnPaymentCompleted(
        SignupState state,
        SignupEvent.PaymentCompleted completed,
        StepTokenIssuer issuer)
    {
        if (state.Step != SignupStep.Payment)
        {
            return state;
        }

        var paid = state.Copy();
        paid.PaymentTransactionId = completed.PaymentTransactionId;

        // Account-first: the account is already made, so the card is the last thing before the
        // disclaimers rather than another form step on the way to creating one. A card that goes
        // through also un-pends the plan: this may be the second visit to the step, after a first
        // one the customer walked away from, and the outcome must not still call the plan pending.
        if (state.PaymentAfterAccount)
        {
            paid.PlanActivationPending = false;
            return AfterAccountPayment(paid, issuer);
        }

        return Advance(paid, SignupStep.Payment, issuer);
    }

    private static SignupState OnPaymentAbandoned(SignupState state, StepTokenIssuer issuer)
    {
        // Pay-first has nothing to abandon INTO: no account exists yet, so backing out of the card
        // is a GoTo, not this.
        if (state.Step != SignupStep.Payment || !state.PaymentAfterAccount)
        {
            return state;
        }

        var next = state.Copy();
        next.PlanActivationPending = true;
        return AfterAccountPayment(next, issuer);
    }

    private static SignupState OnAccountCreated(
        SignupState state,
        SignupEvent.AccountCreated created,
        StepTokenIssuer issuer)
    {
        if (state.Step != SignupStep.Creating || !StepToken.IsCurrentStep(state.Token, created.Token))
        {
            return state;
        }

        // Default true: the plan's order puts disclaimers after account creation, and a host that
        // knows there are none passes false rather than rendering an empty step.
        var requiresDisclaimers = created.RequiresDisclaimers != false;

        var next = state.Copy();
        next.Token = null;
        next.UserId = created.UserId;

        // Remembered, because an account-first card step runs between here and there.
        next.PendingDisclaimers = requiresDisclaimers;

        if (state.Options.PaymentOrder == SignupPaymentOrder.AfterAccount
            && !(state.PaymentTransactionId is { Length: > 0 })
            && PlanNeedsPayment(state))
        {
            next.PaymentAfterAccount = true;
            return Enter(next, SignupStep.Payment, issuer);
        }

        if (requiresDisclaimers)
        {
            return Enter(next, SignupStep.Disclaimers, issuer);
        }

        return AfterDisclaimers(next, issuer);
    }

    private static SignupState OnPackCheckoutFinished(SignupState state, SignupEvent.PackCheckoutFinished finished)
    {
        if (state.Step != SignupStep.PackCheckout || !StepToken.IsCurrentStep(state.Token, finished.Token))
        {
            return state;
        }

        var next = state.Copy();
        next.Token = null;
        return Finish(next, finished.Packs);
    }

    private static SignupState OnGoTo(SignupState state, SignupEvent.GoTo goTo, StepTokenIssuer issuer)
    {
        if (state.Step == SignupStep.Loading || state.Step == SignupStep.Closed || state.Step == SignupStep.Done)
        {
            return state;
        }

        // TypeScript restricts the target to the five form steps in the event's own type; an enum
        // cannot, so a step the union does not contain is ignored rather than entered.
        if (!IsBackNavigable(goTo.Step))
        {
            return state;
        }

        if (goTo.Step == SignupStep.Payment && state.Options.PaymentOrder == SignupPaymentOrder.AfterAccount)
        {
            // There is no pre-account card step in that order, so the card can only be re-opened
            // where the machine itself put it: at the step, or at the disclaimers an abandoned card
            // dropped the flow into. Past the disclaimers the signup is buying packs — re-opening
            // the plan's card there would come back through disclaimers already accepted and
            // restart a checkout that may already be charging. Done is refused by the guard above:
            // the outcome is out.
            if (state.Step != SignupStep.Payment && state.Step != SignupStep.Disclaimers)
            {
                return state;
            }

            // Only while the card is genuinely still outstanding: an account to attach it to, a
            // plan that has to be paid for, and nothing taken for it yet.
            if (!(state.UserId is { Length: > 0 })
                || state.PaymentTransactionId is { Length: > 0 }
                || !PlanNeedsPayment(state))
            {
                return state;
            }

            var toCard = state.Copy();
            toCard.TokenError = null;
            toCard.PaymentAfterAccount = true;
            return Enter(toCard, SignupStep.Payment, issuer);
        }

        var next = state.Copy();
        next.TokenError = null;
        return Enter(next, goTo.Step, issuer);
    }

    private static SignupState OnRetry(SignupState state, StepTokenIssuer issuer)
    {
        if (state.Step != SignupStep.Failed || state.RetryFrom is null)
        {
            return state;
        }

        if (state.RetryFrom == SignupStep.Loading)
        {
            var back = state.Copy();
            back.Step = SignupStep.Loading;
            back.Token = null;
            back.Error = null;
            back.RetryFrom = null;
            return back;
        }

        // RetryFrom is only ever an async step (loading, creating, packCheckout), so re-entering it
        // never lands on a payment step and the account-first latch is left exactly as it was.
        return Enter(state, state.RetryFrom.Value, issuer);
    }

    private static SignupState OnReset(SignupState state)
    {
        var fresh = InitialState(new SignupMachineOptions
        {
            TokenMode = state.Options.TokenMode,
            PlanSelection = state.Options.PlanSelection,
            PackSelection = state.Options.PackSelection,
            PaymentOrder = state.Options.PaymentOrder
        });

        // The latch belongs to the visit, not the attempt: starting over must not suddenly claim
        // they were already signed in.
        fresh.Initialized = state.Initialized;
        fresh.AlreadySignedInLatched = state.AlreadySignedInLatched;
        return fresh;
    }

    #endregion

    #region Flow helpers

    /// <summary>Whether a step is passed over for this flow. TS <c>isSkipped</c>.</summary>
    private static bool IsSkipped(SignupState state, SignupStep step)
    {
        switch (step)
        {
            case SignupStep.Token:
                // No token path at all: neither required nor offered as an option.
                return !(state.Mode?.RequireToken == true || state.Mode?.ShowOptionalTokenEntry == true);

            case SignupStep.Plan:
                // A grant already names the plan, a resolved link already chose it, and invite
                // redemption is "take what the invite gives" — in none of those is there anything
                // to choose.
                return state.Options.PlanSelection == SignupPlanSelection.Skip
                    || state.Options.TokenMode == SignupTokenMode.Required
                    || state.PlanPreset
                    || state.TokenGrant is not null;

            case SignupStep.Packs:
                // Invite redemption is "take what the invite gives", not a shopping trip.
                return state.Options.PackSelection == SignupPackSelection.None
                    || state.Options.TokenMode == SignupTokenMode.Required;

            case SignupStep.Payment:
                // Account-first: the card is taken after Creating, not inside the form's order, so
                // the form never walks a payment step at all.
                if (state.Options.PaymentOrder == SignupPaymentOrder.AfterAccount)
                {
                    return true;
                }

                return !PlanNeedsPayment(state);

            default:
                return false;
        }
    }

    /// <summary>Move into a step, issuing a token when that step starts async work. TS <c>enter</c>.</summary>
    private static SignupState Enter(SignupState state, SignupStep step, StepTokenIssuer issuer)
    {
        var startsWork = step == SignupStep.Creating || step == SignupStep.PackCheckout;

        var next = state.Copy();
        next.Step = step;
        next.Token = startsWork ? issuer.Issue() : null;
        next.TokenChecking = false;
        next.Error = null;
        next.RetryFrom = null;
        return next;
    }

    /// <summary>TS <c>fail</c>.</summary>
    private static SignupState Fail(SignupState state, string message, SignupStep retryFrom)
    {
        var next = state.Copy();
        next.Step = SignupStep.Failed;
        next.Token = null;
        next.TokenChecking = false;
        next.Error = message;
        next.RetryFrom = retryFrom;
        return next;
    }

    /// <summary>The next step after <paramref name="from"/>, skipping whatever this flow does not need.</summary>
    private static SignupState Advance(SignupState state, SignupStep from, StepTokenIssuer issuer)
    {
        // The form comes first, always. A visitor who followed "change plan" out of the form and
        // chose there is sent back to it with their new choice, rather than on towards a card form
        // or an account creation that has no details to work with.
        if (!state.FormSubmitted)
        {
            return Enter(state, SignupStep.Register, issuer);
        }

        var start = IndexInFormOrder(from);
        for (var i = start + 1; i < FormOrderSteps.Length; i++)
        {
            var step = FormOrderSteps[i];
            if (!IsSkipped(state, step))
            {
                return Enter(state, step, issuer);
            }
        }

        return Enter(state, SignupStep.Creating, issuer);
    }

    /// <summary>After the account exists and the disclaimers are out of the way.</summary>
    private static SignupState AfterDisclaimers(SignupState state, StepTokenIssuer issuer)
    {
        if (state.PacksToBuy.Count > 0)
        {
            return Enter(state, SignupStep.PackCheckout, issuer);
        }

        return Finish(state, Array.Empty<SignupPackOutcome>());
    }

    /// <summary>
    /// Leaving the ACCOUNT-FIRST payment step, whether the card was given or walked away from: on
    /// to the disclaimers ACCOUNT_CREATED asked for, or straight past them — exactly where that
    /// event would have gone had there been no card to take.
    /// </summary>
    private static SignupState AfterAccountPayment(SignupState state, StepTokenIssuer issuer)
    {
        var next = state.Copy();
        next.PaymentAfterAccount = false;

        if (next.PendingDisclaimers)
        {
            return Enter(next, SignupStep.Disclaimers, issuer);
        }

        return AfterDisclaimers(next, issuer);
    }

    /// <summary>Both the mode and the catalog are in: open the form, or say sign-up is closed.</summary>
    private static SignupState StartForm(SignupState state, StepTokenIssuer issuer)
    {
        if (!state.ModeReady || !state.CatalogReady)
        {
            return state;
        }

        if (state.Mode?.Closed == true)
        {
            var closed = state.Copy();
            closed.Step = SignupStep.Closed;
            closed.Token = null;
            return closed;
        }

        return Enter(state, SignupStep.Register, issuer);
    }

    /// <summary>Everything is done: build the outcome, granted packs first. TS <c>finish</c>.</summary>
    private static SignupState Finish(SignupState state, IReadOnlyList<SignupPackOutcome> bought)
    {
        var packs = new List<SignupPackOutcome>();

        if (state.TokenGrant is not null)
        {
            foreach (var addOnId in state.TokenGrant.AddOnIds)
            {
                packs.Add(new SignupPackOutcome
                {
                    AddOnId = addOnId,
                    Name = state.Names.AddOnName(addOnId),
                    Status = SignupPackStatuses.Granted
                });
            }
        }

        if (bought is not null)
        {
            foreach (var pack in bought)
            {
                packs.Add(pack);
            }
        }

        var next = state.Copy();
        next.Step = SignupStep.Done;
        next.Token = null;
        next.Error = null;
        next.RetryFrom = null;
        next.Outcome = new SignupOutcome
        {
            UserId = state.UserId,
            Tier = ResolveTier(state),
            Packs = packs,
            TokenGrant = state.TokenGrant,

            // Left unset entirely when it did not happen, so the ordinary outcome carries no dead
            // flag (the JS outcome leaves the key off).
            PlanActivationPending = state.PlanActivationPending ? true : (bool?)null
        };
        return next;
    }

    /// <summary>TS <c>resolveTier</c>.</summary>
    private static SignupOutcomeTier? ResolveTier(SignupState state)
    {
        var tierId = state.TokenGrant is not null ? state.TokenGrant.TierId : state.Selection.TierId;
        if (!(tierId is { Length: > 0 }))
        {
            return null;
        }

        return new SignupOutcomeTier
        {
            TierId = tierId,
            Name = state.Names.TierName(tierId),
            PricingId = state.TokenGrant?.PricingId ?? state.Selection.PricingId
        };
    }

    #endregion

    #region Small helpers

    private static bool IsBackNavigable(SignupStep step)
    {
        return step == SignupStep.Register
            || step == SignupStep.Token
            || step == SignupStep.Plan
            || step == SignupStep.Packs
            || step == SignupStep.Payment;
    }

    private static int IndexInFormOrder(SignupStep step)
    {
        for (var i = 0; i < FormOrderSteps.Length; i++)
        {
            if (FormOrderSteps[i] == step)
            {
                return i;
            }
        }

        // TS Array.indexOf answers -1, and the loop then starts at the first step.
        return -1;
    }

    /// <summary>TS <c>[...new Set(ids ?? [])]</c>: unique, in the order they were given.</summary>
    private static IReadOnlyList<string> Dedupe(IReadOnlyList<string>? ids)
    {
        if (ids is null || ids.Count == 0)
        {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<string>(ids.Count);
        foreach (var id in ids)
        {
            if (seen.Add(id))
            {
                unique.Add(id);
            }
        }

        return unique;
    }

    private static HashSet<string> GrantedAddOnIds(SignupTokenGrant? grant)
    {
        var granted = new HashSet<string>(StringComparer.Ordinal);
        if (grant is not null)
        {
            foreach (var addOnId in grant.AddOnIds)
            {
                granted.Add(addOnId);
            }
        }

        return granted;
    }

    private static IReadOnlyList<string> Without(IReadOnlyList<string> ids, HashSet<string> excluded)
    {
        if (excluded.Count == 0)
        {
            return ids;
        }

        var kept = new List<string>(ids.Count);
        foreach (var id in ids)
        {
            if (!excluded.Contains(id))
            {
                kept.Add(id);
            }
        }

        return kept;
    }

    #endregion
}
