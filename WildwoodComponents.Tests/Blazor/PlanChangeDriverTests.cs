using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Components.RegistrationSubscription;
using WildwoodComponents.Blazor.Components.Subscription.Admin;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// Changing an existing subscriber's plan: preview, confirm, card, 3-D Secure, completion.
/// </summary>
/// <remarks>
/// Ported from packages/wildwood-react/src/__tests__/RegistrationAndSubscription.planChange.test.tsx
/// and SubscriptionAdminComponent.payment.test.tsx. The driver is exercised directly — the repo has
/// no bUnit renderer — with the real app-tier service over a scripted transport, so the requests
/// asserted here are the requests the server would receive.
///
/// The expensive rules are the ones being defended. A change is never posted twice, whatever a
/// double click does. A charge the bank wants to see is a "not yet", not a refusal: the parked
/// change is completed after the customer confirms it, and a server still applying it is asked
/// again rather than reported as a failure. A card the customer never gave changes nothing at all.
/// And the component collects that card itself — the host's own handler still wins when it has one.
/// </remarks>
public class PlanChangeDriverTests
{
    private const string PreviewRoute = "preview-change";
    private const string CompleteRoute = "/complete";
    private const string ChangeRoute = "my-subscription/change";
    private const string AdminChangeRoute = "app-tiers/change-tier";

    #region Harness

    /// <summary>
    /// A payment-action adapter that records what it was asked to confirm, can be held open
    /// mid-challenge, and owns a disposable "instance" the way the Stripe one does.
    /// </summary>
    private sealed class RecordingPaymentActions : IPaymentActionAdapter, IAsyncDisposable
    {
        private readonly Func<string, PaymentActionOutcome> _answer;

        public RecordingPaymentActions(Func<string, PaymentActionOutcome>? answer = null)
        {
            _answer = answer ?? (_ => PaymentActionOutcome.Success);
        }

        public List<string> Confirmed { get; } = new();

        public List<string?> Keys { get; } = new();

        /// <summary>Closed, a confirmation waits here: a customer part-way through a bank challenge.</summary>
        public TaskCompletionSource? Gate { get; set; }

        /// <summary>Signalled once a confirmation is actually waiting on <see cref="Gate"/>.</summary>
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Disposals { get; private set; }

        /// <summary>Read at disposal, so "dropped only after the drain" is a fact, not an order of calls.</summary>
        public Func<int>? CompletionsSoFar { get; set; }

        public int CompletionsAtDisposal { get; private set; } = -1;

        public async Task<PaymentActionOutcome> ConfirmPaymentAsync(string clientSecret, string? publishableKey)
        {
            Confirmed.Add(clientSecret);
            Keys.Add(publishableKey);

            var gate = Gate;
            if (gate is not null)
            {
                Reached.TrySetResult();
                await gate.Task;
            }

            return _answer(clientSecret);
        }

        public ValueTask DisposeAsync()
        {
            Disposals++;
            if (CompletionsSoFar is not null) CompletionsAtDisposal = CompletionsSoFar();
            return default;
        }
    }

    private sealed class Harness
    {
        public ScriptedHttpMessageHandler Handler { get; } = new();

        public FakePaymentProviderService Payments { get; } = new();

        public RecordingPaymentActions Actions { get; set; } = new();

        /// <summary>The host's own card modal, when the test gives it one.</summary>
        public Func<PaymentRequiredArgs, Task<string?>>? PaymentHandler { get; set; }

        public List<PaymentRequiredArgs> PaymentRequests { get; } = new();

        public int Renders { get; private set; }

        public int Refreshes { get; private set; }

        public List<string> Reasons { get; } = new();

        public List<(string Code, string Message)> Errors { get; } = new();

        /// <summary>Every entitlement invalidation the driver made on its own authority.</summary>
        public List<EntitlementsChangedEventArgs> Entitlements { get; } = new();

        public PlanChangeDriver Driver { get; private set; } = default!;

        public PlanChangeDriver Build(string? userId = null, string? companyId = null)
        {
            var client = Handler.CreateClient();
            var appTier = new AppTierComponentService(client, NullLogger<AppTierComponentService>.Instance);

            // The real service: Invalidate is a cache drop and an event, with no HTTP surface of
            // its own, so the reason asserted here is the reason a host would receive.
            var entitlements = new FeatureEntitlementService(
                appTier, NullLogger<FeatureEntitlementService>.Instance);
            entitlements.EntitlementsChangedDetailed += args => Entitlements.Add(args);

            Driver = new PlanChangeDriver(
                appTier,
                Payments,
                Actions,
                new PlanChangeSettings
                {
                    AppId = "app-1",
                    UserId = userId,
                    CompanyId = companyId,
                    Labels = RegistrationSubscriptionLabels.Defaults,

                    // The real 500 ms would be spent four times over in the bounded-retry test.
                    CompleteRetryDelay = TimeSpan.Zero
                },
                entitlements,
                NullLogger.Instance)
            {
                StateChanged = () => Renders++,
                Changed = () =>
                {
                    Refreshes++;
                    return Task.CompletedTask;
                },
                EntitlementsChanged = reason =>
                {
                    Reasons.Add(reason);
                    return Task.CompletedTask;
                },
                ErrorReported = (code, message) =>
                {
                    Errors.Add((code, message));
                    return Task.CompletedTask;
                }
            };

            if (PaymentHandler is not null)
            {
                Driver.PaymentRequested = request =>
                {
                    PaymentRequests.Add(request);
                    return PaymentHandler(request);
                };
            }

            return Driver;
        }
    }

    private static Harness WithStripeProvider(Harness harness, bool isDefault = true)
    {
        harness.Payments.Configuration = new AppPaymentConfigurationDto
        {
            DefaultProviderId = isDefault ? "prov-stripe" : null,
            Providers =
            {
                new PaymentProviderDto
                {
                    Id = "prov-other",
                    ProviderType = (int)PaymentProviderType.PayPal,
                    IsEnabled = true,
                    PublishableKey = "pk_paypal"
                },
                new PaymentProviderDto
                {
                    Id = "prov-stripe",
                    ProviderType = (int)PaymentProviderType.Stripe,
                    IsEnabled = true,
                    PublishableKey = "pk_test_1"
                }
            }
        };

        return harness;
    }

    private static TierSelectedEventArgs Pro(bool isChange = true)
    {
        return new TierSelectedEventArgs
        {
            TierId = "tier-pro",
            TierName = "Pro",
            PricingId = "price-pro-monthly",
            PricingModelId = "pm-monthly",
            Price = 79m,
            TrialDays = 14,
            IsChange = isChange
        };
    }

    private static string Preview(bool paymentRequired = false, bool bypassAllowed = false)
    {
        return $$"""
            {"success":true,"isUpgrade":true,"paymentRequired":{{(paymentRequired ? "true" : "false")}},
             "paymentBypassAllowed":{{(bypassAllowed ? "true" : "false")}},"paymentProviderAvailable":true,
             "newTierName":"Pro","currency":"USD","allowImmediateChange":true}
            """;
    }

    private const string ChangeOk = """{"success":true}""";

    private const string Parked =
        """
        {"success":false,"requiresAction":true,"clientSecret":"pi_secret","pendingChangeId":"pending-1",
         "currency":"USD"}
        """;

    private static readonly TierChangeConfirmOptions Immediate =
        new TierChangeConfirmOptions { Immediate = true, BypassPayment = false };

    private static JsonElement Body(ScriptedHttpMessageHandler.RecordedRequest request)
        => JsonDocument.Parse(request.Body ?? "{}").RootElement;

    /// <summary>Preview, confirm — the two clicks every test below starts with.</summary>
    private static async Task UpgradeAsync(PlanChangeDriver driver)
    {
        await driver.SelectTierAsync(Pro());
        await driver.ConfirmAsync(Immediate);
    }

    #endregion

    #region The plain change

    [Fact]
    public async Task PostsTheChangeThroughTheOptionsForm_SoTheServerMayParkIt()
    {
        var harness = new Harness();
        harness.Handler.On(PreviewRoute, Preview()).On(CompleteRoute, ChangeOk).On(ChangeRoute, ChangeOk);
        var driver = harness.Build();

        await UpgradeAsync(driver);

        Assert.Equal(PlanChangeStep.Done, driver.Step);

        var change = harness.Handler.Single(ChangeRoute);
        Assert.True(Body(change).GetProperty("SupportsPaymentAction").GetBoolean());
        Assert.Equal("tier-pro", Body(change).GetProperty("NewAppTierId").GetString());
        Assert.Equal("price-pro-monthly", Body(change).GetProperty("NewAppTierPricingId").GetString());
        Assert.True(Body(change).GetProperty("Immediate").GetBoolean());
    }

    /// <summary>
    /// The change landed: the host reloads, its callback is told why, and the shared entitlement
    /// cache is dropped so no FeatureGate keeps serving the old plan.
    /// </summary>
    [Fact]
    public async Task AChangeThatLands_RefreshesTheHost_AndDropsTheEntitlementCacheOnce()
    {
        var harness = new Harness();
        harness.Handler.On(PreviewRoute, Preview()).On(CompleteRoute, ChangeOk).On(ChangeRoute, ChangeOk);
        var driver = harness.Build();

        await UpgradeAsync(driver);

        Assert.Equal(1, harness.Refreshes);
        Assert.Equal(new[] { EntitlementsChangedReasons.TierChange }, harness.Reasons);

        var invalidation = Assert.Single(harness.Entitlements);
        Assert.Equal("app-1", invalidation.AppId);
        Assert.Equal(EntitlementsChangedReasons.TierChange, invalidation.Reason);
    }

    /// <summary>The step tokens' whole job: a double-clicked confirmation posts one change.</summary>
    [Fact]
    public async Task ConfirmingTwice_PostsOneChange()
    {
        var harness = new Harness();
        harness.Handler.On(PreviewRoute, Preview()).On(CompleteRoute, ChangeOk).On(ChangeRoute, ChangeOk);
        var driver = harness.Build();

        await driver.SelectTierAsync(Pro());
        var first = driver.ConfirmAsync(Immediate);
        var second = driver.ConfirmAsync(Immediate);
        await Task.WhenAll(first, second);

        Assert.Equal(1, harness.Handler.Count(ChangeRoute));
        Assert.Equal(1, harness.Handler.Count(PreviewRoute));
    }

    [Fact]
    public async Task CancellingTheConfirmation_ChangesNothing()
    {
        var harness = new Harness();
        harness.Handler.On(PreviewRoute, Preview()).On(ChangeRoute, ChangeOk);
        var driver = harness.Build();

        await driver.SelectTierAsync(Pro());
        Assert.Equal(PlanChangeStep.Confirm, driver.Step);

        await driver.CancelAsync();

        Assert.Equal(PlanChangeStep.Idle, driver.Step);
        Assert.Equal(0, harness.Handler.Count(ChangeRoute));
    }

    #endregion

    #region The card

    [Fact]
    public async Task CollectsTheCardItself_WithWhatThePlanWillBeBilledAs()
    {
        var harness = new Harness();
        harness.Handler.On(PreviewRoute, Preview(paymentRequired: true)).On(ChangeRoute, ChangeOk);
        var driver = harness.Build();

        await UpgradeAsync(driver);

        Assert.Equal(PlanChangeStep.CollectingPayment, driver.Step);

        var request = driver.PaymentRequest;
        Assert.NotNull(request);
        Assert.Equal("tier-pro", request!.TierId);
        Assert.Equal("Pro", request.TierName);

        // The pricing MODEL, not the tier-pricing link id: the payment starts the plan's own
        // subscription, and the server finds no model for a link id.
        Assert.Equal("pm-monthly", request.PricingModelId);
        Assert.Equal(79m, request.Price);
        Assert.Equal(14, request.TrialDays);

        // Nothing is posted until the card is answered for.
        Assert.Equal(0, harness.Handler.Count(ChangeRoute));
    }

    [Fact]
    public async Task TheCardsTransactionId_GoesOntoTheChange()
    {
        var harness = new Harness();
        harness.Handler.On(PreviewRoute, Preview(paymentRequired: true)).On(CompleteRoute, ChangeOk).On(ChangeRoute, ChangeOk);
        var driver = harness.Build();

        await UpgradeAsync(driver);
        await driver.ProvidePaymentAsync("txn-card");

        Assert.Equal(PlanChangeStep.Done, driver.Step);
        Assert.Equal("txn-card", Body(harness.Handler.Single(ChangeRoute)).GetProperty("PaymentTransactionId").GetString());
    }

    /// <summary>
    /// The built-in modal is inside this flow, so closing it returns to the confirmation the
    /// customer came from rather than throwing the priced change away.
    /// </summary>
    [Fact]
    public async Task ClosingTheBuiltInModal_ReturnsToTheConfirmation_AndChangesNothing()
    {
        var harness = new Harness();
        harness.Handler.On(PreviewRoute, Preview(paymentRequired: true)).On(ChangeRoute, ChangeOk);
        var driver = harness.Build();

        await UpgradeAsync(driver);
        await driver.ProvidePaymentAsync(null);

        Assert.Equal(PlanChangeStep.Confirm, driver.Step);
        Assert.NotNull(driver.Preview);
        Assert.Equal(0, harness.Handler.Count(ChangeRoute));
    }

    [Fact]
    public async Task TheHostsOwnModalWins_AndTheBuiltInOneIsNeverOffered()
    {
        var harness = new Harness { PaymentHandler = _ => Task.FromResult<string?>("txn-host") };
        harness.Handler.On(PreviewRoute, Preview(paymentRequired: true)).On(CompleteRoute, ChangeOk).On(ChangeRoute, ChangeOk);
        var driver = harness.Build();

        await UpgradeAsync(driver);

        var request = Assert.Single(harness.PaymentRequests);
        Assert.Equal("pm-monthly", request.PricingModelId);
        Assert.Equal(14, request.TrialDays);

        // The view must not mount a card modal of its own over the host's.
        Assert.Null(driver.PaymentRequest);
        Assert.Equal("txn-host", Body(harness.Handler.Single(ChangeRoute)).GetProperty("PaymentTransactionId").GetString());
    }

    /// <summary>
    /// The host owns that modal, so closing it is the customer walking away from the whole change
    /// — not a step back to a confirmation they have already dismissed.
    /// </summary>
    [Fact]
    public async Task AHostModalThatAnswersWithNothing_ResetsTheWholeChange()
    {
        var harness = new Harness { PaymentHandler = _ => Task.FromResult<string?>(null) };
        harness.Handler.On(PreviewRoute, Preview(paymentRequired: true)).On(ChangeRoute, ChangeOk);
        var driver = harness.Build();

        await UpgradeAsync(driver);

        Assert.Equal(PlanChangeStep.Idle, driver.Step);
        Assert.Null(driver.Preview);
        Assert.Equal(0, harness.Handler.Count(ChangeRoute));
    }

    [Fact]
    public async Task AHostModalThatThrows_StopsTheChangeAndSaysWhy()
    {
        var harness = new Harness
        {
            PaymentHandler = _ => throw new InvalidOperationException("the modal blew up")
        };
        harness.Handler.On(PreviewRoute, Preview(paymentRequired: true)).On(ChangeRoute, ChangeOk);
        var driver = harness.Build();

        await UpgradeAsync(driver);

        Assert.Equal(PlanChangeStep.Failed, driver.Step);
        Assert.Equal("the modal blew up", driver.Error);
        Assert.Equal(0, harness.Handler.Count(ChangeRoute));
    }

    /// <summary>An admin acting on somebody else's subscription is never asked for a card.</summary>
    [Fact]
    public async Task AnAdminScopedChange_CollectsNoCard_AndGoesThroughTheAdminEndpoint()
    {
        var harness = new Harness();
        harness.Handler
            .On("admin/preview-change", Preview(paymentRequired: true))
            .On(AdminChangeRoute, ChangeOk);
        var driver = harness.Build(userId: "user-9");

        await UpgradeAsync(driver);

        Assert.Equal(PlanChangeStep.Done, driver.Step);
        Assert.Null(driver.PaymentRequest);
        Assert.Equal(1, harness.Handler.Count(AdminChangeRoute));
        Assert.Equal(0, harness.Handler.Count(ChangeRoute));
        Assert.Equal(1, harness.Handler.Count("admin/preview-change/user-9"));
    }

    #endregion

    #region 3-D Secure

    [Fact]
    public async Task ConfirmsTheChargeWithTheBank_AndCompletesTheParkedChange()
    {
        var harness = WithStripeProvider(new Harness());
        harness.Handler
            .On(PreviewRoute, Preview())
            .On(CompleteRoute, ChangeOk)
            .On(ChangeRoute, Parked);
        var driver = harness.Build();

        await UpgradeAsync(driver);

        Assert.Equal("pi_secret", Assert.Single(harness.Actions.Confirmed));
        Assert.Equal("pk_test_1", Assert.Single(harness.Actions.Keys));
        Assert.Equal(1, harness.Handler.Count("change/pending-1/complete"));
        Assert.Equal(PlanChangeStep.Done, driver.Step);
        Assert.Equal(new[] { EntitlementsChangedReasons.TierChange }, harness.Reasons);
    }

    [Fact]
    public async Task ADeclinedChallenge_StopsTheChange_AndTryAgainPicksItBackUp()
    {
        var answers = 0;
        var harness = WithStripeProvider(new Harness());
        harness.Actions = new RecordingPaymentActions(_ =>
            answers++ == 0 ? PaymentActionOutcome.Failed("Your card was declined.") : PaymentActionOutcome.Success);
        harness.Handler
            .On(PreviewRoute, Preview())
            .On(CompleteRoute, ChangeOk)
            .On(ChangeRoute, Parked);
        var driver = harness.Build();

        await UpgradeAsync(driver);

        Assert.Equal(PlanChangeStep.Failed, driver.Step);
        Assert.Equal("Your card was declined.", driver.Error);
        Assert.Equal(0, harness.Handler.Count(CompleteRoute));
        Assert.Contains(
            (PlanChangeDriver.AuthenticationFailedCode, "Your card was declined."), harness.Errors);

        await driver.RetryAsync();

        Assert.Equal(2, harness.Actions.Confirmed.Count);
        Assert.Equal(1, harness.Handler.Count("change/pending-1/complete"));
        Assert.Equal(PlanChangeStep.Done, driver.Step);
    }

    /// <summary>
    /// A challenge with no Stripe account behind it cannot be put to anyone: the change stops and
    /// says so rather than looking as if it went through.
    /// </summary>
    [Fact]
    public async Task NoPublishableKey_StopsTheChangeWithTheUnconfirmedMessage()
    {
        var harness = new Harness();
        harness.Handler.On(PreviewRoute, Preview()).On(CompleteRoute, ChangeOk).On(ChangeRoute, Parked);
        var driver = harness.Build();

        await UpgradeAsync(driver);

        Assert.Equal(PlanChangeStep.Failed, driver.Step);
        Assert.Equal(PlanChangeDriver.ChargeUnconfirmed, driver.Error);
        Assert.Empty(harness.Actions.Confirmed);
    }

    [Fact]
    public async Task AServerStillApplyingTheChange_IsAskedAgain()
    {
        var harness = WithStripeProvider(new Harness());
        harness.Handler
            .On(PreviewRoute, Preview())
            .On(CompleteRoute, seen => (System.Net.HttpStatusCode.OK, seen == 0
                ? """{"success":false,"processing":true,"pendingChangeId":"pending-1"}"""
                : ChangeOk))
            .On(ChangeRoute, Parked);
        var driver = harness.Build();

        await UpgradeAsync(driver);

        Assert.Equal(2, harness.Handler.Count(CompleteRoute));
        Assert.Equal(PlanChangeStep.Done, driver.Step);
        Assert.Equal(1, harness.Refreshes);
    }

    /// <summary>
    /// The budget is the machine's, and it is not endless: five answers of "still applying" and
    /// the customer is told to come back rather than watched forever.
    /// </summary>
    [Fact]
    public async Task AServerThatNeverFinishes_IsAskedFiveTimesAndThenSaysSo()
    {
        var harness = WithStripeProvider(new Harness());
        harness.Handler
            .On(PreviewRoute, Preview())
            .On(CompleteRoute, """{"success":false,"processing":true,"pendingChangeId":"pending-1"}""")
            .On(ChangeRoute, Parked);
        var driver = harness.Build();

        await UpgradeAsync(driver);

        Assert.Equal(PlanChangeMachine.MaxPlanChangeCompleteAttempts, harness.Handler.Count(CompleteRoute));
        Assert.Equal(PlanChangeStep.Failed, driver.Step);
        Assert.Equal(
            "The payment went through but the plan change is still being applied. Refresh in a moment.",
            driver.Error);
        Assert.Equal(0, harness.Refreshes);
    }

    [Fact]
    public async Task AChangeMadeSomewhereElse_SaysSoInItsOwnWords()
    {
        var harness = WithStripeProvider(new Harness());
        harness.Handler
            .On(PreviewRoute, Preview())
            .On(CompleteRoute, """{"success":false,"errorCode":"pending_change_superseded","errorMessage":"Superseded"}""")
            .On(ChangeRoute, Parked);
        var driver = harness.Build();

        await UpgradeAsync(driver);

        Assert.Equal(PlanChangeStep.Failed, driver.Step);
        Assert.Equal(RegistrationSubscriptionLabels.Defaults.PlanChangeSuperseded, driver.Error);
        Assert.Contains(
            (TierChangeErrorCodes.PendingChangeSuperseded,
                RegistrationSubscriptionLabels.Defaults.PlanChangeSuperseded),
            harness.Errors);
    }

    [Fact]
    public async Task APaymentWindowThatClosed_SaysSoInItsOwnWords()
    {
        var harness = WithStripeProvider(new Harness());
        harness.Handler
            .On(PreviewRoute, Preview())
            .On(CompleteRoute, """{"success":false,"errorCode":"pending_change_expired","errorMessage":"Pending change expired"}""")
            .On(ChangeRoute, Parked);
        var driver = harness.Build();

        await UpgradeAsync(driver);

        Assert.Equal(RegistrationSubscriptionLabels.Defaults.PlanChangeExpired, driver.Error);
        Assert.Contains(
            (TierChangeErrorCodes.PendingChangeExpired, RegistrationSubscriptionLabels.Defaults.PlanChangeExpired),
            harness.Errors);
    }

    #endregion

    #region Refusals

    [Fact]
    public async Task APreviewTheServerCouldNotPrice_IsReported_AndChangesNothing()
    {
        var harness = new Harness();
        harness.Handler
            .On(PreviewRoute, """{"success":false,"errorMessage":"No such plan."}""")
            .On(ChangeRoute, ChangeOk);
        var driver = harness.Build();

        await driver.SelectTierAsync(Pro());

        Assert.Equal(PlanChangeStep.Failed, driver.Step);
        Assert.Null(driver.Preview);
        Assert.Equal(0, harness.Handler.Count(ChangeRoute));
        Assert.Equal((PlanChangeDriver.PreviewFailedCode, "No such plan."), Assert.Single(harness.Errors));
    }

    [Fact]
    public async Task AChangeTheServerSimplyRefused_IsReportedOnce()
    {
        var harness = new Harness();
        harness.Handler
            .On(PreviewRoute, Preview())
            .On(ChangeRoute, """{"success":false,"errorMessage":"That plan is not available."}""");
        var driver = harness.Build();

        await UpgradeAsync(driver);

        Assert.Equal(PlanChangeStep.Failed, driver.Step);
        Assert.Equal("That plan is not available.", driver.Error);
        Assert.Equal(
            (PlanChangeDriver.ChangeFailedCode, "That plan is not available."), Assert.Single(harness.Errors));
        Assert.Equal(0, harness.Refreshes);
    }

    #endregion

    #region Teardown

    /// <summary>
    /// Detach is not stop. A bank challenge is a live call that teardown neither cancels nor waits
    /// for: abandoning its answer would charge the customer's card and then never complete the
    /// parked change, so the change is DRAINED — server-side only, with no re-render and no host
    /// callback — and the entitlement cache is still dropped, because the plan really did move.
    /// </summary>
    [Fact]
    public async Task AChallengeAnsweredAfterTeardown_StillCompletesTheChange_Silently()
    {
        var harness = WithStripeProvider(new Harness());
        harness.Actions.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Actions.CompletionsSoFar = () => harness.Handler.Count(CompleteRoute);
        harness.Handler
            .On(PreviewRoute, Preview())
            .On(CompleteRoute, ChangeOk)
            .On(ChangeRoute, Parked);
        var driver = harness.Build();

        await driver.SelectTierAsync(Pro());
        var confirming = driver.ConfirmAsync(Immediate);
        await harness.Actions.Reached.Task;

        await driver.DisposeAsync();
        var rendersAtTeardown = harness.Renders;

        harness.Actions.Gate!.SetResult();
        await confirming;

        // The pack the bank said yes to is completed, and nothing tells a component that has gone.
        Assert.Equal(1, harness.Handler.Count("change/pending-1/complete"));
        Assert.Equal(rendersAtTeardown, harness.Renders);
        Assert.Equal(0, harness.Refreshes);
        Assert.Empty(harness.Reasons);

        // The cache still has to be dropped: the plan changed while nobody was watching.
        var invalidation = Assert.Single(harness.Entitlements);
        Assert.Equal(EntitlementsChangedReasons.TierChange, invalidation.Reason);

        // And the Stripe instance goes only AFTER the drain, never out from under it.
        Assert.Equal(1, harness.Actions.Disposals);
        Assert.Equal(1, harness.Actions.CompletionsAtDisposal);
    }

    /// <summary>A change not yet posted needs a customer who has left: it is not drained.</summary>
    [Fact]
    public async Task ATeardownBeforeTheCardIsAnswered_PostsNothing_AndDropsTheScriptInstance()
    {
        var harness = new Harness();
        harness.Handler.On(PreviewRoute, Preview(paymentRequired: true)).On(ChangeRoute, ChangeOk);
        var driver = harness.Build();

        await UpgradeAsync(driver);
        await driver.DisposeAsync();
        await driver.ProvidePaymentAsync("txn-card");

        Assert.Equal(0, harness.Handler.Count(ChangeRoute));
        Assert.Equal(1, harness.Actions.Disposals);
        Assert.Empty(harness.Entitlements);
    }

    [Fact]
    public async Task DisposingTwice_DropsTheScriptInstanceOnce()
    {
        var harness = new Harness();
        var driver = harness.Build();

        await driver.DisposeAsync();
        await driver.DisposeAsync();

        Assert.Equal(1, harness.Actions.Disposals);
    }

    #endregion
}
