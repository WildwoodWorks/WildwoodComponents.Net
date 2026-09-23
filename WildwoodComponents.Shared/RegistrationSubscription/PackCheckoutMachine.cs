using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Shared.RegistrationSubscription;

// Buying any number of packs against one card, as a pure reducer. Ported from
// packages/wildwood-react-shared/src/registrationSubscription/packCheckoutMachine.ts.
//
//   idle -> quoting -> quoted -> [collectingCard] -> checkingOut
//        -> authenticating -> completing -> (next item) -> done
//
// The server prices the basket, the customer's card is collected once when there is none on file,
// the basket is bought in one call, and then any pack the bank wants authenticated is walked ONE
// AT A TIME, in order: confirm its client secret, complete it on the server, move to the next. One
// pack failing never stops the others — a basket is a basket, not a transaction — so read Results
// per pack rather than a single success flag.

/// <summary>
/// The pack-checkout reducer. Pure apart from issuing step tokens, and it returns the SAME state
/// instance for an event it ignores.
/// </summary>
public static class PackCheckoutMachine
{
    private const string QuoteRefused = "The packs could not be priced.";
    private const string PurchaseRefused = "The packs could not be bought.";

    /// <summary>TS <c>initialPackCheckoutState</c>.</summary>
    public static PackCheckoutState InitialState(PackCheckoutMachineOptions? options = null)
    {
        var source = options ?? new PackCheckoutMachineOptions();
        return new PackCheckoutState
        {
            Step = PackCheckoutStep.Idle,
            Token = null,
            AppId = source.AppId ?? string.Empty,
            Items = source.Items ?? Array.Empty<AddOnCheckoutItemInput>(),
            Quote = null,
            UseSavedCard = false,
            Results = Array.Empty<AddOnCheckoutItemResultModel>(),
            PendingIndexes = Array.Empty<int>(),
            PendingPosition = 0,
            Error = null,
            RetryFrom = null
        };
    }

    /// <summary>
    /// TS <c>currentPackCheckoutItem</c>. The pack currently being authenticated/completed, or null
    /// when the walk is over.
    /// </summary>
    public static AddOnCheckoutItemResultModel? CurrentPackCheckoutItem(PackCheckoutState state)
    {
        if (state is null)
        {
            return null;
        }

        var index = CurrentIndex(state);
        if (index < 0 || index >= state.Results.Count)
        {
            return null;
        }

        return state.Results[index];
    }

    /// <summary>TS <c>packCheckoutTransition</c>, using the process-wide step-token issuer.</summary>
    public static PackCheckoutState Transition(PackCheckoutState state, PackCheckoutEvent checkoutEvent)
    {
        return Transition(state, checkoutEvent, StepToken.Default);
    }

    /// <summary>TS <c>packCheckoutTransition</c> with an explicit token issuer.</summary>
    public static PackCheckoutState Transition(
        PackCheckoutState state,
        PackCheckoutEvent checkoutEvent,
        StepTokenIssuer issuer)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (checkoutEvent is null)
        {
            throw new ArgumentNullException(nameof(checkoutEvent));
        }

        if (issuer is null)
        {
            throw new ArgumentNullException(nameof(issuer));
        }

        switch (checkoutEvent)
        {
            case PackCheckoutEvent.QuoteRequested quoteRequested:
                return OnQuoteRequested(state, quoteRequested, issuer);

            case PackCheckoutEvent.QuoteReceived quoteReceived:
                return OnQuoteReceived(state, quoteReceived, issuer);

            case PackCheckoutEvent.QuoteFailed quoteFailed:
                if (state.Step != PackCheckoutStep.Quoting || !StepToken.IsCurrentStep(state.Token, quoteFailed.Token))
                {
                    return state;
                }

                return Fail(state, quoteFailed.Message, PackCheckoutStep.Quoting, quoteFailed.ErrorCode);

            case PackCheckoutEvent.CardRequested:
                if (state.Step != PackCheckoutStep.Quoted)
                {
                    return state;
                }

                var forCard = state.Copy();
                forCard.CardClientSecret = null;
                return Enter(forCard, PackCheckoutStep.CollectingCard, issuer);

            case PackCheckoutEvent.CardIntentReceived cardIntentReceived:
                return OnCardIntentReceived(state, cardIntentReceived);

            case PackCheckoutEvent.CardIntentFailed cardIntentFailed:
                if (state.Step != PackCheckoutStep.CollectingCard
                    || !StepToken.IsCurrentStep(state.Token, cardIntentFailed.Token))
                {
                    return state;
                }

                return Fail(
                    state,
                    cardIntentFailed.Message,
                    PackCheckoutStep.CollectingCard,
                    cardIntentFailed.ErrorCode);

            case PackCheckoutEvent.CardConfirmed cardConfirmed:
                return OnCardConfirmed(state, cardConfirmed, issuer);

            case PackCheckoutEvent.CardFailed cardFailed:
                if (state.Step != PackCheckoutStep.CollectingCard
                    || !StepToken.IsCurrentStep(state.Token, cardFailed.Token))
                {
                    return state;
                }

                return Fail(state, cardFailed.Message, PackCheckoutStep.CollectingCard);

            case PackCheckoutEvent.CheckoutRequested:
                if (state.Step != PackCheckoutStep.Quoted)
                {
                    return state;
                }

                return Enter(state, PackCheckoutStep.CheckingOut, issuer);

            case PackCheckoutEvent.CheckoutReceived checkoutReceived:
                return OnCheckoutReceived(state, checkoutReceived, issuer);

            case PackCheckoutEvent.CheckoutFailed checkoutFailed:
                if (state.Step != PackCheckoutStep.CheckingOut
                    || !StepToken.IsCurrentStep(state.Token, checkoutFailed.Token))
                {
                    return state;
                }

                return Fail(
                    state,
                    checkoutFailed.Message,
                    PackCheckoutStep.CheckingOut,
                    checkoutFailed.ErrorCode);

            case PackCheckoutEvent.ItemAuthenticated itemAuthenticated:
                if (state.Step != PackCheckoutStep.Authenticating
                    || !StepToken.IsCurrentStep(state.Token, itemAuthenticated.Token))
                {
                    return state;
                }

                return Enter(state, PackCheckoutStep.Completing, issuer);

            case PackCheckoutEvent.ItemAuthFailed itemAuthFailed:
                return OnItemAuthFailed(state, itemAuthFailed, issuer);

            case PackCheckoutEvent.ItemCompleted itemCompleted:
                return OnItemCompleted(state, itemCompleted, issuer);

            case PackCheckoutEvent.Retry:
                return OnRetry(state, issuer);

            case PackCheckoutEvent.Reset:
                return InitialState(new PackCheckoutMachineOptions { AppId = state.AppId, Items = state.Items });

            default:
                return state;
        }
    }

    #region Event handlers

    private static PackCheckoutState OnQuoteRequested(
        PackCheckoutState state,
        PackCheckoutEvent.QuoteRequested requested,
        StepTokenIssuer issuer)
    {
        // Re-quoting while a quote is in flight is allowed and supersedes it — that is exactly what
        // a doubled mount effect does, and the older answer is then dropped as stale.
        if (state.Step != PackCheckoutStep.Idle
            && state.Step != PackCheckoutStep.Failed
            && state.Step != PackCheckoutStep.Quoted
            && state.Step != PackCheckoutStep.Quoting)
        {
            return state;
        }

        var next = state.Copy();
        next.AppId = requested.AppId;
        next.Items = requested.Items;
        next.Quote = null;
        return Enter(next, PackCheckoutStep.Quoting, issuer);
    }

    private static PackCheckoutState OnQuoteReceived(
        PackCheckoutState state,
        PackCheckoutEvent.QuoteReceived received,
        StepTokenIssuer issuer)
    {
        if (state.Step != PackCheckoutStep.Quoting || !StepToken.IsCurrentStep(state.Token, received.Token))
        {
            return state;
        }

        var quote = received.Quote;
        if (quote is null || !quote.Success)
        {
            var refused = state.Copy();
            refused.Quote = quote;
            return Fail(
                refused,
                quote?.ErrorMessage ?? QuoteRefused,
                PackCheckoutStep.Quoting,
                quote?.ErrorCode);
        }

        var priced = state.Copy();
        priced.Quote = quote;
        var next = Enter(priced, PackCheckoutStep.Quoted, issuer);

        // A quote that needs no new card is bought against the one already on file.
        next.UseSavedCard = !quote.RequiresPaymentMethod;
        return next;
    }

    private static PackCheckoutState OnCardIntentReceived(
        PackCheckoutState state,
        PackCheckoutEvent.CardIntentReceived received)
    {
        if (state.Step != PackCheckoutStep.CollectingCard || !StepToken.IsCurrentStep(state.Token, received.Token))
        {
            return state;
        }

        var next = state.Copy();
        next.CardClientSecret = received.ClientSecret;
        next.PaymentTransactionId = received.PaymentTransactionId;
        return next;
    }

    private static PackCheckoutState OnCardConfirmed(
        PackCheckoutState state,
        PackCheckoutEvent.CardConfirmed confirmed,
        StepTokenIssuer issuer)
    {
        if (state.Step != PackCheckoutStep.CollectingCard || !StepToken.IsCurrentStep(state.Token, confirmed.Token))
        {
            return state;
        }

        var withCard = state.Copy();
        withCard.PaymentTransactionId = confirmed.PaymentTransactionId ?? state.PaymentTransactionId;
        withCard.UseSavedCard = false;
        return Enter(withCard, PackCheckoutStep.CheckingOut, issuer);
    }

    private static PackCheckoutState OnCheckoutReceived(
        PackCheckoutState state,
        PackCheckoutEvent.CheckoutReceived received,
        StepTokenIssuer issuer)
    {
        if (state.Step != PackCheckoutStep.CheckingOut || !StepToken.IsCurrentStep(state.Token, received.Token))
        {
            return state;
        }

        IReadOnlyList<AddOnCheckoutItemResultModel> results =
            received.Result?.Results ?? (IReadOnlyList<AddOnCheckoutItemResultModel>)Array.Empty<AddOnCheckoutItemResultModel>();

        if (results.Count == 0)
        {
            return Fail(
                state,
                received.Result?.ErrorMessage ?? PurchaseRefused,
                PackCheckoutStep.CheckingOut,
                received.Result?.ErrorCode);
        }

        var pendingIndexes = new List<int>();
        for (var i = 0; i < results.Count; i++)
        {
            if (string.Equals(results[i].Status, AddOnCheckoutItemStatuses.RequiresAction, StringComparison.Ordinal))
            {
                pendingIndexes.Add(i);
            }
        }

        var withResults = state.Copy();
        withResults.Results = results;
        withResults.PendingIndexes = pendingIndexes;
        withResults.PendingPosition = 0;

        if (pendingIndexes.Count == 0)
        {
            return Done(withResults);
        }

        return Enter(withResults, PackCheckoutStep.Authenticating, issuer);
    }

    private static PackCheckoutState OnItemAuthFailed(
        PackCheckoutState state,
        PackCheckoutEvent.ItemAuthFailed failed,
        StepTokenIssuer issuer)
    {
        if (state.Step != PackCheckoutStep.Authenticating || !StepToken.IsCurrentStep(state.Token, failed.Token))
        {
            return state;
        }

        // This pack is lost; the rest of the basket is not.
        var results = ReplaceCurrent(state, current =>
        {
            var patched = CloneItem(current);
            patched.Status = AddOnCheckoutItemStatuses.Failed;
            patched.ErrorMessage = failed.Message;
            return patched;
        });

        var next = state.Copy();
        next.Results = results;
        return NextPending(next, issuer);
    }

    private static PackCheckoutState OnItemCompleted(
        PackCheckoutState state,
        PackCheckoutEvent.ItemCompleted completed,
        StepTokenIssuer issuer)
    {
        if (state.Step != PackCheckoutStep.Completing || !StepToken.IsCurrentStep(state.Token, completed.Token))
        {
            return state;
        }

        var results = ReplaceCurrent(state, current => MergeItem(current, completed.Result));

        var next = state.Copy();
        next.Results = results;
        return NextPending(next, issuer);
    }

    private static PackCheckoutState OnRetry(PackCheckoutState state, StepTokenIssuer issuer)
    {
        if (state.Step != PackCheckoutStep.Failed || state.RetryFrom is null)
        {
            return state;
        }

        // Back to the card form means collecting a NEW SetupIntent: the secret this state is
        // holding belongs to the attempt that just failed, and confirming it again confirms the
        // wrong intent.
        if (state.RetryFrom == PackCheckoutStep.CollectingCard)
        {
            var fresh = state.Copy();
            fresh.CardClientSecret = null;
            fresh.PaymentTransactionId = null;
            return Enter(fresh, PackCheckoutStep.CollectingCard, issuer);
        }

        return Enter(state, state.RetryFrom.Value, issuer);
    }

    #endregion

    #region Flow helpers

    private static PackCheckoutState Enter(PackCheckoutState state, PackCheckoutStep step, StepTokenIssuer issuer)
    {
        var startsWork = step == PackCheckoutStep.Quoting
            || step == PackCheckoutStep.CollectingCard
            || step == PackCheckoutStep.CheckingOut
            || step == PackCheckoutStep.Authenticating
            || step == PackCheckoutStep.Completing;

        var next = state.Copy();
        next.Step = step;
        next.Token = startsWork ? issuer.Issue() : null;
        next.Error = null;
        next.ErrorCode = null;
        next.RetryFrom = null;
        return next;
    }

    private static PackCheckoutState Fail(
        PackCheckoutState state,
        string message,
        PackCheckoutStep retryFrom,
        string? errorCode = null)
    {
        var next = state.Copy();
        next.Step = PackCheckoutStep.Failed;
        next.Token = null;
        next.Error = message;
        next.ErrorCode = errorCode;
        next.RetryFrom = retryFrom;
        return next;
    }

    private static PackCheckoutState Done(PackCheckoutState state)
    {
        var next = state.Copy();
        next.Step = PackCheckoutStep.Done;
        next.Token = null;
        next.Error = null;
        next.ErrorCode = null;
        next.RetryFrom = null;
        return next;
    }

    /// <summary>Move to the next pack needing 3-D Secure, or finish. TS <c>nextPending</c>.</summary>
    private static PackCheckoutState NextPending(PackCheckoutState state, StepTokenIssuer issuer)
    {
        var advanced = state.Copy();
        advanced.PendingPosition = state.PendingPosition + 1;

        if (advanced.PendingPosition >= advanced.PendingIndexes.Count)
        {
            return Done(advanced);
        }

        return Enter(advanced, PackCheckoutStep.Authenticating, issuer);
    }

    private static int CurrentIndex(PackCheckoutState state)
    {
        if (state.PendingPosition < 0 || state.PendingPosition >= state.PendingIndexes.Count)
        {
            return -1;
        }

        return state.PendingIndexes[state.PendingPosition];
    }

    /// <summary>Replace the current pack's result, keeping its place in Results.</summary>
    private static IReadOnlyList<AddOnCheckoutItemResultModel> ReplaceCurrent(
        PackCheckoutState state,
        Func<AddOnCheckoutItemResultModel, AddOnCheckoutItemResultModel> patch)
    {
        var index = CurrentIndex(state);
        if (index < 0 || index >= state.Results.Count)
        {
            return state.Results;
        }

        var replaced = new List<AddOnCheckoutItemResultModel>(state.Results.Count);
        for (var i = 0; i < state.Results.Count; i++)
        {
            replaced.Add(i == index ? patch(state.Results[i]) : state.Results[i]);
        }

        return replaced;
    }

    private static AddOnCheckoutItemResultModel CloneItem(AddOnCheckoutItemResultModel source)
    {
        return new AddOnCheckoutItemResultModel
        {
            AddOnId = source.AddOnId,
            PricingId = source.PricingId,
            Status = source.Status,
            SubscriptionId = source.SubscriptionId,
            TrialEnd = source.TrialEnd,
            AmountDueToday = source.AmountDueToday,
            ClientSecret = source.ClientSecret,
            PaymentIntentId = source.PaymentIntentId,
            PaymentTransactionId = source.PaymentTransactionId,
            ErrorCode = source.ErrorCode,
            ErrorMessage = source.ErrorMessage
        };
    }

    /// <summary>
    /// The C# rendering of the TypeScript spread <c>{ ...current, ...result }</c>: JavaScript only
    /// overwrites the keys the server actually sent, so a value the completion answer left out
    /// keeps whatever the purchase put there. C# has every property present, so "left out" is read
    /// as null (or an empty string for the three the model declares non-nullable).
    /// </summary>
    private static AddOnCheckoutItemResultModel MergeItem(
        AddOnCheckoutItemResultModel current,
        AddOnCheckoutItemResultModel? patch)
    {
        if (patch is null)
        {
            return current;
        }

        return new AddOnCheckoutItemResultModel
        {
            AddOnId = patch.AddOnId is { Length: > 0 } ? patch.AddOnId : current.AddOnId,
            PricingId = patch.PricingId is { Length: > 0 } ? patch.PricingId : current.PricingId,
            Status = patch.Status is { Length: > 0 } ? patch.Status : current.Status,
            SubscriptionId = patch.SubscriptionId ?? current.SubscriptionId,
            TrialEnd = patch.TrialEnd ?? current.TrialEnd,
            AmountDueToday = patch.AmountDueToday ?? current.AmountDueToday,
            ClientSecret = patch.ClientSecret ?? current.ClientSecret,
            PaymentIntentId = patch.PaymentIntentId ?? current.PaymentIntentId,
            PaymentTransactionId = patch.PaymentTransactionId ?? current.PaymentTransactionId,
            ErrorCode = patch.ErrorCode ?? current.ErrorCode,
            ErrorMessage = patch.ErrorMessage ?? current.ErrorMessage
        };
    }

    #endregion
}
