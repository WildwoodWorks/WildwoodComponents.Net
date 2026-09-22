using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The pack-checkout reducer: quote, one card, one purchase, then 3-D Secure walked one pack at a
/// time. One pack failing must leave the others alone. Ported case for case from
/// packages/wildwood-react/src/__tests__/packCheckoutMachine.test.ts.
/// </summary>
public class PackCheckoutMachineTests
{
    private static AddOnCheckoutQuoteModel Quote(bool requiresPaymentMethod)
    {
        return new AddOnCheckoutQuoteModel
        {
            Success = true,
            CheckoutId = "chk-1",
            ProviderId = "prov-1",
            Currency = "USD",
            Lines = new List<AddOnCheckoutQuoteLineModel>(),
            TotalDueToday = 20,
            RequiresPaymentMethod = requiresPaymentMethod
        };
    }

    private static AddOnCheckoutItemResultModel Item(
        string addOnId,
        string status,
        Action<AddOnCheckoutItemResultModel>? extra = null)
    {
        var item = new AddOnCheckoutItemResultModel
        {
            AddOnId = addOnId,
            PricingId = addOnId + "-pricing",
            Status = status
        };
        extra?.Invoke(item);
        return item;
    }

    private static string[] Statuses(IReadOnlyList<AddOnCheckoutItemResultModel> results)
    {
        var statuses = new string[results.Count];
        for (var i = 0; i < results.Count; i++)
        {
            statuses[i] = results[i].Status;
        }

        return statuses;
    }

    private static PackCheckoutState Quoted(bool requiresPaymentMethod)
    {
        var state = PackCheckoutMachine.Transition(
            PackCheckoutMachine.InitialState(),
            new PackCheckoutEvent.QuoteRequested(
                "app-1",
                new[]
                {
                    new AddOnCheckoutItemInput { AddOnId = "pack-a" },
                    new AddOnCheckoutItemInput { AddOnId = "pack-b" }
                }));

        return PackCheckoutMachine.Transition(
            state,
            new PackCheckoutEvent.QuoteReceived(state.Token!, Quote(requiresPaymentMethod)));
    }

    [Fact(DisplayName = "buys against the card on file when the quote needs no new one")]
    public void BuysAgainstTheCardOnFile()
    {
        var state = Quoted(false);
        Assert.Equal(PackCheckoutStep.Quoted, state.Step);
        Assert.True(state.UseSavedCard);

        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.CheckoutRequested());
        Assert.Equal(PackCheckoutStep.CheckingOut, state.Step);

        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.CheckoutReceived(
            state.Token!,
            new AddOnCheckoutResultModel
            {
                Success = true,
                CheckoutId = "chk-1",
                Results = new List<AddOnCheckoutItemResultModel>
                {
                    Item("pack-a", AddOnCheckoutItemStatuses.Active),
                    Item("pack-b", AddOnCheckoutItemStatuses.Trialing, i => i.TrialEnd = new DateTime(2026, 10, 1))
                }
            }));

        Assert.Equal(PackCheckoutStep.Done, state.Step);
        Assert.Equal(
            new[] { AddOnCheckoutItemStatuses.Active, AddOnCheckoutItemStatuses.Trialing },
            Statuses(state.Results));
    }

    [Fact(DisplayName = "collects a card first when the quote says there is none")]
    public void CollectsACardFirstWhenTheQuoteSaysThereIsNone()
    {
        var state = Quoted(true);
        Assert.False(state.UseSavedCard);

        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.CardRequested());
        Assert.Equal(PackCheckoutStep.CollectingCard, state.Step);

        state = PackCheckoutMachine.Transition(
            state,
            new PackCheckoutEvent.CardIntentReceived(state.Token!, "seti_secret", "txn-card"));
        Assert.Equal("seti_secret", state.CardClientSecret);
        Assert.Equal(PackCheckoutStep.CollectingCard, state.Step);

        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.CardConfirmed(state.Token!));
        Assert.Equal(PackCheckoutStep.CheckingOut, state.Step);
        Assert.Equal("txn-card", state.PaymentTransactionId);
        Assert.False(state.UseSavedCard);
    }

    [Fact(DisplayName = "walks 3-D Secure one pack at a time, in order")]
    public void WalksThreeDSecureOnePackAtATime()
    {
        var state = Quoted(false);
        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.CheckoutRequested());
        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.CheckoutReceived(
            state.Token!,
            new AddOnCheckoutResultModel
            {
                Success = true,
                CheckoutId = "chk-1",
                Results = new List<AddOnCheckoutItemResultModel>
                {
                    Item("pack-a", AddOnCheckoutItemStatuses.RequiresAction, i =>
                    {
                        i.ClientSecret = "pi_a";
                        i.PaymentTransactionId = "txn-a";
                    }),
                    Item("pack-b", AddOnCheckoutItemStatuses.RequiresAction, i =>
                    {
                        i.ClientSecret = "pi_b";
                        i.PaymentTransactionId = "txn-b";
                    })
                }
            }));

        Assert.Equal(PackCheckoutStep.Authenticating, state.Step);
        Assert.Equal("pack-a", PackCheckoutMachine.CurrentPackCheckoutItem(state)?.AddOnId);

        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.ItemAuthenticated(state.Token!));
        Assert.Equal(PackCheckoutStep.Completing, state.Step);
        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.ItemCompleted(
            state.Token!,
            Item("pack-a", AddOnCheckoutItemStatuses.Active, i => i.SubscriptionId = "sub-a")));

        // On to the second pack, not straight to done.
        Assert.Equal(PackCheckoutStep.Authenticating, state.Step);
        Assert.Equal("pack-b", PackCheckoutMachine.CurrentPackCheckoutItem(state)?.AddOnId);

        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.ItemAuthenticated(state.Token!));
        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.ItemCompleted(
            state.Token!,
            Item("pack-b", AddOnCheckoutItemStatuses.Trialing, i => i.SubscriptionId = "sub-b")));

        Assert.Equal(PackCheckoutStep.Done, state.Step);
        Assert.Equal(
            new[] { AddOnCheckoutItemStatuses.Active, AddOnCheckoutItemStatuses.Trialing },
            Statuses(state.Results));
    }

    [Fact(DisplayName = "one pack failing leaves the rest of the basket alone")]
    public void OnePackFailingLeavesTheRestOfTheBasketAlone()
    {
        var state = Quoted(false);
        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.CheckoutRequested());
        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.CheckoutReceived(
            state.Token!,
            new AddOnCheckoutResultModel
            {
                Success = true,
                CheckoutId = "chk-1",
                Results = new List<AddOnCheckoutItemResultModel>
                {
                    Item("pack-a", AddOnCheckoutItemStatuses.RequiresAction, i => i.ClientSecret = "pi_a"),
                    Item("pack-b", AddOnCheckoutItemStatuses.RequiresAction, i => i.ClientSecret = "pi_b"),
                    Item("pack-c", AddOnCheckoutItemStatuses.Active)
                }
            }));

        state = PackCheckoutMachine.Transition(
            state,
            new PackCheckoutEvent.ItemAuthFailed(state.Token!, "Your bank declined the card"));
        Assert.Equal(PackCheckoutStep.Authenticating, state.Step);
        Assert.Equal(AddOnCheckoutItemStatuses.Failed, state.Results[0].Status);
        Assert.Equal("Your bank declined the card", state.Results[0].ErrorMessage);
        Assert.Equal("pack-b", PackCheckoutMachine.CurrentPackCheckoutItem(state)?.AddOnId);

        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.ItemAuthenticated(state.Token!));
        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.ItemCompleted(
            state.Token!,
            Item("pack-b", AddOnCheckoutItemStatuses.Active)));

        Assert.Equal(PackCheckoutStep.Done, state.Step);
        Assert.Equal(
            new[]
            {
                AddOnCheckoutItemStatuses.Failed,
                AddOnCheckoutItemStatuses.Active,
                AddOnCheckoutItemStatuses.Active
            },
            Statuses(state.Results));
    }

    [Fact(DisplayName = "ignores a result carrying a stale step token")]
    public void IgnoresAResultCarryingAStaleStepToken()
    {
        var state = PackCheckoutMachine.Transition(
            PackCheckoutMachine.InitialState(),
            new PackCheckoutEvent.QuoteRequested(
                "app-1",
                new[] { new AddOnCheckoutItemInput { AddOnId = "pack-a" } }));
        var stale = state.Token!;

        // The effect ran twice: the second attempt supersedes the first.
        state = PackCheckoutMachine.Transition(
            state,
            new PackCheckoutEvent.QuoteRequested(
                "app-1",
                new[] { new AddOnCheckoutItemInput { AddOnId = "pack-a" } }));
        var fresh = state.Token!;
        Assert.NotEqual(stale, fresh);

        var ignored = PackCheckoutMachine.Transition(
            state,
            new PackCheckoutEvent.QuoteReceived(stale, Quote(false)));
        Assert.Same(state, ignored);
        Assert.Equal(PackCheckoutStep.Quoting, ignored.Step);

        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.QuoteReceived(fresh, Quote(false)));
        Assert.Equal(PackCheckoutStep.Quoted, state.Step);
    }

    [Fact(DisplayName = "a refused quote fails with the server reason and can be retried")]
    public void ARefusedQuoteFailsWithTheServerReason()
    {
        var state = PackCheckoutMachine.Transition(
            PackCheckoutMachine.InitialState(),
            new PackCheckoutEvent.QuoteRequested(
                "app-1",
                new[] { new AddOnCheckoutItemInput { AddOnId = "pack-a" } }));

        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.QuoteReceived(
            state.Token!,
            new AddOnCheckoutQuoteModel
            {
                Success = false,
                CheckoutId = string.Empty,
                Currency = string.Empty,
                Lines = new List<AddOnCheckoutQuoteLineModel>(),
                TotalDueToday = 0,
                RequiresPaymentMethod = false,
                ErrorCode = AddOnCheckoutErrorCodes.AlreadySubscribed,
                ErrorMessage = "You already have that pack"
            }));

        Assert.Equal(PackCheckoutStep.Failed, state.Step);
        Assert.Equal("You already have that pack", state.Error);
        Assert.Equal(AddOnCheckoutErrorCodes.AlreadySubscribed, state.ErrorCode);

        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.Retry());
        Assert.Equal(PackCheckoutStep.Quoting, state.Step);
    }

    [Fact(DisplayName = "retrying the card form drops the SetupIntent the failed attempt was holding")]
    public void RetryingTheCardFormDropsTheStaleSetupIntent()
    {
        var state = Quoted(true);
        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.CardRequested());
        state = PackCheckoutMachine.Transition(
            state,
            new PackCheckoutEvent.CardIntentReceived(state.Token!, "seti_secret_first", "txn-first"));
        state = PackCheckoutMachine.Transition(
            state,
            new PackCheckoutEvent.CardFailed(state.Token!, "Card declined"));
        Assert.Equal(PackCheckoutStep.Failed, state.Step);

        state = PackCheckoutMachine.Transition(state, new PackCheckoutEvent.Retry());

        // Back on the card form with nothing to confirm: the driver collects a fresh intent, so the
        // second card is never confirmed against the first attempt's secret.
        Assert.Equal(PackCheckoutStep.CollectingCard, state.Step);
        Assert.Null(state.CardClientSecret);
        Assert.Null(state.PaymentTransactionId);
    }
}
